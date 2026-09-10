using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.CodeAnalysis;
using RepoDoctor.Core.Catalog;
using RepoDoctor.Core.Json;
using RepoDoctor.Core.Policy;
using RepoDoctor.Core.Reporting;

namespace RepoDoctor.Analysis;

public sealed record FindingContext(
    string ProjectIdentity,
    string? TargetFramework,
    string Configuration,
    string RepositoryRoot);

public static class FindingFactory
{
    public const string FingerprintVersion = "fp1";

    public static Finding Create(
        Diagnostic diagnostic,
        EffectiveRule rule,
        RuleSeverity effectiveSeverity,
        CatalogEntry catalog,
        FindingContext context,
        SemanticModel? semanticModel)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(context);

        var location = ToLocation(diagnostic.Location, context.RepositoryRoot);
        var message = diagnostic.GetMessage(CultureInfo.InvariantCulture);
        var symbolId = TryGetSymbolId(diagnostic.Location, semanticModel);
        var evidence = NormalizedEvidence(diagnostic.Location);
        var evidenceHash = evidence is null ? null : Sha256Hex(evidence);
        var fingerprint = BuildFingerprint(catalog.SourcePackage, diagnostic.Id, context, symbolId, evidence, message);
        var policySource = effectiveSeverity == rule.Severity ? rule.Source : PolicySource.AnalyzerConfig;

        return new Finding
        {
            Id = fingerprint,
            RuleId = diagnostic.Id,
            Source = catalog.SourcePackage,
            Category = catalog.NormalizedCategory,
            Severity = effectiveSeverity,
            Confidence = catalog.Confidence,
            ConfidenceRationale = catalog.KnownLimitations.Length == 0 ? null : catalog.KnownLimitations,
            Message = message,
            ProjectPath = context.ProjectIdentity,
            TargetFramework = context.TargetFramework,
            Configuration = context.Configuration,
            Location = location,
            SymbolId = symbolId,
            EvidenceHash = evidenceHash,
            Fingerprint = fingerprint,
            HelpUrl = string.IsNullOrWhiteSpace(catalog.HelpUrl) ? diagnostic.Descriptor.HelpLinkUri : catalog.HelpUrl,
            Remediation = string.IsNullOrWhiteSpace(catalog.ShortGuidance) ? null : catalog.ShortGuidance,
            FixAvailability = catalog.UpstreamFixer switch
            {
                RepoDoctor.Core.Catalog.FixerAvailability.UpstreamFixer => FixAvailability.UpstreamFixer,
                RepoDoctor.Core.Catalog.FixerAvailability.Manual => FixAvailability.Manual,
                _ => FixAvailability.Unknown,
            },
            BaselineState = BaselineState.Uncompared,
            PolicySource = policySource,
        };
    }

    private static SourceLocation? ToLocation(Location location, string repositoryRoot)
    {
        if (location.Kind != LocationKind.SourceFile || location.SourceTree is null)
        {
            return null;
        }

        var mapped = location.GetLineSpan();
        var relative = Path.GetRelativePath(repositoryRoot, location.SourceTree.FilePath).Replace('\\', '/');

        return new SourceLocation
        {
            FilePath = relative,
            StartLine = mapped.StartLinePosition.Line + 1,
            StartColumn = mapped.StartLinePosition.Character + 1,
            EndLine = mapped.EndLinePosition.Line + 1,
            EndColumn = mapped.EndLinePosition.Character + 1,
        };
    }

    private static string? TryGetSymbolId(Location location, SemanticModel? semanticModel)
    {
        if (semanticModel is null || location.SourceTree is null || location.SourceTree != semanticModel.SyntaxTree)
        {
            return null;
        }

        var symbol = semanticModel.GetEnclosingSymbol(location.SourceSpan.Start);
        while (symbol is not null)
        {
            if (symbol.Kind is SymbolKind.Method or SymbolKind.Property or SymbolKind.Field
                or SymbolKind.Event or SymbolKind.NamedType)
            {
                var id = symbol.GetDocumentationCommentId();
                return string.IsNullOrEmpty(id) ? null : id;
            }

            symbol = symbol.ContainingSymbol;
        }

        return null;
    }

    private static string? NormalizedEvidence(Location location)
    {
        var tree = location.SourceTree;
        if (tree is null)
        {
            return null;
        }

        var root = tree.GetRoot();
        var span = location.SourceSpan;

        if (span.IsEmpty)
        {
            var token = root.FindToken(span.Start);
            var text = token.Text.Trim();
            return text.Length == 0 ? null : text;
        }

        var node = root.FindNode(span, getInnermostNodeForTie: true);
        var tokens = node.DescendantTokens()
            .Where(t => t.Span.IntersectsWith(span) || node.Span == span)
            .Select(t => t.Text)
            .Where(t => t.Length > 0)
            .ToList();

        if (tokens.Count == 0)
        {
            tokens = node.DescendantTokens().Select(t => t.Text).Where(t => t.Length > 0).ToList();
        }

        return tokens.Count == 0 ? null : string.Join(' ', tokens);
    }

    private static string BuildFingerprint(
        string sourceFamily,
        string ruleId,
        FindingContext context,
        string? symbolId,
        string? evidence,
        string message)
    {
        var node = new JsonObject
        {
            ["v"] = FingerprintVersion,
            ["sourceFamily"] = sourceFamily,
            ["ruleId"] = ruleId,
            ["project"] = context.ProjectIdentity,
            ["tfm"] = context.TargetFramework,
            ["config"] = context.Configuration,
            ["symbolId"] = symbolId,
            ["evidence"] = evidence,
            ["message"] = message,
        };

        return FingerprintVersion + ":" + CanonicalJson.Sha256Hex(node.ToJsonString());
    }

    private static string Sha256Hex(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
