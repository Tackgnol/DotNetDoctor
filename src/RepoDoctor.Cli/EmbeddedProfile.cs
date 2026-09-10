using RepoDoctor.Core;
using RepoDoctor.Core.Catalog;
using RepoDoctor.Core.Json;
using RepoDoctor.Core.Policy;
using System.Text.Json.Serialization;

namespace RepoDoctor.Cli;

public static class EmbeddedProfile
{
    private const string ManifestResourceName = "RepoDoctor.Cli.recommendations.v1.json";
    private const string ProfileResourcePrefix = "RepoDoctor.Cli.Profiles.";

    public static string RecommendedJson()
    {
        var assembly = typeof(EmbeddedProfile).Assembly;
        using var manifestStream = assembly.GetManifestResourceStream(ManifestResourceName)
            ?? throw new InvalidOperationException($"Embedded recommendation manifest '{ManifestResourceName}' is missing from the tool package.");
        using var manifestReader = new StreamReader(manifestStream);
        var manifest = StrictJson.Deserialize<RecommendationManifest>(manifestReader.ReadToEnd(), "embedded recommendation manifest");
        if (manifest.SchemaVersion != 1 || manifest.Recommendations.Count == 0)
        {
            throw new RepoDoctorValidationException("embedded recommendation manifest is empty or unsupported.");
        }

        var recommended = manifest.Recommendations[0];
        var resourceName = ProfileResourcePrefix + recommended.ProfileFile;
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded profile '{resourceName}' is missing from the tool package.");
        using var reader = new StreamReader(stream);
        var json = reader.ReadToEnd();
        var catalog = DiagnosticCatalog.LoadDefault();
        var profile = ProfileDocument.Parse(json, catalog, "embedded recommended profile");
        if (profile.Profile.Id != recommended.ProfileId || profile.Profile.Version != recommended.ProfileVersion || profile.Hash != recommended.Sha256)
        {
            throw new RepoDoctorValidationException("embedded recommended profile does not match its manifest identity.");
        }

        return json;
    }

    public static LoadedProfile LoadRecommended(DiagnosticCatalog catalog)
    {
        var profile = ProfileDocument.Parse(RecommendedJson(), catalog, "embedded recommended profile");
        return profile;
    }

    private sealed record RecommendationManifest
    {
        [JsonPropertyName("schemaVersion")]
        public int SchemaVersion { get; init; }

        [JsonPropertyName("recommendations")]
        public IReadOnlyList<Recommendation> Recommendations { get; init; } = [];
    }

    private sealed record Recommendation
    {
        [JsonPropertyName("context")]
        public string Context { get; init; } = string.Empty;

        [JsonPropertyName("profileFile")]
        public string ProfileFile { get; init; } = string.Empty;

        [JsonPropertyName("profileId")]
        public string ProfileId { get; init; } = string.Empty;

        [JsonPropertyName("profileVersion")]
        public string ProfileVersion { get; init; } = string.Empty;

        [JsonPropertyName("sha256")]
        public string Sha256 { get; init; } = string.Empty;
    }
}
