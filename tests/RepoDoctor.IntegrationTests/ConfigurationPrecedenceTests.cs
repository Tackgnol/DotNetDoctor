using RepoDoctor.Analysis;
using RepoDoctor.Core.Catalog;
using RepoDoctor.Core.Policy;
using RepoDoctor.Core.Reporting;
using Xunit;

namespace RepoDoctor.IntegrationTests;

public sealed class ConfigurationPrecedenceTests
{
    private static readonly DiagnosticCatalog Catalog = DiagnosticCatalog.LoadDefault();

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "RepoDoctor.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }

    private static LoadedProfile Bootstrap() => ProfileDocument.Parse(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "profiles", "bootstrap-server-core.json")),
        Catalog,
        "bootstrap");

    private static async Task<ScanReport> ScanAsync(string fixtureProject, RepositoryConfig? config = null)
    {
        var profile = Bootstrap();
        var policy = ProfileResolver.Resolve(profile.Profile, profile.Hash, config, Catalog);
        var outcome = await ScanEngine.RunAsync(
            new ScanRequest
            {
                Target = Path.Combine(RepoRoot(), "tests", "Fixtures", fixtureProject),
                ToolVersion = "0.0.0-test",
                Policy = policy,
                Catalog = Catalog,
                TimeoutSeconds = 300,
            },
            CancellationToken.None);

        Assert.Equal(Completeness.Complete, outcome.Report.Analysis.Completeness);
        return outcome.Report;
    }

    private static RepositoryConfig Config(
        Dictionary<string, RepositoryRuleOverride>? rules = null,
        IReadOnlyList<string>? exclude = null) => new()
    {
        SchemaVersion = 1,
        Profile = "bootstrap.json",
        Rules = rules ?? new Dictionary<string, RepositoryRuleOverride>(StringComparer.Ordinal),
        Exclude = exclude ?? [],
    };

    [Fact]
    public async Task Repository_editorconfig_raises_severity_over_the_profile_and_is_marked_analyzer_config()
    {
        var report = await ScanAsync("EditorConfigMatrix/EditorConfigMatrix.csproj");

        var finding = Assert.Single(report.Findings, f => f.RuleId == "CA2200" && f.Location!.FilePath == "Reported.cs");
        Assert.Equal(RuleSeverity.Error, finding.Severity);
        Assert.Equal(PolicySource.AnalyzerConfig, finding.PolicySource);
    }

    [Fact]
    public async Task Per_directory_editorconfig_none_suppresses_and_generated_file_is_not_reported()
    {
        var report = await ScanAsync("EditorConfigMatrix/EditorConfigMatrix.csproj");

        Assert.DoesNotContain(report.Findings, f => f.Location!.FilePath.StartsWith("Silenced/", StringComparison.Ordinal));
        Assert.DoesNotContain(report.Findings, f => f.Location!.FilePath.EndsWith(".g.cs", StringComparison.Ordinal));
    }

    [Fact]
    public async Task NoWarn_in_the_project_is_not_resurrected_by_the_profile()
    {
        var report = await ScanAsync("NoWarnLib/NoWarnLib.csproj");

        Assert.DoesNotContain(report.Findings, f => f.RuleId == "CA2200");
        Assert.Contains("CA2200", report.Analysis.RulesDisabledOrSkipped);
    }

    [Fact]
    public async Task Disabled_by_default_rule_runs_when_the_profile_selects_it()
    {
        // CA2000's descriptors are IsEnabledByDefault=false; it only appears
        // because the bootstrap profile selects it (severity warning).
        var report = await ScanAsync("DisabledDefault/DisabledDefault.csproj");

        var finding = Assert.Single(report.Findings, f => f.RuleId == "CA2000");
        Assert.Equal(RuleSeverity.Warning, finding.Severity);
        Assert.Equal("Leaky.cs", finding.Location!.FilePath);
    }

    [Fact]
    public async Task Production_scoped_rule_is_skipped_in_a_test_project()
    {
        var config = Config(new Dictionary<string, RepositoryRuleOverride>(StringComparer.Ordinal)
        {
            ["CA2200"] = new() { Scope = RuleScope.Production },
        });

        var report = await ScanAsync("ScopeTest/ScopeTest.csproj", config);

        Assert.True(report.Analysis.Projects.Single(p => p.IsSubject).IsTestProject);
        Assert.DoesNotContain(report.Findings, f => f.RuleId == "CA2200");
    }

    [Fact]
    public async Task Production_scoped_rule_still_applies_to_a_non_test_project()
    {
        var config = Config(new Dictionary<string, RepositoryRuleOverride>(StringComparer.Ordinal)
        {
            ["CA2200"] = new() { Scope = RuleScope.Production },
        });

        var report = await ScanAsync("ExcludeMatrix/ExcludeMatrix.csproj", config);

        Assert.False(report.Analysis.Projects.Single(p => p.IsSubject).IsTestProject);
        var finding = Assert.Single(report.Findings, f => f.RuleId == "CA2200" && f.Location!.FilePath == "Keep.cs");
        Assert.Equal(RuleSeverity.Warning, finding.Severity);
    }

    [Fact]
    public async Task Exclude_globs_filter_reported_locations_only()
    {
        var report = await ScanAsync("ExcludeMatrix/ExcludeMatrix.csproj", Config(exclude: ["**/Migrations/**"]));

        Assert.Contains(report.Findings, f => f.Location!.FilePath == "Keep.cs");
        Assert.DoesNotContain(report.Findings, f => f.Location!.FilePath.Contains("Migrations", StringComparison.Ordinal));
        Assert.Contains(report.Analysis.Problems, p => p.Stage == "exclude");
    }
}
