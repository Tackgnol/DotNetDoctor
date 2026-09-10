using System.Text.Json.Serialization;

namespace RepoDoctor.Core.Policy;

public sealed record Profile
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; init; }

    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; init; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    [JsonPropertyName("targetContext")]
    public string TargetContext { get; init; } = string.Empty;

    [JsonPropertyName("authors")]
    public IReadOnlyList<string> Authors { get; init; } = [];

    [JsonPropertyName("derivedFrom")]
    public IReadOnlyList<DerivedProfile> DerivedFrom { get; init; } = [];

    [JsonPropertyName("engineMajor")]
    public int EngineMajor { get; init; }

    [JsonPropertyName("rules")]
    public IReadOnlyDictionary<string, ProfileRule> Rules { get; init; } =
        new Dictionary<string, ProfileRule>(StringComparer.Ordinal);

    [JsonPropertyName("gate")]
    public GatePolicy Gate { get; init; } = new();
}

public sealed record ProfileRule
{
    [JsonPropertyName("severity")]
    public RuleSeverity? Severity { get; init; }

    [JsonPropertyName("scope")]
    public RuleScope? Scope { get; init; }
}

public sealed record DerivedProfile
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; init; } = string.Empty;

    [JsonPropertyName("sha256")]
    public string Sha256 { get; init; } = string.Empty;
}

public sealed record GatePolicy
{
    [JsonPropertyName("minimumSeverity")]
    public RuleSeverity? MinimumSeverity { get; init; }

    [JsonPropertyName("minimumConfidence")]
    public Confidence? MinimumConfidence { get; init; }
}
