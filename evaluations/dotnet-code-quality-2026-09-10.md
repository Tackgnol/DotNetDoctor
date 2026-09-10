# Experimental .NET code-quality evaluation

Date: 2026-09-10

Profile: `experimental/dotnet-code-quality@0.1.0`

Effective policy hash: `8e136c7371fec9b840b24ec7babcd6dbd61dc2897db41a234a7f7a4b5b2e3152`

## RepoDoctor

- Target: the current five-project `RepoDoctor.slnx` workspace snapshot; this checkout has no readable Git metadata, so no commit is claimed.
- Result: complete, gate passed, zero problems, two findings.
- Stage state: workspace load, source analysis, and project configuration complete.
- Findings: two `MA0032` info candidates in `FindingFactory.cs`, where Roslyn `GetEnclosingSymbol` and syntax `GetRoot` calls have cancellation-token overloads.
- Review: useful cancellation-propagation candidates for long scans, but not correctness defects. They are intentionally non-blocking and would require threading the scan token through the finding factory.
- Report: `artifacts/repo-doctor-code-quality.json`.

## CircuitBreak

- Target: CircuitBreak 0.7.0 archive previously recorded as commit `a30251d65742d7a1517acd3e5e935ee519b20ab1`; the archive itself contains no `.git` metadata.
- Result: complete, gate failed, zero problems, fourteen findings across five projects.
- Stage state: workspace load, source analysis, and project configuration complete.
- Findings: four `MA0042` warnings, five `RD1004` info findings, and five `RD1005` info findings.
- `MA0042`: the demo's async top-level flow performs four small sequential file writes. These are legitimate one-shot generator choices rather than demonstrated responsiveness defects, although async writes would satisfy the strict policy.
- `RD1004`: all projects leave `EnforceCodeStyleInBuild=false`. This is a useful repository-wide policy gap if build-time style enforcement is desired.
- `RD1005`: Central Package Management is not enabled. CircuitBreak pins package versions directly, so this is optional governance guidance rather than a defect.
- No `MA0032`, `MA0137`, `MA0147`, or `MA0155` finding appeared.
- Report: `artifacts/circuitbreak-code-quality.json`.

The checked-out CircuitBreak archive is nested below RepoDoctor and therefore inherits RepoDoctor's parent `Directory.Packages.props` during direct MSBuild operations. The recorded run used an isolated temporary copy so the results reflect CircuitBreak's own repository configuration. The temporary copy was restored before scanning; RepoDoctor did not edit source, project, policy, or restore assets during either scan.

This sample validates the implemented path and highlights reviewable exceptions. It is not evidence of broad ASP.NET Core coverage.

## Verification

- Windows: 77 Core tests and 18 integration tests passed in Release configuration.
- The isolated package check installed the packed tool, validated, explained, exported, selected, and executed the packaged profile, completed its project-configuration stage from a path containing spaces, and retained the existing JSON, SARIF, and audit checks.
- The committed CI matrix covers Windows and Linux, but the new profile has not yet run on a hosted Linux runner.
