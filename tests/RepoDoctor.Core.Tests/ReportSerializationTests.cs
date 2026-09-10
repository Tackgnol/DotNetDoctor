using RepoDoctor.Core.Policy;
using RepoDoctor.Core.Reporting;
using Xunit;

namespace RepoDoctor.Core.Tests;

public sealed class ReportSerializationTests
{
    private static Finding Finding(string project, string? tfm, string? file, int line, string ruleId, string fingerprint) => new()
    {
        Id = fingerprint,
        RuleId = ruleId,
        Source = "pkg",
        Category = "correctness",
        Severity = RuleSeverity.Warning,
        Confidence = Confidence.Unknown,
        Message = "IEnumerable<T> re-enumerated & < > handled",
        ProjectPath = project,
        TargetFramework = tfm,
        Location = file is null ? null : new SourceLocation { FilePath = file, StartLine = line, StartColumn = 1, EndLine = line, EndColumn = 2 },
        Fingerprint = fingerprint,
    };

    [Fact]
    public void Findings_sort_is_deterministic_and_regex_free()
    {
        var unsorted = new[]
        {
            Finding("b.csproj", "net8.0", "z.cs", 10, "CA2200", "fp0:3"),
            Finding("a.csproj", "net8.0", "b.cs", 5, "CA2000", "fp0:1"),
            Finding("a.csproj", "net8.0", "b.cs", 5, "CA2200", "fp0:2"),
            Finding("a.csproj", "net8.0", "a.cs", 99, "CA2200", "fp0:0"),
        };

        var sorted = FindingOrder.Sort(unsorted).Select(f => f.Fingerprint).ToArray();

        Assert.Equal(["fp0:0", "fp0:1", "fp0:2", "fp0:3"], sorted);
        Assert.Equal(sorted, FindingOrder.Sort(unsorted.Reverse()).Select(f => f.Fingerprint).ToArray());
    }

    [Fact]
    public void Report_round_trips_through_json()
    {
        var report = Minimal() with { Findings = [Finding("a.csproj", "net8.0", "a.cs", 1, "CA2200", "fp0:x")] };

        var json = ReportJson.Serialize(report);
        var back = ReportJson.Deserialize(json);

        Assert.Equal(report.SchemaVersion, back.SchemaVersion);
        Assert.Equal(report.Policy.EffectivePolicyHash, back.Policy.EffectivePolicyHash);
        Assert.Single(back.Findings);
        Assert.Equal("CA2200", back.Findings[0].RuleId);
        Assert.Equal(GateStatus.Passed, back.Gate.Status);
        Assert.DoesNotContain("\\u003C", json);
        Assert.DoesNotContain("\\u002B", json);
    }

    [Fact]
    public void Json_is_a_single_document_with_no_trailing_content()
    {
        var json = ReportJson.Serialize(Minimal()).TrimEnd();

        Assert.StartsWith("{", json);
        Assert.EndsWith("}", json);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        Assert.Equal(System.Text.Json.JsonValueKind.Object, doc.RootElement.ValueKind);
    }

    [Fact]
    public void Sarif_places_help_uri_on_the_rule_descriptor()
    {
        var finding = Finding("a.csproj", "net8.0", "a.cs", 1, "CA2200", "fp0:1") with
        {
            HelpUrl = "https://example.test/CA2200",
        };
        using var doc = System.Text.Json.JsonDocument.Parse(SarifWriter.Serialize(Minimal() with { Findings = [finding] }));
        var run = doc.RootElement.GetProperty("runs")[0];

        Assert.Equal(finding.HelpUrl, run.GetProperty("tool").GetProperty("driver").GetProperty("rules")[0].GetProperty("helpUri").GetString());
        Assert.False(run.GetProperty("results")[0].TryGetProperty("helpUri", out _));
    }

    [Fact]
    public void Sarif_accepts_locationless_project_configuration_findings()
    {
        var finding = Finding("a.csproj", "net10.0", file: null, line: 0, "RD1001", "fp1:config");

        using var document = System.Text.Json.JsonDocument.Parse(
            SarifWriter.Serialize(Minimal() with { Findings = [finding] }));
        var result = document.RootElement.GetProperty("runs")[0].GetProperty("results")[0];

        Assert.Equal("RD1001", result.GetProperty("ruleId").GetString());
        Assert.False(result.TryGetProperty("locations", out _));
    }

    private static ScanReport Minimal() => new()
    {
        Tool = new ToolInfo { Version = "0.1.0", AnalyzerPackages = ["pkg 1.0"], RoslynVersion = "5.6.0.0", BundleHash = "abc", FingerprintSchema = "fp1" },
        Policy = new PolicyInfo
        {
            ProfileId = "bootstrap/server-core",
            ProfileVersion = "0.1.0",
            ProfileHash = "h1",
            EffectivePolicyHash = "h2",
            HashAlgorithm = "rd-canon-1-sha256",
            SelectedRules = [],
            DisabledRules = [],
            Gate = new GatePolicyInfo { MinimumSeverity = RuleSeverity.Warning, MinimumConfidence = Confidence.High },
            Exclusions = [],
        },
        Environment = new EnvironmentInfo { SelectedSdkVersion = "10.0.303", CliRuntime = "10.0.11", OperatingSystem = "os", OsFamily = "windows", MsBuildVersion = "10.0.303" },
        Scope = new ScopeInfo { TargetPath = "a.csproj", SubjectProjects = ["a.csproj"], DependencyProjects = [], TargetFrameworks = ["net8.0"], Configuration = "Debug", SubjectDocumentCount = 1 },
        Analysis = new AnalysisSection
        {
            Completeness = Completeness.Complete,
            Stages = [new AnalysisStage { Name = "static-analysis", Status = StageStatus.Complete }],
            Projects = [],
            RulesAttempted = 0,
            RulesCompleted = 0,
            Problems = [],
        },
        Findings = [],
        Gate = new GateResult { Status = GateStatus.Passed, BlockingFindingIds = [] },
        Timing = new TimingInfo { StartedAtUtc = "2026-09-07T00:00:00.0000000+00:00", ElapsedSeconds = 0.1 },
    };
}
