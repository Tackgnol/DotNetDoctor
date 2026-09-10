using System.Text.RegularExpressions;
using RepoDoctor.Core.Catalog;

namespace RepoDoctor.Core.Policy;

public static partial class PolicyValidation
{
    public const int SupportedSchemaVersion = 1;

    [GeneratedRegex(@"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$")]
    private static partial Regex SemanticVersionPattern();

    [GeneratedRegex(@"^[a-z0-9][a-z0-9._-]*/[a-z0-9][a-z0-9._-]*$")]
    private static partial Regex ProfileIdPattern();

    [GeneratedRegex("^[0-9a-f]{64}$")]
    private static partial Regex Sha256HexPattern();

    public static bool IsSemanticVersion(string value) => SemanticVersionPattern().IsMatch(value);

    public static void ValidateProfile(Profile profile, DiagnosticCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(catalog);

        Require(profile.SchemaVersion == SupportedSchemaVersion,
            $"profile: schemaVersion {profile.SchemaVersion} is not supported (expected {SupportedSchemaVersion}).");

        Require(profile.EngineMajor >= 0 && profile.EngineMajor <= catalog.EngineMajor,
            $"profile: engineMajor {profile.EngineMajor} is not supported by engine major {catalog.EngineMajor}.");

        Require(!string.IsNullOrWhiteSpace(profile.Id) && ProfileIdPattern().IsMatch(profile.Id),
            $"profile: id '{profile.Id}' must look like 'namespace/name' using lowercase letters, digits, '.', '_', '-'.");

        Require(PolicyValidation.IsSemanticVersion(profile.Version),
            $"profile: version '{profile.Version}' is not a semantic version string.");

        Require(!string.IsNullOrWhiteSpace(profile.Title), "profile: title is required.");

        foreach (var derived in profile.DerivedFrom)
        {
            Require(!string.IsNullOrWhiteSpace(derived.Id), "profile: derivedFrom entry is missing 'id'.");
            Require(IsSemanticVersion(derived.Version),
                $"profile: derivedFrom entry '{derived.Id}' has a non-semantic version '{derived.Version}'.");
            Require(Sha256HexPattern().IsMatch(derived.Sha256),
                $"profile: derivedFrom entry '{derived.Id}' has a sha256 that is not 64 lowercase hex characters.");
        }

        Require(profile.Rules.Count <= StrictRuleEntryLimit,
            $"profile: {profile.Rules.Count} rule entries exceed the limit of {StrictRuleEntryLimit}.");

        foreach (var (ruleId, rule) in profile.Rules)
        {
            Require(catalog.Contains(ruleId), $"profile: unknown diagnostic id '{ruleId}'.");
            Require(rule.Severity is not null, $"profile: rule '{ruleId}' is missing 'severity'. A complete profile rule needs both severity and scope.");
            Require(rule.Scope is not null, $"profile: rule '{ruleId}' is missing 'scope'. A complete profile rule needs both severity and scope.");
        }

        ValidateGate(profile.Gate, "profile", required: true);
    }

    public static void ValidateRepositoryConfig(RepositoryConfig config, DiagnosticCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(catalog);

        Require(config.SchemaVersion == SupportedSchemaVersion,
            $"repository config: schemaVersion {config.SchemaVersion} is not supported (expected {SupportedSchemaVersion}).");

        Require(!string.IsNullOrWhiteSpace(config.Profile), "repository config: 'profile' path is required.");
        Require(!Path.IsPathRooted(config.Profile),
            $"repository config: 'profile' path '{config.Profile}' must be relative to the config file directory.");
        var normalizedProfilePath = config.Profile.Replace('\\', '/');
        Require(!normalizedProfilePath.Split('/').Contains(".."),
            $"repository config: 'profile' path '{config.Profile}' must not traverse outside the repository with '..'.");

        Require(config.Rules.Count <= StrictRuleEntryLimit,
            $"repository config: {config.Rules.Count} rule entries exceed the limit of {StrictRuleEntryLimit}.");

        foreach (var (ruleId, _) in config.Rules)
        {
            Require(catalog.Contains(ruleId), $"repository config: unknown diagnostic id '{ruleId}'.");
        }

        for (var i = 0; i < config.Exclude.Count; i++)
        {
            Require(!string.IsNullOrWhiteSpace(config.Exclude[i]),
                $"repository config: exclude[{i}] is empty.");
        }

        if (config.Gate is not null)
        {
            ValidateGate(config.Gate, "repository config", required: true);
        }
    }

    private const int StrictRuleEntryLimit = 2000;

    private static void ValidateGate(GatePolicy gate, string owner, bool required)
    {
        if (!required && gate.MinimumSeverity is null && gate.MinimumConfidence is null)
        {
            return;
        }

        Require(gate.MinimumSeverity is not null, $"{owner}: gate.minimumSeverity is required.");
        Require(gate.MinimumSeverity != RuleSeverity.None,
            $"{owner}: gate.minimumSeverity must be one of info, warning, error.");
        Require(gate.MinimumConfidence is not null, $"{owner}: gate.minimumConfidence is required.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new RepoDoctorValidationException(message);
        }
    }
}
