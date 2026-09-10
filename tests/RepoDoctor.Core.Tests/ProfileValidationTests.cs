using RepoDoctor.Core;
using RepoDoctor.Core.Policy;
using Xunit;

namespace RepoDoctor.Core.Tests;

public sealed class ProfileValidationTests
{
    private static LoadedProfile Parse(string json) => ProfileDocument.Parse(json, Sample.Catalog);

    [Fact]
    public void Valid_minimal_profile_passes()
    {
        var loaded = Parse(Sample.Profile());

        Assert.Equal("test/minimal", loaded.Profile.Id);
        Assert.Matches("^[0-9a-f]{64}$", loaded.Hash);
    }

    [Fact]
    public void Unknown_property_is_rejected()
    {
        var json = Sample.Profile().Replace("\"engineMajor\": 0,", "\"engineMajor\": 0,\n  \"surpriseField\": true,");

        Assert.Throws<RepoDoctorValidationException>(() => Parse(json));
    }

    [Fact]
    public void Duplicate_property_is_rejected()
    {
        var json = Sample.Profile().Replace("\"version\": \"1.0.0\",", "\"version\": \"1.0.0\",\n  \"version\": \"2.0.0\",");

        var ex = Assert.Throws<RepoDoctorValidationException>(() => Parse(json));
        Assert.Contains("duplicate", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Duplicate_rule_key_is_rejected()
    {
        var json = Sample.Profile(rulesJson:
            "\"CA2200\": { \"severity\": \"warning\", \"scope\": \"all\" }, \"CA2200\": { \"severity\": \"error\", \"scope\": \"all\" }");

        var ex = Assert.Throws<RepoDoctorValidationException>(() => Parse(json));
        Assert.Contains("duplicate", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Unknown_diagnostic_id_is_rejected()
    {
        var json = Sample.Profile(rulesJson: "\"CA9999\": { \"severity\": \"warning\", \"scope\": \"all\" }");

        Assert.Throws<RepoDoctorValidationException>(() => Parse(json));
    }

    [Fact]
    public void Unsupported_schema_version_is_rejected()
    {
        Assert.Throws<RepoDoctorValidationException>(() => Parse(Sample.Profile(schemaVersion: 2)));
    }

    [Fact]
    public void Unsupported_engine_major_is_rejected()
    {
        Assert.Throws<RepoDoctorValidationException>(() => Parse(Sample.Profile(engineMajor: 99)));
    }

    [Theory]
    [InlineData("1.0")]
    [InlineData("v1.0.0")]
    [InlineData("1.0.0.0")]
    [InlineData("latest")]
    public void Non_semver_version_is_rejected(string version)
    {
        Assert.Throws<RepoDoctorValidationException>(() => Parse(Sample.Profile(version: version)));
    }

    [Fact]
    public void Profile_rule_missing_scope_is_rejected()
    {
        var json = Sample.Profile(rulesJson: "\"CA2200\": { \"severity\": \"warning\" }");

        var ex = Assert.Throws<RepoDoctorValidationException>(() => Parse(json));
        Assert.Contains("scope", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Profile_rule_missing_severity_is_rejected()
    {
        var json = Sample.Profile(rulesJson: "\"CA2200\": { \"scope\": \"all\" }");

        Assert.Throws<RepoDoctorValidationException>(() => Parse(json));
    }

    [Fact]
    public void Integer_enum_value_is_rejected()
    {
        var json = Sample.Profile(rulesJson: "\"CA2200\": { \"severity\": 2, \"scope\": \"all\" }");

        Assert.Throws<RepoDoctorValidationException>(() => Parse(json));
    }

    [Fact]
    public void Unknown_enum_value_is_rejected()
    {
        var json = Sample.Profile(rulesJson: "\"CA2200\": { \"severity\": \"critical\", \"scope\": \"all\" }");

        Assert.Throws<RepoDoctorValidationException>(() => Parse(json));
    }

    [Fact]
    public void Gate_minimum_severity_none_is_rejected()
    {
        var json = Sample.Profile(gateJson: "\"minimumSeverity\": \"none\", \"minimumConfidence\": \"high\"");

        Assert.Throws<RepoDoctorValidationException>(() => Parse(json));
    }

    [Fact]
    public void Oversized_file_is_rejected()
    {
        var big = new string('x', 300 * 1024);
        var json = Sample.Profile().Replace("\"description\": \"\",", $"\"description\": \"{big}\",");

        var ex = Assert.Throws<RepoDoctorValidationException>(() => Parse(json));
        Assert.Contains("limit", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Too_deeply_nested_json_is_rejected()
    {
        var json = new string('[', 40) + new string(']', 40);

        Assert.Throws<RepoDoctorValidationException>(
            () => RepoDoctor.Core.Json.StrictJson.Deserialize<Profile>(json, "profile"));
    }

    [Fact]
    public void Rule_count_over_limit_is_rejected()
    {
        var rules = string.Join(", ", Enumerable.Range(0, 2001)
            .Select(i => $"\"R{i}\": {{ \"severity\": \"warning\", \"scope\": \"all\" }}"));

        Assert.Throws<RepoDoctorValidationException>(() => Parse(Sample.Profile(rulesJson: rules)));
    }
}
