using System.Reflection;
using RepoDoctor.Core.Json;

namespace RepoDoctor.Core.Catalog;

public sealed class DiagnosticCatalog
{
    public const int SupportedSchemaVersion = 1;
    private const string EmbeddedResourceName = "RepoDoctor.Core.Catalog.catalog.v1.json";

    private readonly Dictionary<string, CatalogEntry> _byId;

    private DiagnosticCatalog(CatalogDocument document, string sourceJson)
    {
        Document = document;
        SourceJson = sourceJson;
        Hash = CanonicalJson.Sha256Hex(sourceJson);
        _byId = document.Diagnostics.ToDictionary(e => e.Id, StringComparer.Ordinal);
    }

    public CatalogDocument Document { get; }

    public string SourceJson { get; }

    public string Hash { get; }

    public int EngineMajor => Document.EngineMajor;

    public IReadOnlyList<CatalogEntry> Entries => Document.Diagnostics;

    public IReadOnlyList<BundleAnalyzer> Bundle => Document.Bundle.Analyzers;

    public static DiagnosticCatalog LoadDefault()
    {
        var assembly = typeof(DiagnosticCatalog).Assembly;
        using var stream = assembly.GetManifestResourceStream(EmbeddedResourceName)
            ?? throw new InvalidOperationException($"Embedded catalog resource '{EmbeddedResourceName}' is missing.");
        using var reader = new StreamReader(stream);
        return Load(reader.ReadToEnd());
    }

    public static DiagnosticCatalog Load(string json)
    {
        var document = StrictJson.Deserialize<CatalogDocument>(json, "diagnostic catalog");

        if (document.SchemaVersion != SupportedSchemaVersion)
        {
            throw new RepoDoctorValidationException(
                $"diagnostic catalog: schemaVersion {document.SchemaVersion} is not supported (expected {SupportedSchemaVersion}).");
        }

        var duplicates = document.Diagnostics
            .GroupBy(e => e.Id, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        if (duplicates.Count > 0)
        {
            throw new RepoDoctorValidationException(
                $"diagnostic catalog: duplicate diagnostic id(s): {string.Join(", ", duplicates)}.");
        }

        return new DiagnosticCatalog(document, json);
    }

    public bool Contains(string ruleId) => _byId.ContainsKey(ruleId);

    public bool TryGet(string ruleId, out CatalogEntry entry) => _byId.TryGetValue(ruleId, out entry!);

    public CatalogEntry Get(string ruleId) =>
        _byId.TryGetValue(ruleId, out var entry)
            ? entry
            : throw new RepoDoctorValidationException($"Unknown diagnostic id '{ruleId}'.");
}
