using RepoDoctor.Core.Catalog;

namespace RepoDoctor.Core.Tests;

internal static class Sample
{
    public static readonly DiagnosticCatalog Catalog = DiagnosticCatalog.LoadDefault();

    public const string DefaultRules = "\"CA2200\": { \"severity\": \"warning\", \"scope\": \"all\" }";

    public const string DefaultGate = "\"minimumSeverity\": \"warning\", \"minimumConfidence\": \"high\"";

    public static string Profile(
        string id = "test/minimal",
        string version = "1.0.0",
        int schemaVersion = 1,
        int engineMajor = 0,
        string rulesJson = DefaultRules,
        string gateJson = DefaultGate)
        => $$"""
        {
          "schemaVersion": {{schemaVersion}},
          "id": "{{id}}",
          "version": "{{version}}",
          "title": "Sample",
          "description": "",
          "targetContext": "",
          "authors": [],
          "derivedFrom": [],
          "engineMajor": {{engineMajor}},
          "rules": { {{rulesJson}} },
          "gate": { {{gateJson}} }
        }
        """;

    public static string Config(
        string profilePath = "./.repo-doctor/profile.json",
        int schemaVersion = 1,
        string? rulesJson = null,
        string? excludeJson = null,
        string? gateJson = null)
    {
        var parts = new List<string>
        {
            $"\"schemaVersion\": {schemaVersion}",
            $"\"profile\": \"{profilePath}\"",
        };

        if (rulesJson is not null)
        {
            parts.Add($"\"rules\": {{ {rulesJson} }}");
        }

        if (excludeJson is not null)
        {
            parts.Add($"\"exclude\": {excludeJson}");
        }

        if (gateJson is not null)
        {
            parts.Add($"\"gate\": {{ {gateJson} }}");
        }

        return "{\n  " + string.Join(",\n  ", parts) + "\n}";
    }
}
