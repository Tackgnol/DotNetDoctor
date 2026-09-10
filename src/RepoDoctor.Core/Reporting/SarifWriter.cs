using System.Text.Json;
using System.Text.Json.Nodes;
using RepoDoctor.Core.Policy;

namespace RepoDoctor.Core.Reporting;

public static class SarifWriter
{
    public static string Serialize(ScanReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var rules = report.Findings
            .GroupBy(f => f.RuleId, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g =>
            {
                var first = g.First();
                var rule = new JsonObject
                {
                    ["id"] = first.RuleId,
                    ["shortDescription"] = new JsonObject { ["text"] = first.Message },
                };
                if (first.HelpUrl is not null)
                {
                    rule["helpUri"] = first.HelpUrl;
                }

                return rule;
            })
            .ToArray();

        var results = report.Findings.Select(ToResult).ToArray();
        var notifications = report.Analysis.Problems
            .Where(p => p.Severity == AnalysisProblemSeverity.Error)
            .Select(p => new JsonObject
            {
                ["level"] = "error",
                ["message"] = new JsonObject { ["text"] = $"{p.Stage}: {p.Message}" },
            })
            .ToArray();

        var invocation = new JsonObject
        {
            ["executionSuccessful"] = report.Analysis.Completeness == Completeness.Complete
                && report.Gate.Status != GateStatus.NotEvaluated,
        };
        if (notifications.Length > 0)
        {
            invocation["toolExecutionNotifications"] = new JsonArray(notifications);
        }

        var run = new JsonObject
        {
            ["tool"] = new JsonObject
            {
                ["driver"] = new JsonObject
                {
                    ["name"] = "repo-doctor",
                    ["version"] = report.Tool.Version,
                    ["informationUri"] = "https://github.com/repo-doctor/repo-doctor",
                    ["rules"] = new JsonArray(rules),
                },
            },
            ["results"] = new JsonArray(results),
            ["invocations"] = new JsonArray(invocation),
            ["properties"] = new JsonObject
            {
                ["repoDoctor.policyHash"] = report.Policy.EffectivePolicyHash,
                ["repoDoctor.completeness"] = report.Analysis.Completeness.ToString().ToLowerInvariant(),
            },
        };

        var sarif = new JsonObject
        {
            ["$schema"] = "https://json.schemastore.org/sarif-2.1.0.json",
            ["version"] = "2.1.0",
            ["runs"] = new JsonArray(run),
        };

        return sarif.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n";
    }

    private static JsonObject ToResult(Finding finding)
    {
        var result = new JsonObject
        {
            ["ruleId"] = finding.RuleId,
            ["level"] = Level(finding.Severity),
            ["message"] = new JsonObject { ["text"] = finding.Message },
            ["partialFingerprints"] = new JsonObject { ["repoDoctor"] = finding.Fingerprint },
        };

        if (finding.BaselineState == BaselineState.Existing)
        {
            result["baselineState"] = "unchanged";
        }
        else if (finding.BaselineState == BaselineState.New)
        {
            result["baselineState"] = "new";
        }

        if (finding.Location is { } location)
        {
            result["locations"] = new JsonArray
            {
                new JsonObject
                {
                    ["physicalLocation"] = new JsonObject
                    {
                        ["artifactLocation"] = new JsonObject { ["uri"] = location.FilePath },
                        ["region"] = new JsonObject
                        {
                            ["startLine"] = location.StartLine,
                            ["startColumn"] = location.StartColumn,
                            ["endLine"] = location.EndLine,
                            ["endColumn"] = location.EndColumn,
                        },
                    },
                },
            };
        }

        return result;
    }

    private static string Level(RuleSeverity severity) => severity switch
    {
        RuleSeverity.Error => "error",
        RuleSeverity.Warning => "warning",
        _ => "note",
    };
}
