using System.Reflection;
using System.Text.Json;
using RepoDoctor.Analysis;
using RepoDoctor.Core;
using RepoDoctor.Core.Catalog;
using RepoDoctor.Core.Policy;
using RepoDoctor.Core.Reporting;

namespace RepoDoctor.Cli;

public static class CliRunner
{
    public static async Task<int> RunAsync(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (args.Length == 0 || IsHelp(args[0]))
        {
            Console.Error.WriteLine(HelpText);
            return args.Length == 0 ? 2 : 0;
        }

        if (args[0] is "--version" or "version")
        {
            Console.Out.WriteLine(ToolVersion());
            return 0;
        }

        if (args[0] != "scan")
        {
            return RunUtilityCommand(args);
        }

        var rest = args.Skip(1).ToArray();
        if (rest.Length == 1 && IsHelp(rest[0]))
        {
            Console.Error.WriteLine(ScanHelpText);
            return 0;
        }

        ScanArguments parsed;
        try
        {
            parsed = ScanArguments.Parse(rest);
        }
        catch (RepoDoctorValidationException ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            Console.Error.WriteLine(ScanHelpText);
            return 2;
        }

        ResolvedPolicy policy;
        try
        {
            policy = ResolvePolicy(parsed);
        }
        catch (RepoDoctorValidationException ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 2;
        }
        catch (IOException ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 2;
        }

        if (parsed.OutputPath is { } outputPath)
        {
            var reject = ValidateOutputPath(outputPath, parsed, policy);
            if (reject is not null)
            {
                Console.Error.WriteLine($"error: {reject}");
                return 2;
            }
        }

        Console.Error.WriteLine(
            $"profile: {policy.Policy.ProfileId}@{policy.Policy.ProfileVersion} " +
            $"{Short(policy.Policy.PolicyHash)} (source: {policy.Source})");

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        var request = new ScanRequest
        {
            Target = parsed.Target,
            RepositoryConfigPath = parsed.ConfigPath,
            ToolVersion = ToolVersion(),
            Policy = policy.Policy,
            Catalog = policy.Catalog,
            BaselinePath = parsed.BaselinePath,
            Audit = parsed.Audit,
            TimeoutSeconds = parsed.TimeoutSeconds,
        };

        ScanOutcome outcome;
        try
        {
            outcome = await ScanEngine.RunAsync(request, cts.Token);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Console.Error.WriteLine($"error: unexpected failure during scan: {ex.Message}");
            return 2;
        }

        WriteReport(outcome.Report, parsed);
        return outcome.ExitCode;
    }

    private static int RunUtilityCommand(string[] args)
    {
        try
        {
            return args[0] switch
            {
                "init" => RunInit(args[1..]),
                "profile" => RunProfile(args[1..]),
                "config" => RunConfig(args[1..]),
                "rule" => RunRule(args[1..]),
                _ => UnknownCommand(args[0]),
            };
        }
        catch (Exception ex) when (ex is RepoDoctorValidationException or IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 2;
        }
    }

    private static int RunInit(string[] args)
    {
        if (args.Length == 1 && IsHelp(args[0]))
        {
            Console.Error.WriteLine(InitHelpText);
            return 0;
        }

        if (args.Length != 1)
        {
            throw new RepoDoctorValidationException("usage: repo-doctor init <directory>");
        }

        var root = Path.GetFullPath(args[0]);
        var profilePath = Path.Combine(root, ".repo-doctor", "profile.json");
        var configPath = Path.Combine(root, ".repo-doctor.json");
        if (File.Exists(profilePath) || File.Exists(configPath))
        {
            throw new RepoDoctorValidationException("init refuses to overwrite .repo-doctor/profile.json or .repo-doctor.json.");
        }

        Directory.CreateDirectory(root);
        AtomicWrite(profilePath, EmbeddedProfile.RecommendedJson());
        AtomicWrite(configPath, InitialConfigJson);
        Console.Out.WriteLine($"created {RelativeOrName(profilePath)}");
        Console.Out.WriteLine($"created {RelativeOrName(configPath)}");
        return 0;
    }

    private static int RunProfile(string[] args)
    {
        if (args.Length == 0 || IsHelp(args[0]))
        {
            Console.Error.WriteLine(ProfileHelpText);
            return args.Length == 0 ? 2 : 0;
        }

        return args[0] switch
        {
            "validate" => RunProfileValidate(args.Skip(1).ToArray()),
            "export" => RunProfileExport(args.Skip(1).ToArray()),
            _ => throw new RepoDoctorValidationException($"unknown profile command '{args[0]}'."),
        };
    }

    private static int RunProfileValidate(string[] args)
    {
        if (args.Length == 1 && IsHelp(args[0]))
        {
            Console.Error.WriteLine(ProfileValidateHelpText);
            return 0;
        }

        if (args.Length != 1)
        {
            throw new RepoDoctorValidationException("usage: repo-doctor profile validate <file>");
        }

        var path = Path.GetFullPath(args[0]);
        var profile = ProfileDocument.Parse(File.ReadAllText(path), DiagnosticCatalog.LoadDefault(), path);
        Console.Out.WriteLine($"valid: {profile.Profile.Id}@{profile.Profile.Version} sha256:{profile.Hash}");
        return 0;
    }

    private static int RunProfileExport(string[] args)
    {
        if (args.Length == 1 && IsHelp(args[0]))
        {
            Console.Error.WriteLine(ProfileExportHelpText);
            return 0;
        }

        if (args.Length != 3 || args[1] != "--output")
        {
            throw new RepoDoctorValidationException("usage: repo-doctor profile export <directory> --output <file>");
        }

        var outputPath = Path.GetFullPath(args[2]);
        if (File.Exists(outputPath))
        {
            throw new RepoDoctorValidationException($"output '{outputPath}' already exists; refusing to overwrite it.");
        }

        var resolved = ResolvePolicy(args[0], configArgument: null, requireConfig: true);
        var exported = new Profile
        {
            SchemaVersion = PolicyValidation.SupportedSchemaVersion,
            Id = "local/exported",
            Version = "0.0.0",
            Title = $"Exported from {resolved.Profile.Profile.Id}",
            Description = "Flattened policy export. Change id, version, title, authors, and description before publishing.",
            TargetContext = resolved.Profile.Profile.TargetContext,
            Authors = resolved.Profile.Profile.Authors,
            DerivedFrom =
            [
                new DerivedProfile
                {
                    Id = resolved.Profile.Profile.Id,
                    Version = resolved.Profile.Profile.Version,
                    Sha256 = resolved.Profile.Hash,
                },
            ],
            EngineMajor = resolved.Policy.EngineMajor,
            Rules = resolved.Policy.Rules.ToDictionary(
                r => r.RuleId,
                r => new ProfileRule { Severity = r.Severity, Scope = r.Scope },
                StringComparer.Ordinal),
            Gate = new GatePolicy
            {
                MinimumSeverity = resolved.Policy.Gate.MinimumSeverity,
                MinimumConfidence = resolved.Policy.Gate.MinimumConfidence,
            },
        };

        var json = JsonSerializer.Serialize(exported, ReportJson.Options) + "\n";
        _ = ProfileDocument.Parse(json, resolved.Catalog, "exported profile");
        AtomicWrite(outputPath, json);
        Console.Out.WriteLine($"exported {RelativeOrName(outputPath)}");
        Console.Out.WriteLine("edit id/version and authorship metadata before publishing");
        return 0;
    }

    private static int RunConfig(string[] args)
    {
        if (args.Length == 0 || IsHelp(args[0]))
        {
            Console.Error.WriteLine(ConfigHelpText);
            return args.Length == 0 ? 2 : 0;
        }

        if (args.Length == 2 && args[0] == "explain" && IsHelp(args[1]))
        {
            Console.Error.WriteLine(ConfigHelpText);
            return 0;
        }

        if (args[0] != "explain" || args.Length != 2)
        {
            throw new RepoDoctorValidationException("usage: repo-doctor config explain <directory>");
        }

        var resolved = ResolvePolicy(args[1], configArgument: null, requireConfig: true);
        Console.Out.WriteLine($"profile: {resolved.Policy.ProfileId}@{resolved.Policy.ProfileVersion}");
        Console.Out.WriteLine($"profile hash: sha256:{resolved.Policy.ProfileHash}");
        Console.Out.WriteLine($"effective policy hash: {resolved.Policy.PolicyHash}");
        Console.Out.WriteLine($"source: {resolved.Source}");
        Console.Out.WriteLine($"bundle: {string.Join(", ", resolved.Catalog.Bundle.Select(b => $"{b.Package} {b.Version}"))}");
        Console.Out.WriteLine($"bundle hash: {resolved.Catalog.Hash}");
        Console.Out.WriteLine($"gate: severity >= {resolved.Policy.Gate.MinimumSeverity.ToString().ToLowerInvariant()}, confidence >= {resolved.Policy.Gate.MinimumConfidence.ToString().ToLowerInvariant()}");
        Console.Out.WriteLine($"overrides: {resolved.Config?.Rules.Count ?? 0}");
        var overrides = resolved.Config?.Rules.OrderBy(r => r.Key, StringComparer.Ordinal)
            ?? Enumerable.Empty<KeyValuePair<string, RepositoryRuleOverride>>();
        foreach (var (id, rule) in overrides)
        {
            Console.Out.WriteLine($"  {id}: severity={rule.Severity?.ToString().ToLowerInvariant() ?? "inherited"}, scope={rule.Scope?.ToString().ToLowerInvariant() ?? "inherited"}");
        }

        Console.Out.WriteLine($"exclusions: {(resolved.Policy.Excludes.Count == 0 ? "none" : string.Join(", ", resolved.Policy.Excludes))}");
        Console.Out.WriteLine(".editorconfig, NoWarn, and source suppressions take final per-file effect during scan; inspect the scan report's project policy digests and skipped rules.");
        return 0;
    }

    private static int RunRule(string[] args)
    {
        if (args.Length == 0 || IsHelp(args[0]))
        {
            Console.Error.WriteLine(RuleHelpText);
            return args.Length == 0 ? 2 : 0;
        }

        if (args.Length == 2 && args[0] == "explain" && IsHelp(args[1]))
        {
            Console.Error.WriteLine(RuleHelpText);
            return 0;
        }

        if (args[0] != "explain" || args.Length != 2)
        {
            throw new RepoDoctorValidationException("usage: repo-doctor rule explain <rule-id>");
        }

        var catalog = DiagnosticCatalog.LoadDefault();
        var entry = catalog.Get(args[1]);
        var package = catalog.Bundle.FirstOrDefault(b => b.Package == entry.SourcePackage);
        Console.Out.WriteLine($"{entry.Id} — {entry.SelectionIntent}");
        Console.Out.WriteLine($"source: {entry.SourcePackage}{(package is null ? string.Empty : $" {package.Version}")}");
        Console.Out.WriteLine($"category: {entry.NormalizedCategory} (upstream: {entry.UpstreamCategory})");
        Console.Out.WriteLine($"confidence: {entry.Confidence.ToString().ToLowerInvariant()}");
        Console.Out.WriteLine($"guidance: {entry.ShortGuidance}");
        Console.Out.WriteLine($"limitations: {(string.IsNullOrWhiteSpace(entry.KnownLimitations) ? "none documented" : entry.KnownLimitations)}");
        Console.Out.WriteLine($"fix: {entry.UpstreamFixer.ToString().ToLowerInvariant()}");
        Console.Out.WriteLine($"help: {entry.HelpUrl}");
        return 0;
    }

    private static int UnknownCommand(string command)
    {
        Console.Error.WriteLine($"unknown command '{command}'.");
        Console.Error.WriteLine(HelpText);
        return 2;
    }

    private static void WriteReport(ScanReport report, ScanArguments args)
    {
        var content = args.Format == OutputFormat.Json
            ? ReportJson.Serialize(report)
            : args.Format == OutputFormat.Sarif
                ? SarifWriter.Serialize(report)
            : ConsoleReport.Render(report);

        if (args.OutputPath is { } path)
        {
            AtomicWrite(path, content);
            Console.Error.WriteLine($"report written to {path}");
            return;
        }

        Console.Out.Write(content);
        if (!content.EndsWith('\n'))
        {
            Console.Out.Write('\n');
        }
    }

    private static string? ValidateOutputPath(string outputPath, ScanArguments args, ResolvedPolicy policy)
    {
        if (PathsEqual(outputPath, args.ConfigPath))
        {
            return "--output would overwrite the repository config file.";
        }

        if (PathsEqual(outputPath, policy.ProfilePath))
        {
            return "--output would overwrite the selected profile file.";
        }

        if (PathsEqual(outputPath, args.BaselinePath))
        {
            return "--output would overwrite the baseline report.";
        }

        if (PathsEqual(outputPath, Path.GetFullPath(args.Target)))
        {
            return "--output would overwrite the scan target.";
        }

        if (!File.Exists(outputPath))
        {
            return null;
        }

        if (new FileInfo(outputPath).Length == 0)
        {
            return null;
        }

        if (args.Format == OutputFormat.Json)
        {
            try
            {
                _ = ReportJson.Deserialize(File.ReadAllText(outputPath));
                return null;
            }
            catch (Exception ex) when (ex is RepoDoctorValidationException or System.Text.Json.JsonException)
            {
                return $"--output '{outputPath}' already exists and is not a repo-doctor JSON report; refusing to overwrite it.";
            }
        }

        return $"--output '{outputPath}' already exists; refusing to overwrite a non-empty file with a console report.";
    }

    private static void AtomicWrite(string path, string content)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temp = path + ".tmp-" + Guid.NewGuid().ToString("n");
        File.WriteAllText(temp, content);
        File.Move(temp, path, overwrite: true);
    }

    private sealed record ResolvedPolicy(
        EffectivePolicy Policy,
        DiagnosticCatalog Catalog,
        string Source,
        string? ProfilePath,
        LoadedProfile Profile,
        RepositoryConfig? Config);

    private static ResolvedPolicy ResolvePolicy(ScanArguments args)
    {
        if (args.ProfilePath is null)
        {
            return ResolvePolicy(args.Target, args.ConfigPath, requireConfig: false);
        }

        var catalog = DiagnosticCatalog.LoadDefault();
        var profile = ProfileDocument.Parse(File.ReadAllText(args.ProfilePath), catalog, args.ProfilePath);
        var policy = ProfileResolver.Resolve(profile.Profile, profile.Hash, repositoryConfig: null, catalog);
        return new ResolvedPolicy(
            policy,
            catalog,
            $"explicit profile {RelativeOrName(args.ProfilePath)}",
            args.ProfilePath,
            profile,
            Config: null);
    }

    private static ResolvedPolicy ResolvePolicy(string target, string? configArgument, bool requireConfig)
    {
        var catalog = DiagnosticCatalog.LoadDefault();

        var configPath = configArgument ?? DiscoverConfigPath(target);
        if (configPath is not null)
        {
            var config = RepositoryConfigDocument.Parse(File.ReadAllText(configPath), catalog, configPath);
            var profilePath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(configPath)!, config.Profile));
            if (!File.Exists(profilePath))
            {
                throw new RepoDoctorValidationException($"selected profile '{config.Profile}' was not found at {profilePath}.");
            }

            EnsureInsideRepository(profilePath, Path.GetDirectoryName(configPath)!);
            var profile = ProfileDocument.Parse(File.ReadAllText(profilePath), catalog, profilePath);
            var policy = ProfileResolver.Resolve(profile.Profile, profile.Hash, config, catalog);
            return new ResolvedPolicy(policy, catalog, RelativeOrName(configPath), profilePath, profile, config);
        }

        if (requireConfig)
        {
            throw new RepoDoctorValidationException($"no .repo-doctor.json found at or above '{Path.GetFullPath(target)}'.");
        }

        var recommended = EmbeddedProfile.LoadRecommended(catalog);
        var recommendedPolicy = ProfileResolver.Resolve(recommended.Profile, recommended.Hash, repositoryConfig: null, catalog);
        return new ResolvedPolicy(recommendedPolicy, catalog, "embedded recommended profile", ProfilePath: null, recommended, Config: null);
    }

    private static string? DiscoverConfigPath(string target)
    {
        var full = Path.GetFullPath(target);
        var directory = File.Exists(full) ? Path.GetDirectoryName(full)! : full;
        for (var current = new DirectoryInfo(directory); current is not null; current = current.Parent)
        {
            var candidate = Path.Combine(current.FullName, ".repo-doctor.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static void EnsureInsideRepository(string profilePath, string repositoryRoot)
    {
        var resolved = new FileInfo(profilePath).ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? profilePath;
        var relative = Path.GetRelativePath(Path.GetFullPath(repositoryRoot), Path.GetFullPath(resolved));
        if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
        {
            throw new RepoDoctorValidationException("selected profile must resolve inside the repository root.");
        }
    }

    private static bool IsHelp(string arg) => arg is "-h" or "--help" or "help";

    private static bool PathsEqual(string a, string? b) =>
        b is not null && string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    private static string RelativeOrName(string path) =>
        Path.GetRelativePath(Directory.GetCurrentDirectory(), path).Replace('\\', '/');

    private static string Short(string hash) => hash.Length <= 12 ? hash : hash[..12];

    private static string ToolVersion()
    {
        var assembly = typeof(CliRunner).Assembly;
        return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
            ?? assembly.GetName().Version?.ToString(3)
            ?? "0.0.0";
    }

    private const string HelpText = """
        repo-doctor - scan a C#/.NET repository against a shared analyzer policy

        usage:
          repo-doctor init <directory>
          repo-doctor scan <target> [options]
          repo-doctor profile validate <file>
          repo-doctor profile export <directory> --output <file>
          repo-doctor config explain <directory>
          repo-doctor rule explain <rule-id>
          repo-doctor --version
          repo-doctor --help

        commands:
          init      vendor this release's recommended profile and configuration
          scan   run the selected analyzer policy over a solution or project
          profile   validate or export a profile
          config    explain a repository's resolved policy
          rule      explain a supported rule
        """;

    private const string ScanHelpText = """
        usage: repo-doctor scan <target> [options]

          <target>                 a .sln, .slnx, .csproj, or a directory containing one

        options:
          --config <path>          repository configuration file (.repo-doctor.json)
          --profile <path>         profile file to use directly (cannot be combined with --config)
          --baseline <path>         an earlier complete JSON scan report to compare against
          --audit                   request the optional NuGet vulnerability audit
          --format console|json    output format (default: console)
          --output <path>          write the report to a file instead of stdout
          --timeout-seconds <n>    operational timeout, positive integer (default: 300)
          -h, --help               show this help

        exit codes:
          0  analysis completed and the gate passed
          1  analysis completed and policy-blocking findings exist
          2  invalid input, incomplete or failed analysis, or write failure
          130 interrupted
        """;

    private const string InitHelpText = "usage: repo-doctor init <directory>";

    private const string ProfileHelpText = """
        usage:
          repo-doctor profile validate <file>
          repo-doctor profile export <directory> --output <file>
        """;

    private const string ProfileValidateHelpText = "usage: repo-doctor profile validate <file>";

    private const string ProfileExportHelpText = "usage: repo-doctor profile export <directory> --output <file>";

    private const string ConfigHelpText = "usage: repo-doctor config explain <directory>";

    private const string RuleHelpText = "usage: repo-doctor rule explain <rule-id>";

    private const string InitialConfigJson = """
        {
          "schemaVersion": 1,
          "profile": ".repo-doctor/profile.json",
          "rules": {},
          "exclude": []
        }
        """;
}
