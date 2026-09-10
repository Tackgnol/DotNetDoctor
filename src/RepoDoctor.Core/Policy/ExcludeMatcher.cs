using Microsoft.Extensions.FileSystemGlobbing;

namespace RepoDoctor.Core.Policy;

public sealed class ExcludeMatcher
{
    private readonly Matcher? _matcher;
    private readonly IReadOnlyList<string> _patterns;

    public ExcludeMatcher(IReadOnlyList<string> patterns)
    {
        ArgumentNullException.ThrowIfNull(patterns);
        _patterns = patterns;

        if (patterns.Count == 0)
        {
            _matcher = null;
            return;
        }

        _matcher = new Matcher(StringComparison.Ordinal);
        _matcher.AddIncludePatterns(patterns.Select(Normalize));
    }

    public bool IsEmpty => _matcher is null;

    public IReadOnlyList<string> Patterns => _patterns;

    public bool IsExcluded(string repositoryRelativePath)
    {
        if (_matcher is null)
        {
            return false;
        }

        var normalized = Normalize(repositoryRelativePath);
        return _matcher.Match(normalized).HasMatches;
    }

    private static string Normalize(string path) => path.Replace('\\', '/').TrimStart('/');
}
