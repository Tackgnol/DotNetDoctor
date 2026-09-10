using System.Text.Json.Serialization;
using RepoDoctor.Core.Policy;

namespace RepoDoctor.Core.Reporting;

public sealed record ScanReport
{
    public int SchemaVersion { get; init; } = 1;

    public required ToolInfo Tool { get; init; }

    public required PolicyInfo Policy { get; init; }

    public required EnvironmentInfo Environment { get; init; }

    public required ScopeInfo Scope { get; init; }

    public ProvenanceInfo? Provenance { get; init; }

    public required AnalysisSection Analysis { get; init; }

    public required IReadOnlyList<Finding> Findings { get; init; }

    public ScoreInfo Score { get; init; } = ScoreEvaluation.NotEvaluated("analysis did not complete");

    public ComparisonInfo? Comparison { get; init; }

    public AuditInfo? Audit { get; init; }

    public required GateResult Gate { get; init; }

    public required TimingInfo Timing { get; init; }
}

public sealed record ToolInfo
{
    public required string Version { get; init; }

    public required IReadOnlyList<string> AnalyzerPackages { get; init; }

    public required string RoslynVersion { get; init; }

    public required string BundleHash { get; init; }

    public required string FingerprintSchema { get; init; }
}

public sealed record PolicyInfo
{
    public required string ProfileId { get; init; }

    public required string ProfileVersion { get; init; }

    public required string ProfileHash { get; init; }

    public required string EffectivePolicyHash { get; init; }

    public required string HashAlgorithm { get; init; }

    public required IReadOnlyList<SelectedRuleInfo> SelectedRules { get; init; }

    public required IReadOnlyList<string> DisabledRules { get; init; }

    public required GatePolicyInfo Gate { get; init; }

    public required IReadOnlyList<string> Exclusions { get; init; }
}

public sealed record SelectedRuleInfo
{
    public required string RuleId { get; init; }

    public required RuleSeverity Severity { get; init; }

    public required RuleScope Scope { get; init; }

    public required PolicySource Source { get; init; }
}

public sealed record GatePolicyInfo
{
    public required RuleSeverity MinimumSeverity { get; init; }

    public required Confidence MinimumConfidence { get; init; }
}

public sealed record EnvironmentInfo
{
    public required string SelectedSdkVersion { get; init; }

    public required string CliRuntime { get; init; }

    public required string OperatingSystem { get; init; }

    public required string OsFamily { get; init; }

    public required string MsBuildVersion { get; init; }
}

public sealed record ScopeInfo
{
    public required string TargetPath { get; init; }

    public required IReadOnlyList<string> SubjectProjects { get; init; }

    public required IReadOnlyList<string> DependencyProjects { get; init; }

    public required IReadOnlyList<string> TargetFrameworks { get; init; }

    public required string Configuration { get; init; }

    public required int SubjectDocumentCount { get; init; }
}

public sealed record ProvenanceInfo
{
    public string? CommitSha { get; init; }

    public bool? Dirty { get; init; }
}

public sealed record ComparisonInfo
{
    public required string Status { get; init; }

    public int NewCount { get; init; }

    public int ExistingCount { get; init; }

    public int NoLongerReportedCount { get; init; }

    public IReadOnlyList<string> IncompatibilityReasons { get; init; } = [];
}

public sealed record AuditInfo
{
    public required string CompletedAtUtc { get; init; }

    public required IReadOnlyList<string> Sources { get; init; }

    public required string Freshness { get; init; }
}

public sealed record TimingInfo
{
    public required string StartedAtUtc { get; init; }

    public required double ElapsedSeconds { get; init; }
}
