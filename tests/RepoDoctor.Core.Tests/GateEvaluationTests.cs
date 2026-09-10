using RepoDoctor.Core.Policy;
using RepoDoctor.Core.Reporting;
using Xunit;

namespace RepoDoctor.Core.Tests;

public sealed class GateEvaluationTests
{
    private static readonly EffectiveGate WarnHigh = new(RuleSeverity.Warning, Confidence.High);

    private static Finding Finding(RuleSeverity severity, Confidence confidence, BaselineState baseline = BaselineState.Uncompared, string id = "f-1") => new()
    {
        Id = id,
        RuleId = "CA2200",
        Source = "pkg",
        Category = "correctness",
        Severity = severity,
        Confidence = confidence,
        Message = "m",
        ProjectPath = "p.csproj",
        Fingerprint = "fp0:" + id,
        BaselineState = baseline,
    };

    [Fact]
    public void Blocks_only_when_both_severity_and_confidence_meet_the_minimums()
    {
        Assert.True(GateEvaluation.Blocks(Finding(RuleSeverity.Warning, Confidence.High), WarnHigh));
        Assert.True(GateEvaluation.Blocks(Finding(RuleSeverity.Error, Confidence.High), WarnHigh));
        Assert.False(GateEvaluation.Blocks(Finding(RuleSeverity.Warning, Confidence.Unknown), WarnHigh));
        Assert.False(GateEvaluation.Blocks(Finding(RuleSeverity.Info, Confidence.High), WarnHigh));
    }

    [Fact]
    public void Passes_with_findings_that_do_not_meet_confidence()
    {
        var result = GateEvaluation.Evaluate(
            Completeness.Complete, cancelled: false, comparisonRequested: false, comparisonValid: true,
            [Finding(RuleSeverity.Error, Confidence.Unknown)], WarnHigh);

        Assert.Equal(GateStatus.Passed, result.Status);
        Assert.Equal(0, GateEvaluation.ExitCode(false, Completeness.Complete, result));
    }

    [Fact]
    public void Fails_and_exit_1_when_a_finding_blocks()
    {
        var result = GateEvaluation.Evaluate(
            Completeness.Complete, cancelled: false, comparisonRequested: false, comparisonValid: true,
            [Finding(RuleSeverity.Error, Confidence.High, id: "b-1")], WarnHigh);

        Assert.Equal(GateStatus.Failed, result.Status);
        Assert.Equal(["b-1"], result.BlockingFindingIds);
        Assert.Equal(1, GateEvaluation.ExitCode(false, Completeness.Complete, result));
    }

    [Fact]
    public void Partial_analysis_is_never_evaluated_and_exits_2_even_with_serious_findings()
    {
        var result = GateEvaluation.Evaluate(
            Completeness.Partial, cancelled: false, comparisonRequested: false, comparisonValid: true,
            [Finding(RuleSeverity.Error, Confidence.High)], WarnHigh);

        Assert.Equal(GateStatus.NotEvaluated, result.Status);
        Assert.Equal(2, GateEvaluation.ExitCode(false, Completeness.Partial, result));
    }

    [Fact]
    public void Cancellation_takes_precedence_and_exits_130()
    {
        var result = GateEvaluation.Evaluate(
            Completeness.Failed, cancelled: true, comparisonRequested: false, comparisonValid: true,
            [], WarnHigh);

        Assert.Equal(GateStatus.NotEvaluated, result.Status);
        Assert.Equal(130, GateEvaluation.ExitCode(true, Completeness.Failed, result));
    }

    [Fact]
    public void With_baseline_only_new_findings_are_considered()
    {
        var findings = new[]
        {
            Finding(RuleSeverity.Error, Confidence.High, BaselineState.Existing, "old"),
            Finding(RuleSeverity.Error, Confidence.High, BaselineState.New, "new"),
        };

        var result = GateEvaluation.Evaluate(
            Completeness.Complete, cancelled: false, comparisonRequested: true, comparisonValid: true, findings, WarnHigh);

        Assert.Equal(["new"], result.BlockingFindingIds);
    }

    [Fact]
    public void Invalid_requested_comparison_is_not_evaluated()
    {
        var result = GateEvaluation.Evaluate(
            Completeness.Complete, cancelled: false, comparisonRequested: true, comparisonValid: false, [], WarnHigh);

        Assert.Equal(GateStatus.NotEvaluated, result.Status);
    }
}
