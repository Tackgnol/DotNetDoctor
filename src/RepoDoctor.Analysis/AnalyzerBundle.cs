using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace RepoDoctor.Analysis;

public sealed class AnalyzerBundle
{
    private AnalyzerBundle(
        ImmutableArray<DiagnosticAnalyzer> analyzers,
        IReadOnlyDictionary<string, ImmutableArray<DiagnosticDescriptor>> descriptorsById,
        IReadOnlyList<string> assemblyFiles,
        IReadOnlyList<string> loadFailures)
    {
        Analyzers = analyzers;
        DescriptorsById = descriptorsById;
        AssemblyFiles = assemblyFiles;
        LoadFailures = loadFailures;
        RoslynVersion = typeof(Compilation).Assembly.GetName().Version?.ToString() ?? "unknown";
    }

    public ImmutableArray<DiagnosticAnalyzer> Analyzers { get; }

    public IReadOnlyDictionary<string, ImmutableArray<DiagnosticDescriptor>> DescriptorsById { get; }

    public IReadOnlyList<string> AssemblyFiles { get; }

    public IReadOnlyList<string> LoadFailures { get; }

    public string RoslynVersion { get; }

    public static string DefaultDirectory => Path.Combine(AppContext.BaseDirectory, "bundled-analyzers");

    public static AnalyzerBundle Load(string? analyzerDirectory = null)
    {
        var directory = analyzerDirectory ?? DefaultDirectory;
        if (!Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException(
                $"Analyzer bundle directory '{directory}' was not found next to the tool. Packaging is broken.");
        }

        var loadFailures = new List<string>();
        var analyzers = ImmutableArray.CreateBuilder<DiagnosticAnalyzer>();
        var files = new List<string>();

        foreach (var dll in Directory.EnumerateFiles(directory, "*.dll").OrderBy(p => p, StringComparer.Ordinal))
        {
            if (dll.EndsWith(".CodeFixes.dll", StringComparison.OrdinalIgnoreCase) ||
                dll.EndsWith(".resources.dll", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            files.Add(Path.GetFileName(dll));
            var reference = new AnalyzerFileReference(dll, BundleAssemblyLoader.Instance);
            reference.AnalyzerLoadFailed += (_, e) => loadFailures.Add($"{Path.GetFileName(dll)}: {e.Message}");
            analyzers.AddRange(reference.GetAnalyzers(LanguageNames.CSharp));
        }

        var descriptorsById = analyzers
            .SelectMany(a => a.SupportedDiagnostics.Select(d => (d.Id, Descriptor: d)))
            .GroupBy(x => x.Id, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => g.Select(x => x.Descriptor).Distinct().ToImmutableArray(),
                StringComparer.Ordinal);

        return new AnalyzerBundle(analyzers.ToImmutable(), descriptorsById, files, loadFailures);
    }

    public bool Supports(string ruleId) => DescriptorsById.ContainsKey(ruleId);

    public ImmutableArray<DiagnosticAnalyzer> AnalyzersFor(IEnumerable<string> ruleIds)
    {
        var wanted = ruleIds.ToHashSet(StringComparer.Ordinal);
        return Analyzers
            .Where(a => a.SupportedDiagnostics.Any(d => wanted.Contains(d.Id)))
            .ToImmutableArray();
    }
}

internal sealed class BundleAssemblyLoader : IAnalyzerAssemblyLoader
{
    public static readonly BundleAssemblyLoader Instance = new();

    private readonly HashSet<string> _directories = new(StringComparer.OrdinalIgnoreCase);

    private BundleAssemblyLoader()
    {
        AppDomain.CurrentDomain.AssemblyResolve += ResolveFromKnownDirectories;
    }

    public void AddDependencyLocation(string fullPath)
    {
        var dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(dir))
        {
            lock (_directories)
            {
                _directories.Add(dir);
            }
        }
    }

    public Assembly LoadFromPath(string fullPath)
    {
        AddDependencyLocation(fullPath);
        return Assembly.LoadFrom(fullPath);
    }

    private Assembly? ResolveFromKnownDirectories(object? sender, ResolveEventArgs args)
    {
        var simpleName = new AssemblyName(args.Name).Name;
        if (simpleName is null)
        {
            return null;
        }

        string[] snapshot;
        lock (_directories)
        {
            snapshot = _directories.ToArray();
        }

        foreach (var dir in snapshot)
        {
            var candidate = Path.Combine(dir, simpleName + ".dll");
            if (File.Exists(candidate))
            {
                return Assembly.LoadFrom(candidate);
            }
        }

        return null;
    }
}
