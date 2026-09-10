using System.Security.Cryptography;
using System.Text.Json;
using RepoDoctor.Analysis;
using RepoDoctor.Core.Catalog;
using RepoDoctor.Core.Policy;
using RepoDoctor.Core.Reporting;
using Xunit;

namespace RepoDoctor.IntegrationTests;

public sealed class ScanEngineTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "RepoDoctor.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }

    private static ScanRequest RequestFor(string target, string profileFile = "bootstrap-server-core.json")
    {
        var root = RepoRoot();
        var catalog = DiagnosticCatalog.LoadDefault();
        var profileJson = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "profiles", profileFile));
        var profile = ProfileDocument.Parse(profileJson, catalog, "bootstrap");
        var policy = ProfileResolver.Resolve(profile.Profile, profile.Hash, repositoryConfig: null, catalog);

        return new ScanRequest
        {
            Target = Path.Combine(root, target),
            RepositoryConfigPath = Path.Combine(root, ".repo-doctor.json"),
            ToolVersion = "0.0.0-test",
            Policy = policy,
            Catalog = catalog,
            TimeoutSeconds = 300,
        };
    }

    [Fact]
    public async Task Scan_reports_the_seeded_CA2200_and_honours_the_pragma_suppression()
    {
        var outcome = await ScanEngine.RunAsync(RequestFor("spike/fixtures/BadProject/BadProject.csproj"), CancellationToken.None);

        Assert.Equal(Completeness.Complete, outcome.Report.Analysis.Completeness);
        Assert.Equal(0, outcome.ExitCode);
        Assert.Equal(GateStatus.Passed, outcome.Report.Gate.Status);

        var ca2200 = Assert.Single(outcome.Report.Findings, f => f.RuleId == "CA2200");
        Assert.Equal("spike/fixtures/BadProject/Rethrower.cs", ca2200.Location!.FilePath);
        Assert.Equal(15, ca2200.Location.StartLine);
        Assert.StartsWith("fp1:", ca2200.Fingerprint);
        Assert.Equal("M:BadProject.Rethrower.RethrowLosesStackTrace_Reported", ca2200.SymbolId);
        Assert.Equal("Microsoft.CodeAnalysis.NetAnalyzers", ca2200.Source);
        Assert.Equal(
            StageStatus.Disabled,
            Assert.Single(outcome.Report.Analysis.Stages, stage => stage.Name == "project-configuration").Status);
    }

    [Fact]
    public async Task Scan_does_not_modify_tracked_source_or_project_files()
    {
        var root = RepoRoot();
        var fixtureDir = Path.Combine(root, "spike", "fixtures", "BadProject");
        var tracked = Directory.EnumerateFiles(fixtureDir, "*", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        var before = tracked.ToDictionary(p => p, Hash, StringComparer.Ordinal);

        await ScanEngine.RunAsync(RequestFor("spike/fixtures/BadProject/BadProject.csproj"), CancellationToken.None);

        foreach (var (path, hash) in before)
        {
            Assert.Equal(hash, Hash(path));
        }
    }

    [Fact]
    public async Task Missing_target_returns_failed_completeness_and_exit_2()
    {
        var outcome = await ScanEngine.RunAsync(RequestFor("does/not/exist.slnx"), CancellationToken.None);

        Assert.Equal(Completeness.Failed, outcome.Report.Analysis.Completeness);
        Assert.Equal(2, outcome.ExitCode);
        Assert.Equal(GateStatus.NotEvaluated, outcome.Report.Gate.Status);
        Assert.NotEmpty(outcome.Report.Analysis.Problems);
    }

    [Fact]
    public async Task Code_quality_profile_reports_source_and_evaluated_configuration_findings()
    {
        var request = RequestFor(
            "tests/Fixtures/CodeQualityGaps/CodeQualityGaps.csproj",
            "dotnet-code-quality.json");

        var outcome = await ScanEngine.RunAsync(request, CancellationToken.None);

        Assert.True(
            outcome.Report.Analysis.Completeness == Completeness.Complete,
            ReportJson.Serialize(outcome.Report));
        Assert.Equal(1, outcome.ExitCode);
        Assert.Equal(
            StageStatus.Complete,
            Assert.Single(outcome.Report.Analysis.Stages, stage => stage.Name == "project-configuration").Status);
        Assert.Equal(
            ["RD1001", "RD1002", "RD1003", "RD1004", "RD1005"],
            outcome.Report.Findings
                .Where(finding => finding.RuleId.StartsWith("RD", StringComparison.Ordinal))
                .Select(finding => finding.RuleId));
        Assert.All(
            outcome.Report.Findings.Where(finding => finding.RuleId.StartsWith("RD", StringComparison.Ordinal)),
            finding =>
            {
                Assert.Null(finding.Location);
                Assert.NotNull(finding.EvidenceHash);
                Assert.Equal("repo-doctor engine", finding.Source);
            });
        Assert.Equal(
            ["MA0032", "MA0137", "MA0147", "MA0155"],
            outcome.Report.Findings
                .Where(finding => finding.RuleId is "MA0032" or "MA0137" or "MA0147" or "MA0155")
                .Select(finding => finding.RuleId)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(ruleId => ruleId, StringComparer.Ordinal));
        Assert.Single(outcome.Report.Findings, finding => finding.RuleId == "MA0137");
        Assert.Single(outcome.Report.Findings, finding => finding.RuleId == "MA0147");
        Assert.Equal(2, outcome.Report.Findings.Count(finding => finding.RuleId == "MA0155"));

        var repeated = await ScanEngine.RunAsync(request, CancellationToken.None);
        Assert.Equal(
            outcome.Report.Findings
                .Where(finding => finding.RuleId.StartsWith("RD", StringComparison.Ordinal))
                .Select(finding => finding.Fingerprint),
            repeated.Report.Findings
                .Where(finding => finding.RuleId.StartsWith("RD", StringComparison.Ordinal))
                .Select(finding => finding.Fingerprint));
    }

    [Fact]
    public void Project_configuration_property_parser_rejects_unstructured_output()
    {
        Assert.Throws<JsonException>(() => ProjectConfigurationChecks.ParseProperties("{}"));
    }

    [Fact]
    public void Project_configuration_property_parser_reads_sdk_output()
    {
        const string json = """
            { "Properties": { "Nullable": "warnings", "EnableNETAnalyzers": true } }
            """;

        var properties = ProjectConfigurationChecks.ParseProperties(json);

        Assert.Equal("warnings", properties["Nullable"]);
        Assert.Equal("true", properties["EnableNETAnalyzers"]);
    }

    [Fact]
    public async Task Project_configuration_sdk_mismatch_fails_evaluation()
    {
        var root = RepoRoot();
        var path = Path.Combine(root, "tests", "Fixtures", "CodeQualityGaps", "CodeQualityGaps.csproj");
        var request = RequestFor("tests/Fixtures/CodeQualityGaps/CodeQualityGaps.csproj", "dotnet-code-quality.json");
        var project = new LoadedProject(
            "CodeQualityGaps.csproj",
            path,
            "CodeQualityGaps",
            IsSubject: true,
            IsTestProject: false,
            TargetFrameworks: ["net10.0"],
            IsMultiTarget: false,
            RestoreMissing: false,
            RoslynProject: null);

        var result = await ProjectConfigurationChecks.AnalyzeAsync(
            project,
            request.Policy.Rules.Where(rule => rule.RuleId.StartsWith("RD", StringComparison.Ordinal)).ToList(),
            request.Catalog,
            Path.GetDirectoryName(path)!,
            "Debug",
            expectedSdkVersion: "0.0.0",
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(5, result.RulesAttempted.Count);
        Assert.Empty(result.RulesCompleted);
        Assert.Contains(result.Problems, problem => problem.Stage == "project-configuration");
    }

    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }
}
