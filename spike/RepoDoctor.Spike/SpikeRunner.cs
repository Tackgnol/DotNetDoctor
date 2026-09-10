using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.MSBuild;

namespace RepoDoctor.Spike;

internal static class SpikeRunner
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine("usage: repo-doctor-spike <project-or-solution> [ruleId=CA2200]");
            return 2;
        }

        var target = Path.GetFullPath(args[0]);
        var ruleId = args.Length > 1 ? args[1] : "CA2200";

        var analyzerDir = Path.Combine(AppContext.BaseDirectory, "analyzers");
        var analyzers = LoadAnalyzers(analyzerDir);
        Console.Error.WriteLine($"[spike] loaded {analyzers.Length} analyzer(s) from {analyzerDir}");
        if (analyzers.IsEmpty)
        {
            Console.Error.WriteLine("[spike] FAIL: no analyzer assemblies shipped with the tool");
            return 2;
        }

        using var workspace = MSBuildWorkspace.Create();
        using var _ = workspace.RegisterWorkspaceFailedHandler(e =>
            Console.Error.WriteLine($"[workspace] {e.Diagnostic.Kind}: {e.Diagnostic.Message}"));

        IReadOnlyList<Project> projects;
        try
        {
            if (target.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) ||
                target.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase))
            {
                var solution = await workspace.OpenSolutionAsync(target);
                projects = solution.Projects.ToList();
            }
            else
            {
                projects = new[] { await workspace.OpenProjectAsync(target) };
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[spike] FAIL: could not load {target}: {ex.Message}");
            return 2;
        }

        var reported = 0;
        var suppressed = 0;
        var analyzerOptions = new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty);

        foreach (var project in projects.Where(p => p.Language == LanguageNames.CSharp))
        {
            var compilation = await project.GetCompilationAsync();
            if (compilation is null)
            {
                Console.Error.WriteLine($"[spike] {project.Name}: no compilation produced");
                continue;
            }

            foreach (var error in compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error))
            {
                Console.Error.WriteLine($"[compiler-error] {project.Name} {error.Id} {error.GetMessage()}");
            }

            var forcedOptions = compilation.Options.WithSpecificDiagnosticOptions(
                ImmutableDictionary<string, ReportDiagnostic>.Empty.Add(ruleId, ReportDiagnostic.Warn));

            var withAnalyzers = compilation
                .WithOptions(forcedOptions)
                .WithAnalyzers(analyzers, new CompilationWithAnalyzersOptions(
                    analyzerOptions,
                    onAnalyzerException: (ex, analyzer, _) =>
                        Console.Error.WriteLine($"[analyzer-crash] {analyzer.GetType().Name}: {ex.Message}"),
                    concurrentAnalysis: true,
                    logAnalyzerExecutionTime: false,
                    reportSuppressedDiagnostics: true));

            var diagnostics = await withAnalyzers.GetAnalyzerDiagnosticsAsync();

            foreach (var diagnostic in diagnostics
                         .Where(d => d.Id == ruleId)
                         .OrderBy(d => d.Location.GetLineSpan().Path, StringComparer.Ordinal)
                         .ThenBy(d => d.Location.SourceSpan.Start))
            {
                var line = diagnostic.Location.GetLineSpan();
                var where = $"{line.Path}:{line.StartLinePosition.Line + 1}:{line.StartLinePosition.Character + 1}";
                if (diagnostic.IsSuppressed)
                {
                    suppressed++;
                    Console.WriteLine($"SUPPRESSED {diagnostic.Id} {where} {diagnostic.GetMessage()}");
                }
                else
                {
                    reported++;
                    Console.WriteLine($"REPORTED {diagnostic.Id} {where} {diagnostic.GetMessage()}");
                }
            }
        }

        Console.Error.WriteLine($"[spike] {ruleId}: reported={reported} suppressed={suppressed}");
        return reported > 0 ? 1 : 0;
    }

    private static ImmutableArray<DiagnosticAnalyzer> LoadAnalyzers(string dir)
    {
        if (!Directory.Exists(dir))
        {
            return ImmutableArray<DiagnosticAnalyzer>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<DiagnosticAnalyzer>();
        foreach (var dll in Directory.EnumerateFiles(dir, "*.dll"))
        {
            if (dll.EndsWith(".CodeFixes.dll", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var reference = new AnalyzerFileReference(dll, SpikeAnalyzerLoader.Instance);
            reference.AnalyzerLoadFailed += (_, e) =>
                Console.Error.WriteLine($"[analyzer-load-failed] {Path.GetFileName(dll)}: {e.Message}");
            builder.AddRange(reference.GetAnalyzers(LanguageNames.CSharp));
        }

        return builder.ToImmutable();
    }
}

internal sealed class SpikeAnalyzerLoader : IAnalyzerAssemblyLoader
{
    public static readonly SpikeAnalyzerLoader Instance = new();

    public void AddDependencyLocation(string fullPath)
    {
    }

    public Assembly LoadFromPath(string fullPath) => Assembly.LoadFrom(fullPath);
}
