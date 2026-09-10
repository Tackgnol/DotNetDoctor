using RepoDoctor.Core.Policy;
using RepoDoctor.Core.Reporting;
using Xunit;

namespace RepoDoctor.Core.Tests;

public sealed class BaselineComparisonTests
{
    private static Finding Finding(string fingerprint, string file = "a.cs", int line = 10) => new()
    {
        Id = fingerprint,
        RuleId = "CA2200",
        Source = "Microsoft.CodeAnalysis.NetAnalyzers",
        Category = "correctness",
        Severity = RuleSeverity.Warning,
        Confidence = Confidence.Unknown,
        Message = "m",
        ProjectPath = "src/App/App.csproj",
        TargetFramework = "net8.0",
        Configuration = "Debug",
        Location = new SourceLocation { FilePath = file, StartLine = line, StartColumn = 1, EndLine = line, EndColumn = 5 },
        Fingerprint = fingerprint,
    };

    private static ScanReport Report(
        IReadOnlyList<Finding> findings,
        Completeness completeness = Completeness.Complete,
        string policyHash = "policy-1",
        string bundleHash = "bundle-1",
        string targetPath = "App.slnx",
        IReadOnlyList<string>? subjects = null,
        IReadOnlyList<string>? tfms = null,
        string? configDigest = "cfg-1",
        AuditInfo? audit = null) => new()
    {
        Tool = new ToolInfo { Version = "0.1.0", AnalyzerPackages = ["p 1"], RoslynVersion = "5.6.0.0", BundleHash = bundleHash, FingerprintSchema = "fp1" },
        Policy = new PolicyInfo
        {
            ProfileId = "bootstrap/server-core",
            ProfileVersion = "0.1.0",
            ProfileHash = "ph",
            EffectivePolicyHash = policyHash,
            HashAlgorithm = "rd-canon-1-sha256",
            SelectedRules = [],
            DisabledRules = [],
            Gate = new GatePolicyInfo { MinimumSeverity = RuleSeverity.Warning, MinimumConfidence = Confidence.High },
            Exclusions = [],
        },
        Environment = new EnvironmentInfo { SelectedSdkVersion = "10.0.303", CliRuntime = "10.0.11", OperatingSystem = "os", OsFamily = "windows", MsBuildVersion = "10.0.303" },
        Scope = new ScopeInfo
        {
            TargetPath = targetPath,
            SubjectProjects = subjects ?? ["src/App/App.csproj"],
            DependencyProjects = [],
            TargetFrameworks = tfms ?? ["net8.0"],
            Configuration = "Debug",
            SubjectDocumentCount = 3,
        },
        Analysis = new AnalysisSection
        {
            Completeness = completeness,
            Stages = [new AnalysisStage { Name = "static-analysis", Status = StageStatus.Complete }],
            Projects =
            [
                new ProjectAnalysisStatus
                {
                    ProjectPath = "src/App/App.csproj",
                    IsSubject = true,
                    State = ProjectAnalysisState.Analyzed,
                    EffectiveConfigDigest = configDigest,
                },
            ],
            RulesAttempted = 1,
            RulesCompleted = 1,
            Problems = [],
        },
        Findings = findings,
        Audit = audit,
        Gate = new GateResult { Status = GateStatus.Passed, BlockingFindingIds = [] },
        Timing = new TimingInfo { StartedAtUtc = "2026-09-07T00:00:00.0000000+00:00", ElapsedSeconds = 1 },
    };

    [Fact]
    public void Identical_input_yields_zero_new()
    {
        var findings = new[] { Finding("fp1:a"), Finding("fp1:b") };
        var result = BaselineComparison.Compare(Report(findings), Report(findings));

        Assert.Equal(ComparisonStatus.Complete, result.Status);
        Assert.Equal(0, result.NewCount);
        Assert.Equal(2, result.ExistingCount);
        Assert.Equal(0, result.NoLongerReportedCount);
        Assert.All(result.HeadFindings, f => Assert.Equal(BaselineState.Existing, f.BaselineState));
    }

    [Fact]
    public void A_finding_with_a_distinct_fingerprint_is_new()
    {
        var baseline = Report([Finding("fp1:a")]);
        var head = Report([Finding("fp1:a"), Finding("fp1:new")]);

        var result = BaselineComparison.Compare(baseline, head);

        Assert.Equal(1, result.NewCount);
        Assert.Equal(1, result.ExistingCount);
        Assert.Equal(BaselineState.New, result.HeadFindings.Single(f => f.Fingerprint == "fp1:new").BaselineState);
    }

    [Fact]
    public void A_copied_violation_is_one_more_new_occurrence()
    {
        var baseline = Report([Finding("fp1:dup", "a.cs")]);
        var head = Report([Finding("fp1:dup", "a.cs"), Finding("fp1:dup", "b.cs")]);

        var result = BaselineComparison.Compare(baseline, head);

        Assert.Equal(1, result.ExistingCount);
        Assert.Equal(1, result.NewCount);
    }

    [Fact]
    public void One_of_two_identical_occurrences_removed_is_one_no_longer_reported()
    {
        var baseline = Report([Finding("fp1:dup", "a.cs"), Finding("fp1:dup", "b.cs")]);
        var head = Report([Finding("fp1:dup", "a.cs")]);

        var result = BaselineComparison.Compare(baseline, head);

        Assert.Equal(1, result.ExistingCount);
        Assert.Equal(0, result.NewCount);
        Assert.Equal(1, result.NoLongerReportedCount);
    }

    [Fact]
    public void Different_checkout_directory_is_compatible_when_normalized_scope_matches()
    {
        var baseline = Report([Finding("fp1:a")], targetPath: "./App.slnx");
        var head = Report([Finding("fp1:a")], targetPath: "App.slnx");

        Assert.Equal(ComparisonStatus.Complete, BaselineComparison.Compare(baseline, head).Status);
    }

    [Theory]
    [InlineData("policy")]
    [InlineData("bundle")]
    [InlineData("subjects")]
    [InlineData("tfm")]
    [InlineData("config")]
    public void Scope_or_policy_changes_are_incompatible_and_exit_2(string change)
    {
        var baseline = Report([Finding("fp1:a")]);
        var head = change switch
        {
            "policy" => Report([Finding("fp1:a")], policyHash: "policy-2"),
            "bundle" => Report([Finding("fp1:a")], bundleHash: "bundle-2"),
            "subjects" => Report([Finding("fp1:a")], subjects: ["src/App/App.csproj", "src/Lib/Lib.csproj"]),
            "tfm" => Report([Finding("fp1:a")], tfms: ["net10.0"]),
            "config" => Report([Finding("fp1:a")], configDigest: "cfg-2"),
            _ => throw new ArgumentOutOfRangeException(nameof(change)),
        };

        var result = BaselineComparison.Compare(baseline, head);

        Assert.Equal(ComparisonStatus.Incompatible, result.Status);
        Assert.NotEmpty(result.Reasons);
        Assert.False(result.Valid);
    }

    [Fact]
    public void An_incomplete_baseline_or_head_is_analysis_incomplete()
    {
        var complete = Report([Finding("fp1:a")]);
        var partial = Report([Finding("fp1:a")], completeness: Completeness.Partial);

        Assert.Equal(ComparisonStatus.AnalysisIncomplete, BaselineComparison.Compare(partial, complete).Status);
        Assert.Equal(ComparisonStatus.AnalysisIncomplete, BaselineComparison.Compare(complete, partial).Status);
    }

    [Fact]
    public void Audited_comparison_requires_an_audited_baseline_with_the_same_sources()
    {
        var audit = new AuditInfo
        {
            CompletedAtUtc = "2026-09-09T00:00:00Z",
            Sources = ["https://api.nuget.org/v3/index.json"],
            Freshness = "unknown",
        };

        Assert.Equal(
            ComparisonStatus.Incompatible,
            BaselineComparison.Compare(Report([]), Report([], audit: audit)).Status);
        Assert.Equal(
            ComparisonStatus.Complete,
            BaselineComparison.Compare(Report([], audit: audit), Report([], audit: audit)).Status);
    }
}
