using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using RepoDoctor.Core.Catalog;
using RepoDoctor.Core.Json;
using RepoDoctor.Core.Policy;
using RepoDoctor.Core.Reporting;

namespace RepoDoctor.Analysis;

public sealed record ProjectAnalysisResult(
    IReadOnlyList<Finding> Findings,
    IReadOnlyList<AnalysisProblem> Problems,
    IReadOnlyList<string> RulesRan,
    IReadOnlyList<string> RulesSuppressedByRepository,
    string? EffectiveConfigDigest,
    bool CompilerStateUsable);

public static class ProjectAnalyzer
{
    public static async Task<ProjectAnalysisResult> AnalyzeAsync(
        Project project,
        LoadedProject info,
        EffectivePolicy policy,
        AnalyzerBundle bundle,
        DiagnosticCatalog catalog,
        string repositoryRoot,
        string configuration,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(bundle);
        ArgumentNullException.ThrowIfNull(catalog);

        var problems = new List<AnalysisProblem>();

        var applicable = policy.Rules
            .Where(r => r.Scope == RuleScope.All || !info.IsTestProject)
            .Where(r => bundle.Supports(r.RuleId))
            .ToDictionary(r => r.RuleId, StringComparer.Ordinal);

        if (applicable.Count == 0)
        {
            return new ProjectAnalysisResult([], problems, [], [], null, CompilerStateUsable: true);
        }

        var compilation = await project.GetCompilationAsync(cancellationToken);
        if (compilation is null)
        {
            problems.Add(Problem(info, "compilation", "no compilation was produced for the project."));
            return new ProjectAnalysisResult([], problems, [], [], null, CompilerStateUsable: false);
        }

        var configDigest = await ComputeConfigDigestAsync(project, compilation, applicable.Keys, cancellationToken);

        var essentialErrors = compilation
            .GetDiagnostics(cancellationToken)
            .Where(d => d.Severity == DiagnosticSeverity.Error && IsEssential(d.Id))
            .Take(5)
            .ToList();
        var compilerStateUsable = essentialErrors.Count == 0;
        foreach (var error in essentialErrors)
        {
            problems.Add(Problem(info, "compilation", $"{error.Id}: {error.GetMessage(CultureInfo.InvariantCulture)}"));
        }

        var explicitOptions = compilation.Options.SpecificDiagnosticOptions;
        var suppressedByRepo = applicable.Keys
            .Where(id => explicitOptions.TryGetValue(id, out var report) && report == ReportDiagnostic.Suppress)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();
        var suppressedSet = suppressedByRepo.ToHashSet(StringComparer.Ordinal);

        var fallbackSeverities = applicable
            .Where(kvp => !suppressedSet.Contains(kvp.Key))
            .ToDictionary(kvp => kvp.Key, kvp => ToReportDiagnostic(kvp.Value.Severity), StringComparer.Ordinal);

        if (fallbackSeverities.Count == 0)
        {
            return new ProjectAnalysisResult([], problems, [], suppressedByRepo, configDigest, compilerStateUsable);
        }

        var configured = compilation.WithOptions(compilation.Options.WithSyntaxTreeOptionsProvider(
            new FallbackSyntaxTreeOptions(compilation.Options.SyntaxTreeOptionsProvider, fallbackSeverities)));

        var analyzers = bundle.AnalyzersFor(fallbackSeverities.Keys);
        var crashed = false;

        var withAnalyzers = configured.WithAnalyzers(analyzers, new CompilationWithAnalyzersOptions(
            project.AnalyzerOptions,
            onAnalyzerException: (ex, analyzer, _) =>
            {
                crashed = true;
                problems.Add(Problem(info, "analyzer", $"{analyzer.GetType().Name} crashed: {ex.Message}"));
            },
            concurrentAnalysis: true,
            logAnalyzerExecutionTime: false,
            reportSuppressedDiagnostics: false));

        var raw = await withAnalyzers.GetAnalyzerDiagnosticsAsync(cancellationToken);

        var context = new FindingContext(
            info.Identity,
            info.TargetFrameworks.Count == 1 ? info.TargetFrameworks[0] : null,
            configuration,
            repositoryRoot);

        var semanticModels = new Dictionary<SyntaxTree, SemanticModel>();
        SemanticModel? ModelFor(SyntaxTree? tree)
        {
            if (tree is null)
            {
                return null;
            }

            if (!semanticModels.TryGetValue(tree, out var model))
            {
                model = configured.GetSemanticModel(tree);
                semanticModels[tree] = model;
            }

            return model;
        }

        var findings = raw
            .Where(d => applicable.ContainsKey(d.Id))
            .Where(d => !suppressedSet.Contains(d.Id))
            .Where(d => d.Severity != DiagnosticSeverity.Hidden)
            .Where(d => !IsGeneratedLocation(d.Location, configured))
            .DistinctBy(d => (d.Id, TreePath(d), d.Location.SourceSpan.Start, d.Location.SourceSpan.Length, d.GetMessage(CultureInfo.InvariantCulture)))
            .Select(d => FindingFactory.Create(
                d,
                applicable[d.Id],
                EffectiveSeverity(d, configured, applicable[d.Id].Severity, cancellationToken),
                catalog.Get(d.Id),
                context,
                ModelFor(d.Location.SourceTree)))
            .ToList();

        var rulesRan = crashed
            ? []
            : fallbackSeverities.Keys.OrderBy(id => id, StringComparer.Ordinal).ToArray();

        return new ProjectAnalysisResult(findings, problems, rulesRan, suppressedByRepo, configDigest, compilerStateUsable && !crashed);
    }

    private static async Task<string> ComputeConfigDigestAsync(
        Project project,
        Compilation compilation,
        IEnumerable<string> selectedRuleIds,
        CancellationToken cancellationToken)
    {
        var projectDir = Path.GetDirectoryName(project.FilePath) ?? string.Empty;
        var provider = compilation.Options.SyntaxTreeOptionsProvider;
        var specific = compilation.Options.SpecificDiagnosticOptions;

        var severities = new JsonObject();
        var noWarn = new JsonArray();
        foreach (var id in selectedRuleIds.OrderBy(i => i, StringComparer.Ordinal))
        {
            if (specific.TryGetValue(id, out var explicitReport))
            {
                severities[id] = explicitReport.ToString();
                if (explicitReport == ReportDiagnostic.Suppress)
                {
                    noWarn.Add(id);
                }
            }
            else if (provider is not null && provider.TryGetGlobalDiagnosticValue(id, cancellationToken, out var globalReport))
            {
                severities[id] = globalReport.ToString();
            }
        }

        var editorConfigs = new JsonArray();
        foreach (var document in project.AnalyzerConfigDocuments.OrderBy(d => d.FilePath, StringComparer.Ordinal))
        {
            var text = await document.GetTextAsync(cancellationToken);
            var relative = document.FilePath is null
                ? document.Name
                : Path.GetRelativePath(projectDir, document.FilePath).Replace('\\', '/');
            editorConfigs.Add(new JsonObject
            {
                ["path"] = relative,
                ["sha256"] = Sha256Hex(text.ToString()),
            });
        }

        var digestObject = new JsonObject
        {
            ["severities"] = severities,
            ["noWarn"] = noWarn,
            ["editorConfigs"] = editorConfigs,
        };

        return CanonicalJson.Sha256Hex(digestObject.ToJsonString());
    }

    private static string Sha256Hex(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static bool IsEssential(string diagnosticId) =>
        diagnosticId is "CS0006" or "CS0009" or "CS0012" or "CS0234" or "CS0246" or "CS8805";

    private static bool IsGeneratedLocation(Location location, Compilation compilation)
    {
        var tree = location.SourceTree;
        if (tree is null)
        {
            return false;
        }

        if (compilation.Options.SyntaxTreeOptionsProvider?.IsGenerated(tree, CancellationToken.None) == GeneratedKind.MarkedGenerated)
        {
            return true;
        }

        var normalized = tree.FilePath.Replace('\\', '/');
        return normalized.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith(".g.i.cs", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith(".generated.cs", StringComparison.OrdinalIgnoreCase);
    }

    private static ReportDiagnostic ToReportDiagnostic(RuleSeverity severity) => severity switch
    {
        RuleSeverity.Error => ReportDiagnostic.Error,
        RuleSeverity.Warning => ReportDiagnostic.Warn,
        RuleSeverity.Info => ReportDiagnostic.Info,
        _ => ReportDiagnostic.Suppress,
    };

    private static RuleSeverity EffectiveSeverity(
        Diagnostic diagnostic,
        Compilation compilation,
        RuleSeverity fallback,
        CancellationToken cancellationToken)
    {
        var provider = compilation.Options.SyntaxTreeOptionsProvider;
        if (diagnostic.Location.SourceTree is { } tree
            && provider?.TryGetDiagnosticValue(tree, diagnostic.Id, cancellationToken, out var perTree) == true)
        {
            return MapSeverity(perTree, fallback);
        }

        return provider?.TryGetGlobalDiagnosticValue(diagnostic.Id, cancellationToken, out var global) == true
            ? MapSeverity(global, fallback)
            : fallback;
    }

    private static RuleSeverity MapSeverity(ReportDiagnostic severity, RuleSeverity fallback) => severity switch
    {
        ReportDiagnostic.Error => RuleSeverity.Error,
        ReportDiagnostic.Warn => RuleSeverity.Warning,
        ReportDiagnostic.Info or ReportDiagnostic.Hidden => RuleSeverity.Info,
        ReportDiagnostic.Suppress => RuleSeverity.None,
        _ => fallback,
    };

    private static string TreePath(Diagnostic diagnostic) => diagnostic.Location.SourceTree?.FilePath ?? string.Empty;

    private static AnalysisProblem Problem(LoadedProject info, string stage, string message) => new()
    {
        Severity = AnalysisProblemSeverity.Error,
        Stage = stage,
        Message = message,
        ProjectPath = info.Identity,
    };
}
