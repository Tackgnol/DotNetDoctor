using System.Globalization;
using System.Text;
using RepoDoctor.Core.Policy;

namespace RepoDoctor.Core.Reporting;

public static class ConsoleReport
{
    public static string Render(ScanReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var text = new StringBuilder();

        text.Append("repo-doctor ").Append(report.Tool.Version).Append('\n');
        text.Append("analysis:  ").Append(report.Analysis.Completeness.ToString().ToLowerInvariant()).Append('\n');
        text.Append("gate:      ").Append(GateLine(report.Gate)).Append('\n');
        text.Append("profile:   ").Append(report.Policy.ProfileId).Append('@').Append(report.Policy.ProfileVersion)
            .Append(" (").Append(Short(report.Policy.EffectivePolicyHash)).Append(")\n");

        var analyzed = report.Analysis.Projects.Count(p => p is { IsSubject: true, State: ProjectAnalysisState.Analyzed });
        var subject = report.Analysis.Projects.Count(p => p.IsSubject);
        text.Append("projects:  ").Append(analyzed).Append('/').Append(subject).Append(" subject project(s) analyzed\n");

        if (report.Comparison is { } comparison)
        {
            text.Append("baseline:  ").Append(comparison.Status)
                .Append(" | new ").Append(comparison.NewCount)
                .Append(" | existing ").Append(comparison.ExistingCount)
                .Append(" | no longer reported ").Append(comparison.NoLongerReportedCount).Append('\n');
        }

        foreach (var problem in report.Analysis.Problems)
        {
            text.Append("  [").Append(problem.Severity.ToString().ToLowerInvariant()).Append("] ")
                .Append(problem.Stage).Append(": ").Append(problem.Message);
            if (problem.ProjectPath is { } path)
            {
                text.Append(" (").Append(path).Append(')');
            }

            text.Append('\n');
        }

        text.Append('\n');

        if (report.Findings.Count == 0)
        {
            text.Append("No findings for the selected rules.\n");
        }
        else
        {
            foreach (var group in report.Findings
                         .GroupBy(f => f.Severity)
                         .OrderByDescending(g => g.Key))
            {
                text.Append(group.Key.ToString().ToUpperInvariant()).Append(" (").Append(group.Count()).Append(")\n");
                foreach (var finding in group)
                {
                    text.Append("  ").Append(Where(finding)).Append("  ").Append(finding.RuleId)
                        .Append("  ").Append(finding.Message).Append('\n');
                }
            }
        }

        text.Append('\n');
        var newCount = report.Findings.Count(f => f.BaselineState == BaselineState.New);
        var existingCount = report.Findings.Count(f => f.BaselineState == BaselineState.Existing);
        if (report.Comparison is not null)
        {
            text.Append(CultureInfo.InvariantCulture, $"{newCount} new, {existingCount} existing.\n");
        }
        else
        {
            text.Append(report.Findings.Count).Append(" finding(s).\n");
        }

        return text.ToString();
    }

    private static string GateLine(GateResult gate) => gate.Status switch
    {
        GateStatus.Passed => "passed",
        GateStatus.Failed => $"FAILED ({gate.BlockingFindingIds.Count} blocking)",
        _ => $"not evaluated ({gate.NotEvaluatedReason})",
    };

    private static string Where(Finding finding)
    {
        if (finding.Location is not { } location)
        {
            return finding.ProjectPath;
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{location.FilePath}:{location.StartLine}:{location.StartColumn}");
    }

    private static string Short(string hash) => hash.Length <= 12 ? hash : hash[..12];
}
