using RepoDoctor.Core.Reporting;

namespace RepoDoctor.Analysis;

public static class GitProvenance
{
    public static ProvenanceInfo? TryRead(string repositoryRoot)
    {
        try
        {
            var gitDir = Path.Combine(repositoryRoot, ".git");
            if (!Directory.Exists(gitDir))
            {
                return null;
            }

            var headPath = Path.Combine(gitDir, "HEAD");
            if (!File.Exists(headPath))
            {
                return null;
            }

            var head = File.ReadAllText(headPath).Trim();
            if (head.StartsWith("ref:", StringComparison.Ordinal))
            {
                var reference = head[4..].Trim();
                var refFile = Path.Combine(gitDir, reference.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(refFile))
                {
                    return new ProvenanceInfo { CommitSha = File.ReadAllText(refFile).Trim(), Dirty = null };
                }

                var packedRefs = Path.Combine(gitDir, "packed-refs");
                if (File.Exists(packedRefs))
                {
                    foreach (var line in File.ReadLines(packedRefs))
                    {
                        if (line.EndsWith(" " + reference, StringComparison.Ordinal))
                        {
                            return new ProvenanceInfo { CommitSha = line.Split(' ')[0], Dirty = null };
                        }
                    }
                }

                return null;
            }

            return new ProvenanceInfo { CommitSha = head, Dirty = null };
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}
