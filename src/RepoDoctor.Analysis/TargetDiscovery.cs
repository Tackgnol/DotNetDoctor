using RepoDoctor.Core;

namespace RepoDoctor.Analysis;

public enum TargetKind
{
    Solution,
    Project,
}

public sealed record DiscoveryResult(string TargetPath, TargetKind Kind, string RepositoryRoot);

public static class TargetDiscovery
{
    private static readonly string[] SolutionExtensions = [".sln", ".slnx"];

    public static DiscoveryResult Resolve(string target, string? repositoryConfigPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target);

        var full = Path.GetFullPath(target);

        if (File.Exists(full))
        {
            var kind = ClassifyFile(full);
            return new DiscoveryResult(full, kind, DetermineRoot(Path.GetDirectoryName(full)!, repositoryConfigPath));
        }

        if (!Directory.Exists(full))
        {
            throw new RepoDoctorValidationException($"target '{target}' does not exist.");
        }

        var solutions = SolutionExtensions
            .SelectMany(ext => Directory.EnumerateFiles(full, "*" + ext))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        if (solutions.Count == 1)
        {
            return new DiscoveryResult(solutions[0], TargetKind.Solution, DetermineRoot(full, repositoryConfigPath));
        }

        if (solutions.Count > 1)
        {
            throw new RepoDoctorValidationException(
                $"target directory '{target}' contains multiple solutions; pass one explicitly:\n  " +
                string.Join("\n  ", solutions));
        }

        var projects = Directory.EnumerateFiles(full, "*.csproj")
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        return projects.Count switch
        {
            1 => new DiscoveryResult(projects[0], TargetKind.Project, DetermineRoot(full, repositoryConfigPath)),
            0 => throw new RepoDoctorValidationException(
                $"target directory '{target}' contains no solution or project file. Pass a .sln, .slnx, or .csproj path."),
            _ => throw new RepoDoctorValidationException(
                $"target directory '{target}' contains multiple projects; pass one explicitly:\n  " +
                string.Join("\n  ", projects)),
        };
    }

    private static TargetKind ClassifyFile(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (SolutionExtensions.Contains(extension))
        {
            return TargetKind.Solution;
        }

        if (extension == ".csproj")
        {
            return TargetKind.Project;
        }

        throw new RepoDoctorValidationException(
            $"target '{path}' must be a .sln, .slnx, or .csproj file.");
    }

    private static string DetermineRoot(string startDirectory, string? repositoryConfigPath)
    {
        if (!string.IsNullOrWhiteSpace(repositoryConfigPath))
        {
            return Path.GetDirectoryName(Path.GetFullPath(repositoryConfigPath))!;
        }

        var gitRoot = FindEnclosingGitRoot(startDirectory);
        return gitRoot ?? startDirectory;
    }

    private static string? FindEnclosingGitRoot(string startDirectory)
    {
        var dir = new DirectoryInfo(startDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, ".git")) ||
                File.Exists(Path.Combine(dir.FullName, ".git")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return null;
    }
}
