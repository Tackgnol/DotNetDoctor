using RepoDoctor.Core;
using RepoDoctor.Cli;
using Xunit;

namespace RepoDoctor.IntegrationTests;

public sealed class ScanArgumentsTests
{
    [Fact]
    public void Scan_accepts_an_explicit_profile()
    {
        var profile = Path.Combine(AppContext.BaseDirectory, "profiles", "dotnet-code-quality.json");

        var arguments = ScanArguments.Parse(["target.csproj", "--profile", profile]);

        Assert.Equal(Path.GetFullPath(profile), arguments.ProfilePath);
        Assert.Null(arguments.ConfigPath);
    }

    [Fact]
    public void Scan_rejects_profile_and_repository_config_together()
    {
        var profile = Path.Combine(AppContext.BaseDirectory, "profiles", "dotnet-code-quality.json");

        Assert.Throws<RepoDoctorValidationException>(() => ScanArguments.Parse(
            ["target.csproj", "--profile", profile, "--config", "repo-doctor.json"]));
    }
}
