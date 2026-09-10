using RepoDoctor.Core.Catalog;
using RepoDoctor.Core.Json;

namespace RepoDoctor.Core.Policy;

public sealed record LoadedProfile(Profile Profile, string Hash, string CanonicalJson);

public static class ProfileDocument
{
    public static LoadedProfile Parse(string json, DiagnosticCatalog catalog, string sourceLabel = "profile")
    {
        var profile = StrictJson.Deserialize<Profile>(json, sourceLabel);
        PolicyValidation.ValidateProfile(profile, catalog);
        var canonical = CanonicalJson.Canonicalize(json);
        return new LoadedProfile(profile, CanonicalJson.Sha256Hex(canonical), canonical);
    }
}

public static class RepositoryConfigDocument
{
    public static RepositoryConfig Parse(string json, DiagnosticCatalog catalog, string sourceLabel = "repository config")
    {
        var config = StrictJson.Deserialize<RepositoryConfig>(json, sourceLabel);
        PolicyValidation.ValidateRepositoryConfig(config, catalog);
        return config;
    }
}
