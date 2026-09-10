using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using RepoDoctor.Core;
using RepoDoctor.Core.Catalog;
using RepoDoctor.Core.Json;
using RepoDoctor.Core.Policy;
using RepoDoctor.Core.Reporting;

namespace RepoDoctor.Analysis;

public sealed record NuGetAuditOccurrence(
    string ProjectPath,
    string TargetFramework,
    string PackageId,
    string ResolvedVersion,
    string AdvisoryUrl,
    string AdvisorySeverity,
    string Relationship);

public sealed record NuGetAuditOutput(
    IReadOnlyList<string> Sources,
    IReadOnlyList<NuGetAuditOccurrence> Occurrences);

public sealed record NuGetAuditResult(
    bool Succeeded,
    IReadOnlyList<string> Sources,
    IReadOnlyList<NuGetAuditOccurrence> Occurrences,
    IReadOnlyList<AnalysisProblem> Problems);

public static class NuGetAudit
{
    public static IReadOnlyList<Finding> ToFindings(
        NuGetAuditResult result,
        EffectiveRule rule,
        CatalogEntry catalog,
        string configuration)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(catalog);

        return result.Occurrences.Select(occurrence =>
        {
            var evidence = new JsonObject
            {
                ["source"] = catalog.SourcePackage,
                ["ruleId"] = catalog.Id,
                ["project"] = occurrence.ProjectPath,
                ["tfm"] = occurrence.TargetFramework,
                ["package"] = occurrence.PackageId,
                ["version"] = occurrence.ResolvedVersion,
                ["advisory"] = occurrence.AdvisoryUrl,
            };
            var evidenceHash = CanonicalJson.Sha256Hex(evidence.ToJsonString());
            var fingerprint = $"{FindingFactory.FingerprintVersion}:{evidenceHash}";
            var advisoryId = Uri.TryCreate(occurrence.AdvisoryUrl, UriKind.Absolute, out var uri)
                ? uri.Segments[^1].Trim('/')
                : occurrence.AdvisoryUrl;

            return new Finding
            {
                Id = fingerprint,
                RuleId = catalog.Id,
                Source = catalog.SourcePackage,
                Category = catalog.NormalizedCategory,
                Severity = rule.Severity,
                Confidence = catalog.Confidence,
                ConfidenceRationale = catalog.KnownLimitations,
                Message = $"{occurrence.PackageId} {occurrence.ResolvedVersion} has a {occurrence.AdvisorySeverity.ToLowerInvariant()} severity vulnerability ({advisoryId}).",
                ProjectPath = occurrence.ProjectPath,
                TargetFramework = occurrence.TargetFramework,
                Configuration = configuration,
                EvidenceHash = evidenceHash,
                Fingerprint = fingerprint,
                HelpUrl = occurrence.AdvisoryUrl,
                Remediation = catalog.ShortGuidance,
                FixAvailability = FixAvailability.Manual,
                PolicySource = rule.Source,
                Dependency = new DependencyEvidence
                {
                    PackageId = occurrence.PackageId,
                    ResolvedVersion = occurrence.ResolvedVersion,
                    AdvisoryId = advisoryId,
                    AdvisoryUrl = occurrence.AdvisoryUrl,
                    AdvisorySeverity = occurrence.AdvisorySeverity,
                    Relationship = occurrence.Relationship,
                },
            };
        }).ToList();
    }

    public static async Task<NuGetAuditResult> RunAsync(
        IReadOnlyList<LoadedProject> projects,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(projects);

        var sources = new HashSet<string>(StringComparer.Ordinal);
        var occurrences = new List<NuGetAuditOccurrence>();
        var problems = new List<AnalysisProblem>();

        foreach (var project in projects)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var process = new Process
            {
                StartInfo = StartInfo(project.AbsolutePath),
            };

            try
            {
                process.Start();
                var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
                var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
                await process.WaitForExitAsync(cancellationToken);
                var output = await stdout;
                var error = await stderr;

                if (process.ExitCode != 0 || !string.IsNullOrWhiteSpace(error))
                {
                    problems.Add(Problem(project, string.IsNullOrWhiteSpace(error)
                        ? $"dotnet package list exited with code {process.ExitCode}."
                        : error.Trim()));
                    continue;
                }

                var parsed = Parse(output, project.Identity);
                foreach (var source in parsed.Sources)
                {
                    sources.Add(SanitizeSource(source));
                }

                occurrences.AddRange(parsed.Occurrences);
            }
            catch (OperationCanceledException)
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }

                throw;
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or JsonException or RepoDoctorValidationException or System.ComponentModel.Win32Exception)
            {
                problems.Add(Problem(project, ex.Message));
            }
        }

        return new NuGetAuditResult(
            problems.Count == 0,
            sources.OrderBy(s => s, StringComparer.Ordinal).ToList(),
            occurrences
                .DistinctBy(o => (o.ProjectPath, o.TargetFramework, o.PackageId, o.ResolvedVersion, o.AdvisoryUrl, o.Relationship))
                .OrderBy(o => o.ProjectPath, StringComparer.Ordinal)
                .ThenBy(o => o.TargetFramework, StringComparer.Ordinal)
                .ThenBy(o => o.PackageId, StringComparer.Ordinal)
                .ThenBy(o => o.AdvisoryUrl, StringComparer.Ordinal)
                .ToList(),
            problems);
    }

    public static NuGetAuditOutput Parse(string json, string projectIdentity)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectIdentity);

        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("version", out var version)
            || version.ValueKind != JsonValueKind.Number
            || !version.TryGetInt32(out var outputVersion)
            || outputVersion != 1)
        {
            throw new RepoDoctorValidationException("NuGet audit output is not JSON output version 1.");
        }

        var sources = RequiredStringArray(root, "sources");
        if (sources.Count == 0)
        {
            throw new RepoDoctorValidationException("NuGet audit output did not identify a vulnerability source.");
        }

        if (root.TryGetProperty("problems", out var problems)
            && problems.ValueKind == JsonValueKind.Array
            && problems.GetArrayLength() > 0)
        {
            throw new RepoDoctorValidationException("NuGet audit output contains SDK problem records.");
        }

        var projects = RequiredArray(root, "projects");
        if (projects.GetArrayLength() == 0)
        {
            throw new RepoDoctorValidationException("NuGet audit output did not contain the requested project.");
        }

        var occurrences = new List<NuGetAuditOccurrence>();
        foreach (var project in projects.EnumerateArray())
        {
            _ = RequiredString(project, "path");
            if (!project.TryGetProperty("frameworks", out var frameworks))
            {
                continue;
            }

            RequireArray(frameworks, "projects[].frameworks");
            foreach (var framework in frameworks.EnumerateArray())
            {
                var targetFramework = RequiredString(framework, "framework");
                AddPackages(framework, "topLevelPackages", "direct", projectIdentity, targetFramework, occurrences);
                AddPackages(framework, "transitivePackages", "transitive", projectIdentity, targetFramework, occurrences);
            }
        }

        return new NuGetAuditOutput(sources, occurrences);
    }

    private static ProcessStartInfo StartInfo(string projectPath)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = Path.GetDirectoryName(projectPath)!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in new[]
                 {
                     "package", "list", "--project", projectPath, "--vulnerable", "--include-transitive",
                     "--format", "json", "--output-version", "1", "--no-restore",
                 })
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    private static void AddPackages(
        JsonElement framework,
        string propertyName,
        string relationship,
        string projectIdentity,
        string targetFramework,
        List<NuGetAuditOccurrence> occurrences)
    {
        if (!framework.TryGetProperty(propertyName, out var packages))
        {
            return;
        }

        RequireArray(packages, $"frameworks[].{propertyName}");
        foreach (var package in packages.EnumerateArray())
        {
            var packageId = RequiredString(package, "id");
            var resolvedVersion = RequiredString(package, "resolvedVersion");
            var vulnerabilities = RequiredArray(package, "vulnerabilities");
            foreach (var vulnerability in vulnerabilities.EnumerateArray())
            {
                occurrences.Add(new NuGetAuditOccurrence(
                    projectIdentity,
                    targetFramework,
                    packageId,
                    resolvedVersion,
                    RequiredString(vulnerability, "advisoryurl"),
                    RequiredString(vulnerability, "severity"),
                    relationship));
            }
        }
    }

    private static List<string> RequiredStringArray(JsonElement owner, string propertyName)
    {
        var array = RequiredArray(owner, propertyName);
        var values = new List<string>();
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(item.GetString()))
            {
                throw new RepoDoctorValidationException($"NuGet audit output property '{propertyName}' must contain strings.");
            }

            values.Add(item.GetString()!);
        }

        return values;
    }

    private static JsonElement RequiredArray(JsonElement owner, string propertyName)
    {
        if (!owner.TryGetProperty(propertyName, out var value))
        {
            throw new RepoDoctorValidationException($"NuGet audit output is missing '{propertyName}'.");
        }

        RequireArray(value, propertyName);
        return value;
    }

    private static void RequireArray(JsonElement value, string propertyName)
    {
        if (value.ValueKind != JsonValueKind.Array)
        {
            throw new RepoDoctorValidationException($"NuGet audit output property '{propertyName}' must be an array.");
        }
    }

    private static string RequiredString(JsonElement owner, string propertyName)
    {
        if (!owner.TryGetProperty(propertyName, out var value)
            || value.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw new RepoDoctorValidationException($"NuGet audit output is missing string property '{propertyName}'.");
        }

        return value.GetString()!;
    }

    private static string SanitizeSource(string source)
    {
        if (!Uri.TryCreate(source, UriKind.Absolute, out var uri) || uri.IsFile)
        {
            return source;
        }

        var safe = new UriBuilder(uri)
        {
            UserName = string.Empty,
            Password = string.Empty,
            Query = string.Empty,
            Fragment = string.Empty,
        };
        return safe.Uri.GetLeftPart(UriPartial.Path);
    }

    private static AnalysisProblem Problem(LoadedProject project, string message) => new()
    {
        Severity = AnalysisProblemSeverity.Error,
        Stage = "dependency-audit",
        Message = message,
        ProjectPath = project.Identity,
    };
}
