# Compatibility matrix

## Scanner host

| Item | Pinned or verified value |
| --- | --- |
| Tool target | `net10.0` |
| SDK | `10.0.303` from `global.json` |
| Roslyn | `5.6.0` |
| Windows | Windows 10 x64, package install and audited scan verified 2026-09-09 |
| Linux | Ubuntu 24.04 x64 under WSL2, 75 core tests, 12 integration tests, package install and audited scan verified 2026-09-09 |

GitHub Actions repeats the solution tests and isolated package verification on
`windows-latest` and `ubuntu-latest`.

## Analyzed repositories

| Boundary | Status |
| --- | --- |
| SDK-style C# | Supported |
| Single-target `net8.0` and `net10.0` | Exercised |
| `.csproj` | Exercised by fixtures and installed-package verification |
| `.sln` | Exercised by the five-project CircuitBreak evaluation |
| `.slnx` | Exercised by installed-package verification |
| Test-project classification and production-only scope | Exercised |
| Multi-target projects | Detected as unsupported/incomplete; per-TFM reconciliation is not implemented |
| Legacy non-SDK, .NET Framework, F#, VB, Unity, MAUI, desktop UI, Razor/Blazor source analysis | Not supported |

The scanner requires the repository's selected SDK and restored assets. It
does not silently restore during static analysis. `--audit` also uses
`--no-restore`; a missing SDK, assets file, source response, or parseable audit
result makes the requested scan incomplete.

## Pinned dependencies

| Package family | Version |
| --- | --- |
| Microsoft.Build.Locator | `1.11.2` |
| Microsoft.CodeAnalysis | `5.6.0` |
| Microsoft.CodeAnalysis.NetAnalyzers | `10.0.303` |
| Meziantou.Analyzer | `3.0.224` (`roslyn5.6` assembly) |
| Microsoft.Extensions.FileSystemGlobbing | `10.0.11` |

`PackageDownload` acquires analyzer assemblies without applying them to
RepoDoctor's own compilation. The tool package carries both NetAnalyzers
assemblies and Meziantou.Analyzer under `bundled-analyzers/`, plus the pinned
profiles, schemas, operating guidance, license, and third-party notices.

Baselines intentionally require the same OS family, tool version, analyzer
bundle, policy, project/TFM scope, stage selection, and audit sources. Audit
freshness is recorded as unknown because the SDK output exposes no advisory
database version.
