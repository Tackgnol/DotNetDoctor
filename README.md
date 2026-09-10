# repo-doctor

Deterministic, pinned analyzer scans for SDK-style C# repositories.

## Try or install

.NET 10 can run the tool without installing it or changing `PATH`:

```text
dotnet tool exec RepoDoctor.Cli@0.1.0-beta.2 -- scan ./YourSolution.slnx
```

For regular use, install it for the current user. Administrator privileges are not required:

```text
dotnet tool install --global RepoDoctor.Cli --version 0.1.0-beta.2
repo-doctor init .
dotnet restore
repo-doctor scan ./YourSolution.slnx
```

To pin the tool version in a repository instead:

```text
dotnet new tool-manifest
dotnet tool install RepoDoctor.Cli --version 0.1.0-beta.2
dotnet tool run repo-doctor -- scan ./YourSolution.slnx
```

Useful outputs:

```text
repo-doctor scan ./YourSolution.slnx --format json --output artifacts/current.json
repo-doctor scan ./YourSolution.slnx --format sarif --output artifacts/current.sarif
repo-doctor scan ./YourSolution.slnx --baseline artifacts/base.json --format json --output artifacts/head.json
repo-doctor scan ./YourSolution.slnx --audit --format json --output artifacts/audited.json
```

The vendored profile and `.repo-doctor.json` pin policy locally. `--audit` runs the SDK's machine-readable direct and transitive vulnerability check against restored projects; a failed or unverifiable audit makes the scan incomplete instead of silently reporting clean. See `docs/AGENT_WORKFLOW.md`, `docs/COMPATIBILITY.md`, and `docs/PROFILE_CONTRIBUTING.md`.

For stricter opt-in async and evaluated project-policy checks, vendor `profiles/dotnet-code-quality.json` and select it in `.repo-doctor.json`. The default bootstrap profile is unchanged; see `docs/CONFIGURATION.md#project-configuration-checks`.

## Score (experimental)

Complete scans report `score = max(0, 100 - penalty)`: errors cost 10, warnings 3, and info findings 1. `penalty` is not capped; valid baseline comparisons also report score and penalty deltas. The gate remains independent and authoritative.

## No-admin local run

From this source checkout, one command packs the current code, installs it under the ignored `artifacts/local-tool` directory, and runs both shipped profiles:

```powershell
pwsh -NoProfile -File ./scripts/run-local.ps1 ./CircuitBreak-master/CircuitBreak.sln
```

Use `-Profile bootstrap` or `-Profile code-quality` to run one profile, `-Audit` to include the bootstrap NuGet audit, or `-InstallOnly` to stop after installation. Add `-Reinstall` after changing the tool's source. Nothing is installed globally and no administrator privileges or PATH changes are needed. The installed executable path is printed after installation; later runs reuse it without contacting NuGet.

The CLI also accepts a profile directly without changing the target repository:

```text
repo-doctor scan ./YourSolution.slnx --profile ./profiles/dotnet-code-quality.json
```

For release verification, run `dotnet test RepoDoctor.slnx` followed by `pwsh ./scripts/verify-package.ps1`. The package check installs the tool into an isolated path and exercises init, policy inspection, JSON, SARIF, and NuGet audit flows.

## Releasing

The `release` workflow publishes tags named `v<semver>` to NuGet.org and creates a matching GitHub release. Before the first tag, create a NuGet.org Trusted Publishing policy for owner `Tackgnol`, repository `DotNetDoctor`, workflow `release.yml`, and environment `release`; then set the GitHub Actions repository variable `NUGET_USER` to the NuGet.org profile name.
