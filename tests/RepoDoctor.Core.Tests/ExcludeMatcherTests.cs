using RepoDoctor.Core.Policy;
using Xunit;

namespace RepoDoctor.Core.Tests;

public sealed class ExcludeMatcherTests
{
    [Fact]
    public void Empty_matcher_excludes_nothing()
    {
        var matcher = new ExcludeMatcher([]);

        Assert.True(matcher.IsEmpty);
        Assert.False(matcher.IsExcluded("src/Anything.cs"));
    }

    [Theory]
    [InlineData("**/Migrations/**", "src/Data/Migrations/0001_Init.cs", true)]
    [InlineData("**/Migrations/**", "Migrations/0001_Init.cs", true)]
    [InlineData("**/Migrations/**", "src/Data/Model.cs", false)]
    [InlineData("**/*.Designer.cs", "Forms/Main.Designer.cs", true)]
    [InlineData("src/Generated/**", "src/Generated/Api.g.cs", true)]
    [InlineData("src/Generated/**", "src/Hand/Api.cs", false)]
    public void Glob_matches_on_normalized_repository_relative_paths(string pattern, string path, bool expected)
    {
        var matcher = new ExcludeMatcher([pattern]);

        Assert.Equal(expected, matcher.IsExcluded(path));
        Assert.Equal(expected, matcher.IsExcluded(path.Replace('/', '\\')));
    }

    [Fact]
    public void Matching_is_case_sensitive()
    {
        var matcher = new ExcludeMatcher(["**/Migrations/**"]);

        Assert.True(matcher.IsExcluded("db/Migrations/x.cs"));
        Assert.False(matcher.IsExcluded("db/migrations/x.cs"));
    }
}
