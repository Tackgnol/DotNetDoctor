namespace RepoDoctor.Core.Reporting;

public static class FindingOrder
{
    public static IReadOnlyList<Finding> Sort(IEnumerable<Finding> findings)
    {
        ArgumentNullException.ThrowIfNull(findings);

        return findings
            .OrderBy(f => f.ProjectPath, StringComparer.Ordinal)
            .ThenBy(f => f.TargetFramework ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(f => f.Location?.FilePath ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(f => f.Location?.StartLine ?? 0)
            .ThenBy(f => f.Location?.StartColumn ?? 0)
            .ThenBy(f => f.Location?.EndLine ?? 0)
            .ThenBy(f => f.Location?.EndColumn ?? 0)
            .ThenBy(f => f.RuleId, StringComparer.Ordinal)
            .ThenBy(f => f.Fingerprint, StringComparer.Ordinal)
            .ToList();
    }
}
