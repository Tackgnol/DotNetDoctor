using System.Text.Json.Serialization;
using RepoDoctor.Core.Policy;

namespace RepoDoctor.Core.Catalog;

public sealed record CatalogDocument
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; init; }

    [JsonPropertyName("engineMajor")]
    public int EngineMajor { get; init; }

    [JsonPropertyName("bundle")]
    public CatalogBundle Bundle { get; init; } = new();

    [JsonPropertyName("diagnostics")]
    public IReadOnlyList<CatalogEntry> Diagnostics { get; init; } = [];
}

public sealed record CatalogBundle
{
    [JsonPropertyName("analyzers")]
    public IReadOnlyList<BundleAnalyzer> Analyzers { get; init; } = [];
}

public sealed record BundleAnalyzer
{
    [JsonPropertyName("package")]
    public string Package { get; init; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; init; } = string.Empty;
}

public sealed record CatalogEntry
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("kind")]
    public CatalogEntryKind Kind { get; init; } = CatalogEntryKind.SourceDiagnostic;

    [JsonPropertyName("sourcePackage")]
    public string SourcePackage { get; init; } = string.Empty;

    [JsonPropertyName("upstreamCategory")]
    public string UpstreamCategory { get; init; } = string.Empty;

    [JsonPropertyName("normalizedCategory")]
    public string NormalizedCategory { get; init; } = string.Empty;

    [JsonPropertyName("selectionIntent")]
    public string SelectionIntent { get; init; } = string.Empty;

    [JsonPropertyName("shortGuidance")]
    public string ShortGuidance { get; init; } = string.Empty;

    [JsonPropertyName("knownLimitations")]
    public string KnownLimitations { get; init; } = string.Empty;

    [JsonPropertyName("helpUrl")]
    public string HelpUrl { get; init; } = string.Empty;

    [JsonPropertyName("upstreamFixer")]
    public FixerAvailability UpstreamFixer { get; init; } = FixerAvailability.Unknown;

    [JsonPropertyName("confidence")]
    public Confidence Confidence { get; init; } = Confidence.Unknown;
}

public enum CatalogEntryKind
{
    [JsonStringEnumMemberName("source-diagnostic")]
    SourceDiagnostic = 0,

    [JsonStringEnumMemberName("dependency-audit")]
    DependencyAudit = 1,

    [JsonStringEnumMemberName("project-configuration")]
    ProjectConfiguration = 2,
}

public enum FixerAvailability
{
    [JsonStringEnumMemberName("unknown")]
    Unknown = 0,

    [JsonStringEnumMemberName("manual")]
    Manual = 1,

    [JsonStringEnumMemberName("upstream-fixer")]
    UpstreamFixer = 2,
}
