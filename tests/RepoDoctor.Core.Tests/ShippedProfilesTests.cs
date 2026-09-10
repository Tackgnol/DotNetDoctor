using RepoDoctor.Core.Policy;
using Xunit;

namespace RepoDoctor.Core.Tests;

public sealed class ShippedProfilesTests
{
    private static string ProfilePath(string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "profiles", fileName);

    private static LoadedProfile Load(string fileName) =>
        ProfileDocument.Parse(File.ReadAllText(ProfilePath(fileName)), Sample.Catalog, fileName);

    [Fact]
    public void Bootstrap_profile_is_valid_and_keeps_its_original_selection()
    {
        var loaded = Load("bootstrap-server-core.json");
        var policy = ProfileResolver.Resolve(loaded.Profile, loaded.Hash, repositoryConfig: null, Sample.Catalog);

        Assert.Equal("bootstrap/server-core", loaded.Profile.Id);
        Assert.Equal("0.1.0", loaded.Profile.Version);
        Assert.Equal(0, loaded.Profile.EngineMajor);
        Assert.Equal(
            ["CA2000", "CA2012", "CA2200", "CA5359", "MA0009", "MA0022", "MA0033", "MA0040", "MA0042", "MA0054", "MA0100", "MA0134", "NUGET-VULNERABILITY"],
            policy.Rules.Select(rule => rule.RuleId));
        Assert.Equal(RuleSeverity.Warning, policy.Gate.MinimumSeverity);
        Assert.Equal(Confidence.High, policy.Gate.MinimumConfidence);
    }

    [Fact]
    public void Example_strict_gate_profile_is_valid_and_differs_from_bootstrap()
    {
        var bootstrap = Load("bootstrap-server-core.json");
        var strict = Load("example-strict-gate.json");

        var bootstrapPolicy = ProfileResolver.Resolve(bootstrap.Profile, bootstrap.Hash, null, Sample.Catalog);
        var strictPolicy = ProfileResolver.Resolve(strict.Profile, strict.Hash, null, Sample.Catalog);

        Assert.Equal(RuleSeverity.Error, strictPolicy.Gate.MinimumSeverity);
        Assert.NotEqual(bootstrapPolicy.PolicyHash, strictPolicy.PolicyHash);
        Assert.Contains(strictPolicy.Rules, r => r.RuleId == "MA0040" && r.Scope == RuleScope.Production);
    }

    [Fact]
    public void Bootstrap_profile_hash_is_stable()
    {
        Assert.Equal(Load("bootstrap-server-core.json").Hash, Load("bootstrap-server-core.json").Hash);
    }

    [Fact]
    public void Experimental_code_quality_profile_is_valid_and_standalone()
    {
        var loaded = Load("dotnet-code-quality.json");
        var policy = ProfileResolver.Resolve(loaded.Profile, loaded.Hash, repositoryConfig: null, Sample.Catalog);

        Assert.Equal("experimental/dotnet-code-quality", loaded.Profile.Id);
        Assert.Equal(
            ["MA0032", "MA0040", "MA0042", "MA0134", "MA0137", "MA0147", "MA0155", "RD1001", "RD1002", "RD1003", "RD1004", "RD1005"],
            policy.Rules.Select(rule => rule.RuleId));
        Assert.DoesNotContain(policy.Rules, rule => rule.RuleId == "NUGET-VULNERABILITY");
        Assert.Equal(RuleSeverity.Warning, policy.Gate.MinimumSeverity);
        Assert.Equal(Confidence.Unknown, policy.Gate.MinimumConfidence);
    }
}
