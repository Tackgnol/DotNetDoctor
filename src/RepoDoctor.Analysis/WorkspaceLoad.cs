using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using RepoDoctor.Core.Reporting;

namespace RepoDoctor.Analysis;

public sealed record LoadedProject(
    string Identity,
    string AbsolutePath,
    string Name,
    bool IsSubject,
    bool IsTestProject,
    IReadOnlyList<string> TargetFrameworks,
    bool IsMultiTarget,
    bool RestoreMissing,
    Project? RoslynProject);

public sealed record WorkspaceLoadResult(
    IReadOnlyList<LoadedProject> Projects,
    IReadOnlyList<AnalysisProblem> Problems,
    string Configuration,
    string MsBuildVersion,
    string SdkVersion);

public static partial class WorkspaceLoad
{
    [GeneratedRegex(@"<TargetFrameworks>\s*([^<]*?)\s*</TargetFrameworks>", RegexOptions.IgnoreCase)]
    private static partial Regex TargetFrameworksElement();

    [GeneratedRegex(@"<TargetFramework>\s*([^<$]*?)\s*</TargetFramework>", RegexOptions.IgnoreCase)]
    private static partial Regex TargetFrameworkElement();

    private static readonly string[] TestMarkers =
    [
        "xunit", "nunit.framework", "Microsoft.VisualStudio.TestPlatform", "Microsoft.TestPlatform",
        "Microsoft.NET.Test.Sdk", "MSTest", "TestFramework",
    ];

    public static async Task<WorkspaceLoadResult> LoadAsync(
        DiscoveryResult discovery,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(discovery);

        var instance = MsBuildHost.EnsureRegistered();
        var problems = new List<AnalysisProblem>();

        using var workspace = MSBuildWorkspace.Create();
        using var _ = workspace.RegisterWorkspaceFailedHandler(e =>
        {
            var severity = ClassifyWorkspaceFailure(e.Diagnostic.Message);
            problems.Add(new AnalysisProblem
            {
                Severity = severity,
                Stage = "workspace-load",
                Message = e.Diagnostic.Message,
            });
        });

        var subjectPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (discovery.Kind == TargetKind.Solution)
        {
            var solution = await workspace.OpenSolutionAsync(discovery.TargetPath, cancellationToken: cancellationToken);
            foreach (var project in solution.Projects)
            {
                if (project.FilePath is { } filePath)
                {
                    subjectPaths.Add(filePath);
                }
            }
        }
        else
        {
            var project = await workspace.OpenProjectAsync(discovery.TargetPath, cancellationToken: cancellationToken);
            if (project.FilePath is { } filePath)
            {
                subjectPaths.Add(filePath);
            }
        }

        var byPath = workspace.CurrentSolution.Projects
            .Where(p => p.FilePath is not null && p.Language == LanguageNames.CSharp)
            .GroupBy(p => p.FilePath!, StringComparer.OrdinalIgnoreCase);

        var loaded = new List<LoadedProject>();
        foreach (var group in byPath)
        {
            var absolutePath = group.Key;
            var roslynProjects = group.ToList();
            var primary = roslynProjects[0];
            var isSubject = subjectPaths.Contains(absolutePath);

            var projectText = SafeReadAllText(absolutePath);
            var multiFromText = TryGetMultiTargetFromText(projectText, out var textTfms);
            var multiFromWorkspace = roslynProjects.Count > 1;
            var isMultiTarget = multiFromText || multiFromWorkspace;

            var tfms = isMultiTarget
                ? textTfms
                : DeriveSingleTargetFramework(projectText, roslynProjects);

            var restoreMissing = !File.Exists(Path.Combine(
                Path.GetDirectoryName(absolutePath)!, "obj", "project.assets.json"));

            if (restoreMissing && isSubject)
            {
                problems.Add(new AnalysisProblem
                {
                    Severity = AnalysisProblemSeverity.Error,
                    Stage = "restore",
                    Message = $"missing restore assets. Run: dotnet restore \"{absolutePath}\"",
                    ProjectPath = Identity(discovery.RepositoryRoot, absolutePath),
                });
            }

            loaded.Add(new LoadedProject(
                Identity: Identity(discovery.RepositoryRoot, absolutePath),
                AbsolutePath: absolutePath,
                Name: primary.Name,
                IsSubject: isSubject,
                IsTestProject: LooksLikeTestProject(primary, projectText),
                TargetFrameworks: tfms,
                IsMultiTarget: isMultiTarget,
                RestoreMissing: restoreMissing,
                RoslynProject: isMultiTarget ? null : primary));
        }

        loaded.Sort((a, b) => string.CompareOrdinal(a.Identity, b.Identity));

        return new WorkspaceLoadResult(
            loaded,
            problems,
            Configuration: "Debug",
            MsBuildVersion: instance.Version.ToString(),
            SdkVersion: instance.Version.ToString());
    }

    private static string Identity(string repositoryRoot, string absolutePath) =>
        Path.GetRelativePath(repositoryRoot, absolutePath).Replace('\\', '/');

    private static AnalysisProblemSeverity ClassifyWorkspaceFailure(string message)
    {
        if (message.Contains("project.assets.json", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("run a NuGet package restore", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("could not be found", StringComparison.OrdinalIgnoreCase))
        {
            return AnalysisProblemSeverity.Error;
        }

        return AnalysisProblemSeverity.Warning;
    }

    private static string SafeReadAllText(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (IOException)
        {
            return string.Empty;
        }
        catch (UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }

    private static bool TryGetMultiTargetFromText(string projectText, out IReadOnlyList<string> targetFrameworks)
    {
        targetFrameworks = [];
        if (projectText.Length == 0)
        {
            return false;
        }

        var match = TargetFrameworksElement().Match(projectText);
        if (!match.Success)
        {
            return false;
        }

        var raw = match.Groups[1].Value.Trim();
        if (raw.Contains("$(", StringComparison.Ordinal))
        {
            targetFrameworks = ["<unresolved>"];
            return true;
        }

        var parts = raw.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        targetFrameworks = parts;
        return parts.Length > 1;
    }

    private static IReadOnlyList<string> DeriveSingleTargetFramework(string projectText, IReadOnlyList<Project> roslynProjects)
    {
        var match = TargetFrameworkElement().Match(projectText);
        if (match.Success && match.Groups[1].Value.Trim().Length > 0)
        {
            return [match.Groups[1].Value.Trim()];
        }

        var fromSymbols = roslynProjects
            .Select(p => FrameworkFromPreprocessorSymbols(p))
            .FirstOrDefault(f => f is not null);

        return fromSymbols is null ? ["unknown"] : [fromSymbols];
    }

    private static string? FrameworkFromPreprocessorSymbols(Project project)
    {
        if (project.ParseOptions is not Microsoft.CodeAnalysis.CSharp.CSharpParseOptions parse)
        {
            return null;
        }

        foreach (var symbol in parse.PreprocessorSymbolNames)
        {
            var match = Regex.Match(symbol, @"^NET(\d+)_(\d+)$");
            if (match.Success)
            {
                return $"net{match.Groups[1].Value}.{match.Groups[2].Value}";
            }
        }

        return null;
    }

    private static bool LooksLikeTestProject(Project project, string projectText)
    {
        if (projectText.Contains("<IsTestProject>true", StringComparison.OrdinalIgnoreCase) ||
            projectText.Contains("Microsoft.NET.Test.Sdk", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        foreach (var reference in project.MetadataReferences)
        {
            var name = Path.GetFileNameWithoutExtension((reference as PortableExecutableReference)?.FilePath ?? string.Empty);
            if (TestMarkers.Any(marker => name.Contains(marker, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }
}
