using RepoDoctor.Core;
using RepoDoctor.Core.Catalog;
using RepoDoctor.Core.Policy;
using Xunit;

namespace RepoDoctor.Core.Tests;

public sealed class DiagnosticCatalogTests
{
    [Fact]
    public void Default_catalog_has_source_configuration_and_audit_families()
    {
        var catalog = DiagnosticCatalog.LoadDefault();

        Assert.Equal(22, catalog.Entries.Count);
        Assert.Equal(16, catalog.Entries.Count(e => e.Kind == CatalogEntryKind.SourceDiagnostic));
        Assert.Equal(5, catalog.Entries.Count(e => e.Kind == CatalogEntryKind.ProjectConfiguration));
        Assert.True(catalog.Contains("NUGET-VULNERABILITY"));
        Assert.Equal(CatalogEntryKind.DependencyAudit, catalog.Get("NUGET-VULNERABILITY").Kind);
    }

    [Fact]
    public void Source_diagnostics_start_at_unknown_confidence_engine_rules_are_high()
    {
        var catalog = DiagnosticCatalog.LoadDefault();

        Assert.All(
            catalog.Entries.Where(e => e.Kind == CatalogEntryKind.SourceDiagnostic),
            e => Assert.Equal(Confidence.Unknown, e.Confidence));
        Assert.All(
            catalog.Entries.Where(e => e.Kind == CatalogEntryKind.ProjectConfiguration),
            e => Assert.Equal(Confidence.High, e.Confidence));
        Assert.Equal(Confidence.High, catalog.Get("NUGET-VULNERABILITY").Confidence);
    }

    [Fact]
    public void Every_entry_has_help_url_guidance_and_normalized_category()
    {
        var catalog = DiagnosticCatalog.LoadDefault();

        Assert.All(catalog.Entries, e =>
        {
            Assert.False(string.IsNullOrWhiteSpace(e.HelpUrl));
            Assert.False(string.IsNullOrWhiteSpace(e.ShortGuidance));
            Assert.False(string.IsNullOrWhiteSpace(e.NormalizedCategory));
        });
    }

    [Fact]
    public void Catalog_hash_is_deterministic()
    {
        Assert.Equal(DiagnosticCatalog.LoadDefault().Hash, DiagnosticCatalog.LoadDefault().Hash);
        Assert.Matches("^[0-9a-f]{64}$", DiagnosticCatalog.LoadDefault().Hash);
    }

    [Fact]
    public void Catalog_rejects_duplicate_ids()
    {
        const string json = """
        {
          "schemaVersion": 1,
          "engineMajor": 0,
          "bundle": { "analyzers": [] },
          "diagnostics": [
            { "id": "CA2200", "helpUrl": "x", "shortGuidance": "x", "normalizedCategory": "x" },
            { "id": "CA2200", "helpUrl": "y", "shortGuidance": "y", "normalizedCategory": "y" }
          ]
        }
        """;

        Assert.Throws<RepoDoctorValidationException>(() => DiagnosticCatalog.Load(json));
    }

    [Fact]
    public void Catalog_rejects_unsupported_schema_version()
    {
        const string json = """
        { "schemaVersion": 2, "engineMajor": 0, "bundle": { "analyzers": [] }, "diagnostics": [] }
        """;

        Assert.Throws<RepoDoctorValidationException>(() => DiagnosticCatalog.Load(json));
    }
}
