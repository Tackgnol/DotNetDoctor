using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using RepoDoctor.Core.Catalog;
using RepoDoctor.Core.Json;
using RepoDoctor.Core.Policy;
using RepoDoctor.Core.Reporting;

namespace RepoDoctor.Analysis;

internal sealed record ProjectConfigurationResult(
    IReadOnlyList<Finding> Findings,
    IReadOnlyList<AnalysisProblem> Problems,
    IReadOnlyList<string> RulesAttempted,
    IReadOnlyList<string> RulesCompleted,
    bool Succeeded);

internal static class ProjectConfigurationChecks
{
    private static readonly string[] PropertyNames =
    [
        "Nullable",
        "EnableNETAnalyzers",
        "RunAnalyzers",
        "RunAnalyzersDuringBuild",
        "TreatWarningsAsErrors",
        "CodeAnalysisTreatWarningsAsErrors",
        "WarningsAsErrors",
        "WarningsNotAsErrors",
        "EnforceCodeStyleInBuild",
        "ManagePackageVersionsCentrally",
        "AnalysisLevel",
        "AnalysisMode",
        "NETCoreSdkVersion",
    ];

    private static readonly string[] AnalyzerExecutionProperties =
        ["EnableNETAnalyzers", "RunAnalyzers", "RunAnalyzersDuringBuild"];

    private static readonly string[] ConfigurationContextProperties = ["AnalysisLevel", "AnalysisMode"];

    public static async Task<ProjectConfigurationResult> AnalyzeAsync(
        LoadedProject project,
        IReadOnlyList<EffectiveRule> rules,
        DiagnosticCatalog catalog,
        string repositoryRoot,
        string configuration,
        string expectedSdkVersion,
        CancellationToken cancellationToken)
    {
        var applicable = rules
            .Where(r => r.Scope == RuleScope.All || !project.IsTestProject)
            .OrderBy(r => r.RuleId, StringComparer.Ordinal)
            .ToList();
        var attempted = applicable.Select(r => r.RuleId).ToList();
        if (attempted.Count == 0)
        {
            return new ProjectConfigurationResult([], [], [], [], Succeeded: true);
        }

        IReadOnlyDictionary<string, string> properties;
        try
        {
            properties = await EvaluateAsync(project, repositoryRoot, configuration, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or JsonException or System.ComponentModel.Win32Exception)
        {
            return new ProjectConfigurationResult(
                [],
                [Problem(project, ex.Message)],
                attempted,
                [],
                Succeeded: false);
        }

        if (!properties.TryGetValue("NETCoreSdkVersion", out var evaluatedSdk)
            || !string.Equals(evaluatedSdk, expectedSdkVersion, StringComparison.OrdinalIgnoreCase))
        {
            return new ProjectConfigurationResult(
                [],
                [Problem(project, $"evaluated SDK '{Display(evaluatedSdk)}' does not match workspace SDK '{expectedSdkVersion}'.")],
                attempted,
                [],
                Succeeded: false);
        }

        var byId = applicable.ToDictionary(r => r.RuleId, StringComparer.Ordinal);
        var findings = new List<Finding>();

        AddWhen("RD1001", !IsOneOf(Value("Nullable"), "enable", "warnings"),
            $"Nullable={Quoted(Value("Nullable"))}; expected 'enable' or 'warnings'.",
            ["Nullable"]);

        var disabledAnalyzers = AnalyzerExecutionProperties
            .Where(name => IsFalse(Value(name)))
            .ToList();
        AddWhen("RD1002", disabledAnalyzers.Count > 0,
            $"Analyzer execution is disabled by {string.Join(", ", disabledAnalyzers.Select(name => $"{name}={Quoted(Value(name))}"))}.",
            ["EnableNETAnalyzers", "RunAnalyzers", "RunAnalyzersDuringBuild"]);

        var warningGaps = new List<string>();
        if (!IsTrue(Value("TreatWarningsAsErrors")))
        {
            warningGaps.Add($"TreatWarningsAsErrors={Quoted(Value("TreatWarningsAsErrors"))}");
        }

        if (IsFalse(Value("CodeAnalysisTreatWarningsAsErrors")))
        {
            warningGaps.Add($"CodeAnalysisTreatWarningsAsErrors={Quoted(Value("CodeAnalysisTreatWarningsAsErrors"))}");
        }

        if (!string.IsNullOrWhiteSpace(Value("WarningsNotAsErrors")))
        {
            warningGaps.Add($"WarningsNotAsErrors={Quoted(Value("WarningsNotAsErrors"))}");
        }

        AddWhen("RD1003", warningGaps.Count > 0,
            $"Warnings are not comprehensively build-breaking: {string.Join(", ", warningGaps)}; WarningsAsErrors={Quoted(Value("WarningsAsErrors"))}.",
            ["TreatWarningsAsErrors", "CodeAnalysisTreatWarningsAsErrors", "WarningsAsErrors", "WarningsNotAsErrors"]);

        AddWhen("RD1004", !IsTrue(Value("EnforceCodeStyleInBuild")),
            $"EnforceCodeStyleInBuild={Quoted(Value("EnforceCodeStyleInBuild"))}; expected 'true'.",
            ["EnforceCodeStyleInBuild"]);

        AddWhen("RD1005", !IsTrue(Value("ManagePackageVersionsCentrally")),
            $"ManagePackageVersionsCentrally={Quoted(Value("ManagePackageVersionsCentrally"))}; expected 'true'.",
            ["ManagePackageVersionsCentrally"]);

        return new ProjectConfigurationResult(findings, [], attempted, attempted, Succeeded: true);

        string Value(string name) => properties.TryGetValue(name, out var value) ? value : string.Empty;

        void AddWhen(string ruleId, bool condition, string message, IReadOnlyList<string> evidenceProperties)
        {
            if (!condition || !byId.TryGetValue(ruleId, out var rule))
            {
                return;
            }

            findings.Add(CreateFinding(
                project,
                rule,
                catalog.Get(ruleId),
                configuration,
                $"{message} Context: AnalysisLevel={Quoted(Value("AnalysisLevel"))}, AnalysisMode={Quoted(Value("AnalysisMode"))}.",
                evidenceProperties
                    .Concat(ConfigurationContextProperties)
                    .Distinct(StringComparer.Ordinal)
                    .ToDictionary(name => name, Value, StringComparer.Ordinal)));
        }
    }

    private static async Task<IReadOnlyDictionary<string, string>> EvaluateAsync(
        LoadedProject project,
        string repositoryRoot,
        string configuration,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet",
            WorkingDirectory = repositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("msbuild");
        startInfo.ArgumentList.Add(project.AbsolutePath);
        startInfo.ArgumentList.Add($"-getProperty:{string.Join(',', PropertyNames)}");
        startInfo.ArgumentList.Add($"-property:Configuration={configuration}");
        if (project.TargetFrameworks.Count == 1 && project.TargetFrameworks[0] is not "unknown" and not "<unresolved>")
        {
            startInfo.ArgumentList.Add($"-property:TargetFramework={project.TargetFrameworks[0]}");
        }

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException("could not start dotnet msbuild property evaluation.");
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var stderrTask = process.StandardError.ReadToEndAsync(CancellationToken.None);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync(CancellationToken.None);
                }
            }
            catch (InvalidOperationException)
            {
            }

            await Task.WhenAll(stdoutTask, stderrTask);
            throw;
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"dotnet msbuild property evaluation exited with code {process.ExitCode}: {Display(stderr)}");
        }

        return ParseProperties(stdout);
    }

    internal static IReadOnlyDictionary<string, string> ParseProperties(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("Properties", out var properties)
            || properties.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("dotnet msbuild property evaluation did not return a Properties object.");
        }

        return properties.EnumerateObject().ToDictionary(
            property => property.Name,
            property => property.Value.ValueKind == JsonValueKind.String
                ? property.Value.GetString() ?? string.Empty
                : property.Value.GetRawText(),
            StringComparer.Ordinal);
    }

    private static Finding CreateFinding(
        LoadedProject project,
        EffectiveRule rule,
        CatalogEntry catalog,
        string configuration,
        string message,
        IReadOnlyDictionary<string, string> evidence)
    {
        var targetFramework = project.TargetFrameworks.Count == 1 ? project.TargetFrameworks[0] : null;
        var fingerprintNode = new JsonObject
        {
            ["v"] = FindingFactory.FingerprintVersion,
            ["sourceFamily"] = catalog.SourcePackage,
            ["ruleId"] = rule.RuleId,
            ["project"] = project.Identity,
            ["tfm"] = targetFramework,
            ["config"] = configuration,
        };
        var evidenceNode = new JsonObject();
        foreach (var (name, value) in evidence.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            evidenceNode[name] = value;
        }

        var fingerprint = FindingFactory.FingerprintVersion + ":" + CanonicalJson.Sha256Hex(fingerprintNode.ToJsonString());
        var evidenceHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            CanonicalJson.Canonicalize(evidenceNode.ToJsonString()))));

        return new Finding
        {
            Id = fingerprint,
            RuleId = rule.RuleId,
            Source = catalog.SourcePackage,
            Category = catalog.NormalizedCategory,
            Severity = rule.Severity,
            Confidence = catalog.Confidence,
            ConfidenceRationale = catalog.KnownLimitations,
            Message = message,
            ProjectPath = project.Identity,
            TargetFramework = targetFramework,
            Configuration = configuration,
            EvidenceHash = evidenceHash,
            Fingerprint = fingerprint,
            HelpUrl = catalog.HelpUrl,
            Remediation = catalog.ShortGuidance,
            FixAvailability = FixAvailability.Manual,
            PolicySource = rule.Source,
        };
    }

    private static AnalysisProblem Problem(LoadedProject project, string message) => new()
    {
        Severity = AnalysisProblemSeverity.Error,
        Stage = "project-configuration",
        Message = message,
        ProjectPath = project.Identity,
    };

    private static bool IsTrue(string value) => string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);

    private static bool IsFalse(string value) => string.Equals(value, "false", StringComparison.OrdinalIgnoreCase);

    private static bool IsOneOf(string value, params string[] expected) =>
        expected.Any(candidate => string.Equals(value, candidate, StringComparison.OrdinalIgnoreCase));

    private static string Quoted(string value) => $"'{Display(value)}'";

    private static string Display(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "<empty>";
        }

        var normalized = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length <= 500 ? normalized : normalized[..500] + "…";
    }
}
