using RepoDoctor.Core.Policy;

namespace RepoDoctor.Core.Reporting;

public sealed record ScoreInfo
{
    public int Version { get; init; } = 1;
    public int? Value { get; init; }
    public int? Penalty { get; init; }
    public int? BaselineScoreDelta { get; init; }
    public int? BaselinePenaltyDelta { get; init; }
    public string? NotEvaluatedReason { get; init; }
}

public static class ScoreEvaluation
{
    public static ScoreInfo Evaluate(
        Completeness completeness,
        IReadOnlyList<Finding> findings,
        IReadOnlyList<Finding>? baselineFindings = null)
    {
        if (completeness != Completeness.Complete)
        {
            return NotEvaluated("analysis did not complete");
        }

        var penalty = Penalty(findings);
        var baselinePenalty = baselineFindings is null ? (int?)null : Penalty(baselineFindings);

        return new ScoreInfo
        {
            Value = Math.Max(0, 100 - penalty),
            Penalty = penalty,
            BaselineScoreDelta = baselinePenalty is null ? null : baselinePenalty.Value - penalty,
            BaselinePenaltyDelta = baselinePenalty is null ? null : penalty - baselinePenalty.Value,
        };
    }

    public static ScoreInfo NotEvaluated(string reason) => new() { NotEvaluatedReason = reason };

    private static int Penalty(IEnumerable<Finding> findings) => findings.Sum(f => f.Severity switch
    {
        RuleSeverity.Error => 10,
        RuleSeverity.Warning => 3,
        RuleSeverity.Info => 1,
        _ => 0,
    });
}
