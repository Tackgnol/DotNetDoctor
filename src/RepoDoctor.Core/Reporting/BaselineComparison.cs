namespace RepoDoctor.Core.Reporting;

public enum ComparisonStatus
{
    Complete = 0,
    Incompatible = 1,
    AnalysisIncomplete = 2,
    BaselineUnusable = 3,
}

public sealed record BaselineComparisonResult
{
    public required ComparisonStatus Status { get; init; }

    public IReadOnlyList<string> Reasons { get; init; } = [];

    public required IReadOnlyList<Finding> HeadFindings { get; init; }

    public int NewCount { get; init; }

    public int ExistingCount { get; init; }

    public int NoLongerReportedCount { get; init; }

    public bool Valid => Status == ComparisonStatus.Complete;

    public ComparisonInfo ToComparisonInfo() => new()
    {
        Status = Status switch
        {
            ComparisonStatus.Complete => "complete",
            ComparisonStatus.Incompatible => "incompatible",
            ComparisonStatus.AnalysisIncomplete => "analysis-incomplete",
            _ => "baseline-unusable",
        },
        NewCount = NewCount,
        ExistingCount = ExistingCount,
        NoLongerReportedCount = NoLongerReportedCount,
        IncompatibilityReasons = Reasons,
    };
}

public static class BaselineComparison
{
    public static BaselineComparisonResult Compare(ScanReport baseline, ScanReport head)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(head);

        if (baseline.Analysis.Completeness != Completeness.Complete)
        {
            return Fail(ComparisonStatus.AnalysisIncomplete, head, "the baseline report is not a complete scan.");
        }

        if (head.Analysis.Completeness != Completeness.Complete)
        {
            return Fail(ComparisonStatus.AnalysisIncomplete, head, "the current analysis did not complete.");
        }

        var reasons = CompatibilityReasons(baseline, head).ToList();
        if (reasons.Count > 0)
        {
            return new BaselineComparisonResult
            {
                Status = ComparisonStatus.Incompatible,
                Reasons = reasons,
                HeadFindings = head.Findings,
            };
        }

        var baselineByKey = baseline.Findings
            .GroupBy(f => f.Fingerprint, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => new Queue<Finding>(g), StringComparer.Ordinal);

        var headFindings = new List<Finding>(head.Findings.Count);
        var existing = 0;
        var added = 0;
        foreach (var finding in head.Findings)
        {
            if (baselineByKey.TryGetValue(finding.Fingerprint, out var queue) && queue.Count > 0)
            {
                queue.Dequeue();
                headFindings.Add(finding with { BaselineState = BaselineState.Existing });
                existing++;
            }
            else
            {
                headFindings.Add(finding with { BaselineState = BaselineState.New });
                added++;
            }
        }

        var noLongerReported = baselineByKey.Values.Sum(queue => queue.Count);

        return new BaselineComparisonResult
        {
            Status = ComparisonStatus.Complete,
            HeadFindings = headFindings,
            NewCount = added,
            ExistingCount = existing,
            NoLongerReportedCount = noLongerReported,
        };
    }

    private static IEnumerable<string> CompatibilityReasons(ScanReport b, ScanReport h)
    {
        if (b.SchemaVersion != h.SchemaVersion)
        {
            yield return $"report schemaVersion differs ({b.SchemaVersion} vs {h.SchemaVersion}).";
        }

        if (!Same(b.Tool.FingerprintSchema, h.Tool.FingerprintSchema))
        {
            yield return "fingerprint schema differs; regenerate the baseline with this tool.";
        }

        if (!Same(b.Tool.Version, h.Tool.Version))
        {
            yield return $"tool version differs ({b.Tool.Version} vs {h.Tool.Version}).";
        }

        if (!Same(b.Tool.BundleHash, h.Tool.BundleHash))
        {
            yield return "analyzer bundle identity differs; a bundle change is a policy change and needs a fresh base scan.";
        }

        if (!Same(b.Tool.RoslynVersion, h.Tool.RoslynVersion))
        {
            yield return $"Roslyn version differs ({b.Tool.RoslynVersion} vs {h.Tool.RoslynVersion}).";
        }

        if (!Same(b.Policy.EffectivePolicyHash, h.Policy.EffectivePolicyHash))
        {
            yield return "effective policy differs; a policy change cannot be compared as a regression.";
        }

        if (!Same(b.Policy.HashAlgorithm, h.Policy.HashAlgorithm))
        {
            yield return "policy hash algorithm differs.";
        }

        if ((b.Audit is null) != (h.Audit is null))
        {
            yield return "dependency-audit stage selection differs; an audited comparison requires an audited baseline.";
        }
        else if (b.Audit is not null && h.Audit is not null && !SetEqual(b.Audit.Sources, h.Audit.Sources))
        {
            yield return "dependency-audit source identity differs.";
        }

        if (!Same(b.Environment.SelectedSdkVersion, h.Environment.SelectedSdkVersion))
        {
            yield return $"selected SDK differs ({b.Environment.SelectedSdkVersion} vs {h.Environment.SelectedSdkVersion}).";
        }

        if (!Same(b.Environment.OsFamily, h.Environment.OsFamily))
        {
            yield return $"OS family differs ({b.Environment.OsFamily} vs {h.Environment.OsFamily}); cross-platform baseline reuse is deferred.";
        }

        if (!Same(Normalize(b.Scope.TargetPath), Normalize(h.Scope.TargetPath)))
        {
            yield return $"target path differs ({b.Scope.TargetPath} vs {h.Scope.TargetPath}).";
        }

        if (!SetEqual(b.Scope.SubjectProjects, h.Scope.SubjectProjects))
        {
            yield return "the subject project set changed; establish a new common scope and regenerate the baseline.";
        }

        if (!SetEqual(b.Scope.TargetFrameworks, h.Scope.TargetFrameworks))
        {
            yield return "target frameworks changed.";
        }

        if (!Same(b.Scope.Configuration, h.Scope.Configuration))
        {
            yield return $"build configuration differs ({b.Scope.Configuration} vs {h.Scope.Configuration}).";
        }

        var baselineDigests = b.Analysis.Projects
            .Where(p => p.IsSubject)
            .ToDictionary(p => p.ProjectPath, p => p.EffectiveConfigDigest, StringComparer.Ordinal);

        foreach (var project in h.Analysis.Projects.Where(p => p.IsSubject))
        {
            if (baselineDigests.TryGetValue(project.ProjectPath, out var baselineDigest)
                && baselineDigest is not null
                && project.EffectiveConfigDigest is not null
                && !Same(baselineDigest, project.EffectiveConfigDigest))
            {
                yield return $"effective analyzer configuration changed for {project.ProjectPath} (an .editorconfig or NoWarn change).";
            }
        }
    }

    private static BaselineComparisonResult Fail(ComparisonStatus status, ScanReport head, string reason) => new()
    {
        Status = status,
        Reasons = [reason],
        HeadFindings = head.Findings,
    };

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.Ordinal);

    private static string Normalize(string path) => path.Replace('\\', '/').TrimStart('.', '/');

    private static bool SetEqual(IEnumerable<string> a, IEnumerable<string> b) =>
        new HashSet<string>(a, StringComparer.Ordinal).SetEquals(b);
}
