using RepoDoctor.Analysis;
using RepoDoctor.Core;
using Xunit;

namespace RepoDoctor.IntegrationTests;

public sealed class NuGetAuditTests
{
    [Fact]
    public void Captured_sdk_output_maps_direct_and_transitive_vulnerabilities()
    {
        const string json = """
            {
              "version": 1,
              "parameters": "--vulnerable --include-transitive",
              "sources": ["https://api.nuget.org/v3/index.json"],
              "projects": [
                {
                  "path": "src/App/App.csproj",
                  "frameworks": [
                    {
                      "framework": "net10.0",
                      "topLevelPackages": [
                        {
                          "id": "Direct.Package",
                          "requestedVersion": "1.0.0",
                          "resolvedVersion": "1.0.0",
                          "vulnerabilities": [
                            { "severity": "High", "advisoryurl": "https://github.com/advisories/GHSA-aaaa-bbbb-cccc" }
                          ]
                        }
                      ],
                      "transitivePackages": [
                        {
                          "id": "Transitive.Package",
                          "resolvedVersion": "2.0.0",
                          "vulnerabilities": [
                            { "severity": "Moderate", "advisoryurl": "https://github.com/advisories/GHSA-dddd-eeee-ffff" }
                          ]
                        }
                      ]
                    }
                  ]
                }
              ]
            }
            """;

        var result = NuGetAudit.Parse(json, "src/App/App.csproj");

        Assert.Equal(2, result.Occurrences.Count);
        Assert.Contains(result.Occurrences, o => o.PackageId == "Direct.Package" && o.Relationship == "direct");
        Assert.Contains(result.Occurrences, o => o.PackageId == "Transitive.Package" && o.Relationship == "transitive");
    }

    [Fact]
    public void Empty_success_is_accepted_only_when_the_sdk_identifies_its_source()
    {
        var result = NuGetAudit.Parse(
            """{"version":1,"sources":["https://api.nuget.org/v3/index.json"],"projects":[{"path":"App.csproj"}]}""",
            "App.csproj");

        Assert.Empty(result.Occurrences);
        Assert.Throws<RepoDoctorValidationException>(() => NuGetAudit.Parse(
            """{"version":1,"sources":[],"projects":[{"path":"App.csproj"}]}""",
            "App.csproj"));
    }
}
