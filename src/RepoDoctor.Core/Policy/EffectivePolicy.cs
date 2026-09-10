using RepoDoctor.Core.Json;

namespace RepoDoctor.Core.Policy;

public sealed record EffectivePolicy
{
    public required string ProfileId { get; init; }

    public required string ProfileVersion { get; init; }

    public required string ProfileHash { get; init; }

    public required int EngineMajor { get; init; }

    public required IReadOnlyList<EffectiveRule> Rules { get; init; }

    public required IReadOnlyList<DisabledRule> DisabledRules { get; init; }

    public required EffectiveGate Gate { get; init; }

    public required IReadOnlyList<string> Excludes { get; init; }

    public required string CanonicalPolicyJson { get; init; }

    public required string PolicyHash { get; init; }

    public static string HashAlgorithm => CanonicalJson.HashAlgorithmVersion;
}

public sealed record EffectiveRule(string RuleId, RuleSeverity Severity, RuleScope Scope, PolicySource Source);

public sealed record DisabledRule(string RuleId, PolicySource Source);

public sealed record EffectiveGate(RuleSeverity MinimumSeverity, Confidence MinimumConfidence);
