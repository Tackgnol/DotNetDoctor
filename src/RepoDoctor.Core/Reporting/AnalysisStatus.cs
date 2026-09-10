using System.Text.Json.Serialization;

namespace RepoDoctor.Core.Reporting;

public enum Completeness
{
    [JsonStringEnumMemberName("complete")]
    Complete = 0,

    [JsonStringEnumMemberName("partial")]
    Partial = 1,

    [JsonStringEnumMemberName("failed")]
    Failed = 2,
}

public enum StageStatus
{
    [JsonStringEnumMemberName("complete")]
    Complete = 0,

    [JsonStringEnumMemberName("disabled")]
    Disabled = 1,

    [JsonStringEnumMemberName("unsupported")]
    Unsupported = 2,

    [JsonStringEnumMemberName("failed")]
    Failed = 3,

    [JsonStringEnumMemberName("cancelled")]
    Cancelled = 4,
}

public enum ProjectAnalysisState
{
    [JsonStringEnumMemberName("analyzed")]
    Analyzed = 0,

    [JsonStringEnumMemberName("partial")]
    Partial = 1,

    [JsonStringEnumMemberName("unsupported")]
    Unsupported = 2,

    [JsonStringEnumMemberName("failed")]
    Failed = 3,

    [JsonStringEnumMemberName("skipped")]
    Skipped = 4,
}

public enum AnalysisProblemSeverity
{
    [JsonStringEnumMemberName("info")]
    Info = 0,

    [JsonStringEnumMemberName("warning")]
    Warning = 1,

    [JsonStringEnumMemberName("error")]
    Error = 2,
}

public sealed record AnalysisStage
{
    public required string Name { get; init; }

    public required StageStatus Status { get; init; }

    public string? Detail { get; init; }
}

public sealed record ProjectAnalysisStatus
{
    public required string ProjectPath { get; init; }

    public required bool IsSubject { get; init; }

    public required ProjectAnalysisState State { get; init; }

    public IReadOnlyList<string> TargetFrameworks { get; init; } = [];

    public bool IsTestProject { get; init; }

    public string? EffectiveConfigDigest { get; init; }

    public string? Detail { get; init; }
}

public sealed record AnalysisProblem
{
    public required AnalysisProblemSeverity Severity { get; init; }

    public required string Stage { get; init; }

    public required string Message { get; init; }

    public string? ProjectPath { get; init; }
}

public sealed record AnalysisSection
{
    public required Completeness Completeness { get; init; }

    public required IReadOnlyList<AnalysisStage> Stages { get; init; }

    public required IReadOnlyList<ProjectAnalysisStatus> Projects { get; init; }

    public required int RulesAttempted { get; init; }

    public required int RulesCompleted { get; init; }

    public IReadOnlyList<string> RulesWithNoFindings { get; init; } = [];

    public IReadOnlyList<string> RulesDisabledOrSkipped { get; init; } = [];

    public required IReadOnlyList<AnalysisProblem> Problems { get; init; }
}
