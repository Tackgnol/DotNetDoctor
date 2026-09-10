using System.Text.Json.Serialization;

namespace RepoDoctor.Core.Policy;

public enum RuleSeverity
{
    [JsonStringEnumMemberName("none")]
    None = 0,

    [JsonStringEnumMemberName("info")]
    Info = 1,

    [JsonStringEnumMemberName("warning")]
    Warning = 2,

    [JsonStringEnumMemberName("error")]
    Error = 3,
}

public enum RuleScope
{
    [JsonStringEnumMemberName("all")]
    All = 0,

    [JsonStringEnumMemberName("production")]
    Production = 1,
}

public enum Confidence
{
    [JsonStringEnumMemberName("unknown")]
    Unknown = 0,

    [JsonStringEnumMemberName("medium")]
    Medium = 1,

    [JsonStringEnumMemberName("high")]
    High = 2,
}

public enum PolicySource
{
    [JsonStringEnumMemberName("profile")]
    Profile = 0,

    [JsonStringEnumMemberName("repository")]
    Repository = 1,

    [JsonStringEnumMemberName("analyzer-config")]
    AnalyzerConfig = 2,
}

public static class PolicyWire
{
    public static string ToWire(this RuleSeverity value) => value.ToString().ToLowerInvariant();

    public static string ToWire(this RuleScope value) => value.ToString().ToLowerInvariant();

    public static string ToWire(this Confidence value) => value.ToString().ToLowerInvariant();

    public static string ToWire(this PolicySource value) => value switch
    {
        PolicySource.Profile => "profile",
        PolicySource.Repository => "repository",
        PolicySource.AnalyzerConfig => "analyzer-config",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };
}
