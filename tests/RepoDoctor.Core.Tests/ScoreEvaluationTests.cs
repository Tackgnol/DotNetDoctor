using RepoDoctor.Core.Policy;
using RepoDoctor.Core.Reporting;
using Xunit;

namespace RepoDoctor.Core.Tests;

public sealed class ScoreEvaluationTests
{
    [Fact]
    public void Complete_scan_scores_findings_and_baseline_delta()
    {
        var score = ScoreEvaluation.Evaluate(
            Completeness.Complete,
            [Finding(RuleSeverity.Error), Finding(RuleSeverity.Warning), Finding(RuleSeverity.Info)],
            [Finding(RuleSeverity.Error), Finding(RuleSeverity.Error)]);

        Assert.Equal(86, score.Value);
        Assert.Equal(14, score.Penalty);
        Assert.Equal(6, score.BaselineScoreDelta);
        Assert.Equal(-6, score.BaselinePenaltyDelta);
    }

    [Fact]
    public void Incomplete_scan_has_no_score()
    {
        var score = ScoreEvaluation.Evaluate(Completeness.Partial, [Finding(RuleSeverity.Error)]);

        Assert.Null(score.Value);
        Assert.Null(score.Penalty);
    }

    private static Finding Finding(RuleSeverity severity) => new()
    {
        Id = "id",
        RuleId = "RD0001",
        Source = "test",
        Category = "test",
        Severity = severity,
        Confidence = Confidence.High,
        Message = "test",
        ProjectPath = "test.csproj",
        Fingerprint = "fp",
    };
}
