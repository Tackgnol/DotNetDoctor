using RepoDoctor.Core.Policy;

namespace RepoDoctor.Core.Reporting;

public enum GateStatus
{
    NotEvaluated = 0,
    Passed = 1,
    Failed = 2,
}

public sealed record GateResult
{
    public required GateStatus Status { get; init; }

    public required IReadOnlyList<string> BlockingFindingIds { get; init; }

    public string? NotEvaluatedReason { get; init; }
}

public static class GateEvaluation
{
    public static bool Blocks(Finding finding, EffectiveGate gate) =>
        finding.Severity >= gate.MinimumSeverity && finding.Confidence >= gate.MinimumConfidence;

    public static GateResult Evaluate(
        Completeness completeness,
        bool cancelled,
        bool comparisonRequested,
        bool comparisonValid,
        IReadOnlyList<Finding> findings,
        EffectiveGate gate)
    {
        ArgumentNullException.ThrowIfNull(findings);
        ArgumentNullException.ThrowIfNull(gate);

        if (cancelled)
        {
            return NotEvaluated("analysis was cancelled");
        }

        if (completeness != Completeness.Complete)
        {
            return NotEvaluated("analysis did not complete");
        }

        if (comparisonRequested && !comparisonValid)
        {
            return NotEvaluated("the requested baseline comparison is invalid");
        }

        var considered = comparisonRequested
            ? findings.Where(f => f.BaselineState == BaselineState.New)
            : findings;

        var blocking = considered
            .Where(f => Blocks(f, gate))
            .Select(f => f.Id)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();

        return new GateResult
        {
            Status = blocking.Count == 0 ? GateStatus.Passed : GateStatus.Failed,
            BlockingFindingIds = blocking,
        };
    }

    public static int ExitCode(bool cancelled, Completeness completeness, GateResult gate)
    {
        ArgumentNullException.ThrowIfNull(gate);

        if (cancelled)
        {
            return 130;
        }

        if (completeness != Completeness.Complete)
        {
            return 2;
        }

        return gate.Status == GateStatus.Failed ? 1 : 0;
    }

    private static GateResult NotEvaluated(string reason) => new()
    {
        Status = GateStatus.NotEvaluated,
        BlockingFindingIds = [],
        NotEvaluatedReason = reason,
    };
}
