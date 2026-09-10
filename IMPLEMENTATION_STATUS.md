# Implementation status

Brief: `dotnet-repo-doctor-mvp.md`.

## Completed

- Pinned .NET 10.0.303/Roslyn 5.6 toolchain and analyzer bundle.
- Strict profile, repository override, catalog, canonical hash, and validation contracts.
- SDK project/solution discovery, MSBuildWorkspace loading, analyzer execution, exclusions, suppressions, deterministic JSON/console findings, completeness, and exit codes.
- Effective severity follows per-file analyzer configuration without promoting findings merely because the target build treats warnings as errors.
- Compatible baseline comparison with stable fingerprints, duplicate occurrence handling, and explicit incompatibility reasons.
- `init`, profile validate/export, config explain, and rule explain commands with upward configuration discovery.
- SARIF 2.1.0 output validated against the official OASIS schema; rule help links live on rule descriptors.
- Optional direct/transitive NuGet vulnerability audit using SDK JSON output, explicit provenance/freshness, failure propagation, and audited-baseline compatibility.
- Experimental `dotnet-code-quality` profile with four additional Meziantou async rules and five evaluated project-configuration checks; bootstrap selection remains unchanged.
- CircuitBreak 0.7.0 evaluation at commit `a30251d65742d7a1517acd3e5e935ee519b20ab1`: five projects complete, eight findings manually reviewed, zero scanner problems, clean live audit.
- Installable 0.1.0 tool package containing analyzers, profiles, schemas, guidance, license, and notices.
- Isolated package verification covers init, policy commands, `.csproj`, `.slnx`, JSON, SARIF, and live audit. Verified on Windows 10 and Ubuntu 24.04; the same check is committed for Windows/Linux CI.

## Current evidence

- Windows: 77 core tests and 18 integration tests; repository-local no-admin install/run and isolated installed-package checks passed, including explicit profile selection from a path containing spaces.
- Linux: 75 core tests and 12 integration tests; installed package and audited sample scan passed.
- SARIF: representative CircuitBreak output passed OASIS SARIF 2.1.0 JSON Schema validation.
- Audit: CircuitBreak and clean generated samples recorded `https://api.nuget.org/v3/index.json`, complete audit stages, and no vulnerability findings.

## Known limits

- Single-target SDK-style C# only; multi-target, legacy project systems, other languages, desktop/mobile UI stacks, and Razor/Blazor source analysis are outside the MVP boundary.
- Audit feed freshness remains `unknown` because SDK JSON output version 1 does not expose a database revision.
- Baselines are not portable across OS families.
- The broader acceptance backlog still lacks isolated fixtures for a partially failed multi-project solution and custom source generators; these are hardening work, not claims made by the packaged 0.1.0 boundary.

## Next concrete task

Run the committed CI matrix on a Git host, then address any hosted-runner-only discrepancy before tagging 0.1.0. Do not publish the package until that external CI evidence exists.
