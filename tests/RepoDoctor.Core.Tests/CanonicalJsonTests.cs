using RepoDoctor.Core.Json;
using Xunit;

namespace RepoDoctor.Core.Tests;

public sealed class CanonicalJsonTests
{
    [Fact]
    public void Canonicalize_sorts_object_keys_recursively()
    {
        var canonical = CanonicalJson.Canonicalize("""{ "b": 1, "a": { "d": 4, "c": 3 } }""");

        Assert.Equal("""{"a":{"c":3,"d":4},"b":1}""", canonical);
    }

    [Fact]
    public void Canonicalize_preserves_array_order()
    {
        var canonical = CanonicalJson.Canonicalize("""{ "items": [3, 1, 2] }""");

        Assert.Equal("""{"items":[3,1,2]}""", canonical);
    }

    [Fact]
    public void Sha256Hex_is_key_order_independent_and_matches_known_vector()
    {
        const string knownVector = "d3626ac30a87e6f7a6428233b3c68299976865fa5508e4267c5415c76af7a772";

        Assert.Equal(knownVector, CanonicalJson.Sha256Hex("""{"b":1,"a":2}"""));
        Assert.Equal(knownVector, CanonicalJson.Sha256Hex("""{ "a" : 2 , "b" : 1 }"""));
    }

    [Fact]
    public void Sha256Hex_changes_when_array_order_changes()
    {
        var first = CanonicalJson.Sha256Hex("""{"items":[1,2]}""");
        var second = CanonicalJson.Sha256Hex("""{"items":[2,1]}""");

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Canonicalize_preserves_distinct_numeric_literals()
    {
        var first = CanonicalJson.Sha256Hex("""{"x":1}""");
        var second = CanonicalJson.Sha256Hex("""{"x":1.0}""");

        Assert.NotEqual(first, second);
    }
}
