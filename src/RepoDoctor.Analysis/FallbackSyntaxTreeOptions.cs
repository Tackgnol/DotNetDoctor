using Microsoft.CodeAnalysis;

namespace RepoDoctor.Analysis;

internal sealed class FallbackSyntaxTreeOptions(
    SyntaxTreeOptionsProvider? inner,
    IReadOnlyDictionary<string, ReportDiagnostic> fallbackGlobalSeverities) : SyntaxTreeOptionsProvider
{
    public override bool TryGetGlobalDiagnosticValue(string diagnosticId, CancellationToken cancellationToken, out ReportDiagnostic severity)
    {
        if (inner is not null && inner.TryGetGlobalDiagnosticValue(diagnosticId, cancellationToken, out severity))
        {
            return true;
        }

        return fallbackGlobalSeverities.TryGetValue(diagnosticId, out severity);
    }

    public override bool TryGetDiagnosticValue(SyntaxTree tree, string diagnosticId, CancellationToken cancellationToken, out ReportDiagnostic severity)
    {
        if (inner is not null && inner.TryGetDiagnosticValue(tree, diagnosticId, cancellationToken, out severity))
        {
            return true;
        }

        severity = ReportDiagnostic.Default;
        return false;
    }

    public override GeneratedKind IsGenerated(SyntaxTree tree, CancellationToken cancellationToken)
        => inner?.IsGenerated(tree, cancellationToken) ?? GeneratedKind.Unknown;
}
