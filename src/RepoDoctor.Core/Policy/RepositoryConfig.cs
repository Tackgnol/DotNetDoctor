using System.Text.Json.Serialization;

namespace RepoDoctor.Core.Policy;

public sealed record RepositoryConfig
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; init; }

    [JsonPropertyName("profile")]
    public string Profile { get; init; } = string.Empty;

    [JsonPropertyName("rules")]
    public IReadOnlyDictionary<string, RepositoryRuleOverride> Rules { get; init; } =
        new Dictionary<string, RepositoryRuleOverride>(StringComparer.Ordinal);

    [JsonPropertyName("exclude")]
    public IReadOnlyList<string> Exclude { get; init; } = [];

    [JsonPropertyName("gate")]
    public GatePolicy? Gate { get; init; }
}

public sealed record RepositoryRuleOverride
{
    [JsonPropertyName("severity")]
    public RuleSeverity? Severity { get; init; }

    [JsonPropertyName("scope")]
    public RuleScope? Scope { get; init; }
}
