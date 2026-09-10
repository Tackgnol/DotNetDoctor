using System.Text.Json.Nodes;
using RepoDoctor.Core.Catalog;
using RepoDoctor.Core.Json;

namespace RepoDoctor.Core.Policy;

public static class ProfileResolver
{
    public static EffectivePolicy Resolve(
        Profile profile,
        string profileHash,
        RepositoryConfig? repositoryConfig,
        DiagnosticCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(profileHash);
        ArgumentNullException.ThrowIfNull(catalog);

        PolicyValidation.ValidateProfile(profile, catalog);
        if (repositoryConfig is not null)
        {
            PolicyValidation.ValidateRepositoryConfig(repositoryConfig, catalog);
        }

        var working = new Dictionary<string, ResolvedRule>(StringComparer.Ordinal);
        foreach (var (ruleId, rule) in profile.Rules)
        {
            working[ruleId] = new ResolvedRule(rule.Severity!.Value, rule.Scope!.Value, PolicySource.Profile);
        }

        if (repositoryConfig is not null)
        {
            foreach (var (ruleId, over_) in repositoryConfig.Rules)
            {
                if (working.TryGetValue(ruleId, out var current))
                {
                    working[ruleId] = new ResolvedRule(
                        over_.Severity ?? current.Severity,
                        over_.Scope ?? current.Scope,
                        PolicySource.Repository);
                }
                else
                {
                    if (over_.Severity is null || over_.Scope is null)
                    {
                        throw new RepoDoctorValidationException(
                            $"repository config: rule '{ruleId}' is not selected by the profile, so the override must specify both severity and scope.");
                    }

                    working[ruleId] = new ResolvedRule(over_.Severity.Value, over_.Scope.Value, PolicySource.Repository);
                }
            }
        }

        var enabled = working
            .Where(kvp => kvp.Value.Severity != RuleSeverity.None)
            .Select(kvp => new EffectiveRule(kvp.Key, kvp.Value.Severity, kvp.Value.Scope, kvp.Value.Source))
            .OrderBy(r => r.RuleId, StringComparer.Ordinal)
            .ToList();

        var disabled = working
            .Where(kvp => kvp.Value.Severity == RuleSeverity.None)
            .Select(kvp => new DisabledRule(kvp.Key, kvp.Value.Source))
            .OrderBy(r => r.RuleId, StringComparer.Ordinal)
            .ToList();

        var gate = repositoryConfig?.Gate ?? profile.Gate;
        var effectiveGate = new EffectiveGate(gate.MinimumSeverity!.Value, gate.MinimumConfidence!.Value);

        var excludes = (repositoryConfig?.Exclude ?? [])
            .Select(pattern => pattern.Replace('\\', '/'))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(pattern => pattern, StringComparer.Ordinal)
            .ToList();

        var canonicalPolicyJson = BuildCanonicalPolicyJson(
            profile, profileHash, enabled, effectiveGate, excludes);

        return new EffectivePolicy
        {
            ProfileId = profile.Id,
            ProfileVersion = profile.Version,
            ProfileHash = profileHash,
            EngineMajor = profile.EngineMajor,
            Rules = enabled,
            DisabledRules = disabled,
            Gate = effectiveGate,
            Excludes = excludes,
            CanonicalPolicyJson = canonicalPolicyJson,
            PolicyHash = CanonicalJson.Sha256Hex(canonicalPolicyJson),
        };
    }

    private static string BuildCanonicalPolicyJson(
        Profile profile,
        string profileHash,
        IReadOnlyList<EffectiveRule> enabled,
        EffectiveGate gate,
        IReadOnlyList<string> excludes)
    {
        var rules = new JsonArray();
        foreach (var rule in enabled)
        {
            rules.Add(new JsonObject
            {
                ["id"] = rule.RuleId,
                ["severity"] = rule.Severity.ToWire(),
                ["scope"] = rule.Scope.ToWire(),
            });
        }

        var excludeArray = new JsonArray();
        foreach (var pattern in excludes)
        {
            excludeArray.Add(pattern);
        }

        var root = new JsonObject
        {
            ["hashAlgorithm"] = CanonicalJson.HashAlgorithmVersion,
            ["engineMajor"] = profile.EngineMajor,
            ["profile"] = new JsonObject
            {
                ["id"] = profile.Id,
                ["version"] = profile.Version,
                ["hash"] = profileHash,
            },
            ["rules"] = rules,
            ["gate"] = new JsonObject
            {
                ["minimumSeverity"] = gate.MinimumSeverity.ToWire(),
                ["minimumConfidence"] = gate.MinimumConfidence.ToWire(),
            },
            ["excludes"] = excludeArray,
        };

        return CanonicalJson.Canonicalize(root.ToJsonString());
    }

    private readonly record struct ResolvedRule(RuleSeverity Severity, RuleScope Scope, PolicySource Source);
}
