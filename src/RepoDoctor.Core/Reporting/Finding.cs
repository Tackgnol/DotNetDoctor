using System.Text.Json.Serialization;
using RepoDoctor.Core.Policy;

namespace RepoDoctor.Core.Reporting;

public enum BaselineState
{
    [JsonStringEnumMemberName("uncompared")]
    Uncompared = 0,

    [JsonStringEnumMemberName("new")]
    New = 1,

    [JsonStringEnumMemberName("existing")]
    Existing = 2,
}

public enum FixAvailability
{
    [JsonStringEnumMemberName("unknown")]
    Unknown = 0,

    [JsonStringEnumMemberName("manual")]
    Manual = 1,

    [JsonStringEnumMemberName("upstream-fixer")]
    UpstreamFixer = 2,
}

public sealed record SourceLocation
{
    public required string FilePath { get; init; }

    public required int StartLine { get; init; }

    public required int StartColumn { get; init; }

    public required int EndLine { get; init; }

    public required int EndColumn { get; init; }
}

public sealed record Finding
{
    public required string Id { get; init; }

    public required string RuleId { get; init; }

    public required string Source { get; init; }

    public required string Category { get; init; }

    public required RuleSeverity Severity { get; init; }

    public required Confidence Confidence { get; init; }

    public string? ConfidenceRationale { get; init; }

    public required string Message { get; init; }

    public required string ProjectPath { get; init; }

    public string? TargetFramework { get; init; }

    public string? Configuration { get; init; }

    public SourceLocation? Location { get; init; }

    public IReadOnlyList<SourceLocation> RelatedLocations { get; init; } = [];

    public string? SymbolId { get; init; }

    public string? EvidenceHash { get; init; }

    public required string Fingerprint { get; init; }

    public string? HelpUrl { get; init; }

    public string? Remediation { get; init; }

    public FixAvailability FixAvailability { get; init; } = FixAvailability.Unknown;

    public BaselineState BaselineState { get; init; } = BaselineState.Uncompared;

    public PolicySource PolicySource { get; init; } = PolicySource.Profile;

    public DependencyEvidence? Dependency { get; init; }
}

public sealed record DependencyEvidence
{
    public required string PackageId { get; init; }

    public required string ResolvedVersion { get; init; }

    public string? AdvisoryId { get; init; }

    public string? AdvisoryUrl { get; init; }

    public string? AdvisorySeverity { get; init; }

    public string? Relationship { get; init; }
}
