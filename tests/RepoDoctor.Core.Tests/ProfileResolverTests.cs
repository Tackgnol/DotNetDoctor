using RepoDoctor.Core;
using RepoDoctor.Core.Policy;
using Xunit;

namespace RepoDoctor.Core.Tests;

public sealed class ProfileResolverTests
{
    private static EffectivePolicy Resolve(string profileJson, string? configJson = null)
    {
        var loaded = ProfileDocument.Parse(profileJson, Sample.Catalog);
        var config = configJson is null ? null : RepositoryConfigDocument.Parse(configJson, Sample.Catalog);
        return ProfileResolver.Resolve(loaded.Profile, loaded.Hash, config, Sample.Catalog);
    }

    private const string TwoRules =
        "\"CA2200\": { \"severity\": \"warning\", \"scope\": \"all\" }, " +
        "\"CA2012\": { \"severity\": \"error\", \"scope\": \"all\" }";

    [Fact]
    public void Omitted_rule_is_disabled()
    {
        var policy = Resolve(Sample.Profile(rulesJson: TwoRules));

        Assert.Equal(new[] { "CA2012", "CA2200" }, policy.Rules.Select(r => r.RuleId));
        Assert.DoesNotContain(policy.Rules, r => r.RuleId == "MA0040");
    }

    [Fact]
    public void Repo_override_changes_severity_and_inherits_scope()
    {
        var policy = Resolve(
            Sample.Profile(rulesJson: "\"CA2200\": { \"severity\": \"warning\", \"scope\": \"production\" }"),
            Sample.Config(rulesJson: "\"CA2200\": { \"severity\": \"error\" }"));

        var rule = Assert.Single(policy.Rules);
        Assert.Equal(RuleSeverity.Error, rule.Severity);
        Assert.Equal(RuleScope.Production, rule.Scope);
        Assert.Equal(PolicySource.Repository, rule.Source);
    }

    [Fact]
    public void Repo_override_severity_none_disables_rule_but_records_it()
    {
        var policy = Resolve(
            Sample.Profile(rulesJson: TwoRules),
            Sample.Config(rulesJson: "\"CA2200\": { \"severity\": \"none\" }"));

        Assert.DoesNotContain(policy.Rules, r => r.RuleId == "CA2200");
        Assert.Contains(policy.DisabledRules, r => r.RuleId == "CA2200" && r.Source == PolicySource.Repository);
    }

    [Fact]
    public void Repo_override_enabling_rule_absent_from_profile_requires_both_fields()
    {
        Assert.Throws<RepoDoctorValidationException>(() => Resolve(
            Sample.Profile(rulesJson: TwoRules),
            Sample.Config(rulesJson: "\"MA0040\": { \"severity\": \"warning\" }")));
    }

    [Fact]
    public void Repo_override_can_enable_a_rule_absent_from_profile_with_both_fields()
    {
        var policy = Resolve(
            Sample.Profile(rulesJson: TwoRules),
            Sample.Config(rulesJson: "\"MA0040\": { \"severity\": \"warning\", \"scope\": \"all\" }"));

        Assert.Contains(policy.Rules, r => r.RuleId == "MA0040" && r.Source == PolicySource.Repository);
    }

    [Fact]
    public void Repo_gate_overrides_profile_gate()
    {
        var policy = Resolve(
            Sample.Profile(gateJson: "\"minimumSeverity\": \"warning\", \"minimumConfidence\": \"high\""),
            Sample.Config(gateJson: "\"minimumSeverity\": \"error\", \"minimumConfidence\": \"medium\""));

        Assert.Equal(RuleSeverity.Error, policy.Gate.MinimumSeverity);
        Assert.Equal(Confidence.Medium, policy.Gate.MinimumConfidence);
    }

    [Fact]
    public void Excludes_are_normalized_sorted_and_deduped()
    {
        var policy = Resolve(
            Sample.Profile(),
            Sample.Config(excludeJson: """["**\\Migrations\\**", "**/b/**", "**/a/**", "**/b/**"]"""));

        Assert.Equal(new[] { "**/Migrations/**", "**/a/**", "**/b/**" }, policy.Excludes);
    }

    [Fact]
    public void Two_different_profiles_produce_different_policy_hash()
    {
        var a = Resolve(Sample.Profile(rulesJson: "\"CA2200\": { \"severity\": \"warning\", \"scope\": \"all\" }"));
        var b = Resolve(Sample.Profile(rulesJson: "\"CA2200\": { \"severity\": \"error\", \"scope\": \"all\" }"));

        Assert.NotEqual(a.PolicyHash, b.PolicyHash);
    }

    [Fact]
    public void Same_id_and_version_but_different_content_yields_a_different_profile_hash()
    {
        var a = ProfileDocument.Parse(Sample.Profile(), Sample.Catalog);
        var b = ProfileDocument.Parse(
            Sample.Profile().Replace("\"title\": \"Sample\"", "\"title\": \"Sample edited\""),
            Sample.Catalog);

        Assert.Equal(a.Profile.Id, b.Profile.Id);
        Assert.Equal(a.Profile.Version, b.Profile.Version);
        Assert.NotEqual(a.Hash, b.Hash);
    }

    [Fact]
    public void Policy_hash_is_stable_and_independent_of_rule_key_order()
    {
        var ordered = Resolve(Sample.Profile(rulesJson:
            "\"CA2012\": { \"severity\": \"error\", \"scope\": \"all\" }, \"CA2200\": { \"severity\": \"warning\", \"scope\": \"all\" }"));
        var shuffled = Resolve(Sample.Profile(rulesJson:
            "\"CA2200\": { \"severity\": \"warning\", \"scope\": \"all\" }, \"CA2012\": { \"severity\": \"error\", \"scope\": \"all\" }"));

        Assert.Equal(ordered.PolicyHash, shuffled.PolicyHash);
    }

    [Fact]
    public void Policy_hash_includes_profile_identity()
    {
        var a = Resolve(Sample.Profile(id: "test/one"));
        var b = Resolve(Sample.Profile(id: "test/two"));

        Assert.NotEqual(a.PolicyHash, b.PolicyHash);
    }
}
