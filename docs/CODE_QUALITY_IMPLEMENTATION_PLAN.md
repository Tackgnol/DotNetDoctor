# Implementation handoff: opt-in .NET code quality analysis

Status: implemented on 2026-09-10; verification evidence is recorded in `evaluations/dotnet-code-quality-2026-09-10.md`.

## Objective and scope

Implement an opt-in RepoDoctor profile based on the enforceable recommendations in [the archived article](references/making-ai-write-high-quality-code-in-dotnet.md). Deliver async correctness/naming checks and project configuration checks through the existing scan, policy, report, baseline, and gate workflow.

The user approved planning this feature for another agent. This handoff defines the implementation scope when assigned to that agent. It does not authorize publishing packages or modifying repositories being scanned.

Keep the bootstrap profile and recommendation manifest unchanged. Ship a separate, self-contained `profiles/dotnet-code-quality.json` with identity `experimental/dotnet-code-quality`, version `0.1.0`, and honest experimental labeling. Users adopt it through the existing repository configuration file; there is no new CLI switch, command, remote profile, or analyzer plugin mechanism.

First release excludes custom checks for boolean parameters, vague type names, regions, single-implementation interfaces, inheritance, and materialized return types. Document those as review guidance. Their presence alone does not prove a defect. Sonar, Roslynator, and additional test analyzer packages are also outside this slice: use the already bundled analyzers.

## Read before implementing

Read `CLAUDE.md`, [coding practices](CODING_PRACTICES.md), the archived article, and sections 5–14 of `../dotnet-repo-doctor-mvp.md`. This handoff intentionally extends the original catalog with project configuration checks, while preserving its architecture and compatibility constraints. Consult [configuration precedence](CONFIGURATION.md), [compatibility](COMPATIBILITY.md), and [current status](../IMPLEMENTATION_STATUS.md) for existing behavior; confirm it in code and tests.

The article supplies motivation, not authoritative SDK semantics. Check candidate rules against the pinned analyzer assemblies and version-matched upstream documentation. Check MSBuild behavior against the pinned SDK. Do not copy package versions or blanket `AnalysisMode=All` settings from the article.

## Relevant implementation seams

| Existing code | Work needed |
| --- | --- |
| `src/RepoDoctor.Core/Catalog/catalog.v1.json`, `CatalogEntry.cs` | Register selected upstream rules and a `project-configuration` entry kind |
| `src/RepoDoctor.Analysis/AnalyzerBundle.cs` | Inspect actual diagnostic descriptors; reuse existing loading and selection |
| `src/RepoDoctor.Analysis/ProjectAnalyzer.cs` | Preserve real analyzer execution, generated-code filtering, severity precedence, and suppression behavior |
| `src/RepoDoctor.Analysis/WorkspaceLoad.cs`, `MsBuildHost.cs` | Establish the project/TFM/configuration and SDK context for evaluated properties |
| `src/RepoDoctor.Analysis/ScanEngine.cs` | Run selected configuration checks and aggregate their findings, stage state, rule accounting, cancellation, and failures |
| `src/RepoDoctor.Core/Reporting/` | Reuse `Finding`, sorting, fingerprint versioning, gate evaluation, baseline comparison, JSON, console, and SARIF |
| `src/RepoDoctor.Core/Policy/`, `src/RepoDoctor.Cli/CliRunner.cs` | Keep validation, export, explain, and profile adoption working for new rule IDs |
| `tests/RepoDoctor.Core.Tests/`, `tests/RepoDoctor.IntegrationTests/`, `tests/Fixtures/` | Extend current xUnit and real-project test infrastructure |
| `scripts/verify-package.ps1`, `.github/workflows/ci.yml`, `src/RepoDoctor.Cli/RepoDoctor.Cli.csproj` | Verify packaged assets and the installed opt-in workflow on Windows/Linux |

Current constraints to account for:

- `LoadedProject` does not expose evaluated MSBuild properties. `WorkspaceLoad` uses raw XML/heuristics for some metadata. File presence or XML text is insufficient for the new checks.
- `CatalogEntryKind` currently distinguishes source diagnostics and dependency audit only. Search every consumer when adding its third kind.
- `ProjectAnalyzer` filters by `bundle.Supports(ruleId)`. Engine configuration rules must have their own execution path and accurate rule accounting.
- The current catalog's source rules have `unknown` confidence. Severity alone does not establish whether a finding blocks a gate.
- `ShippedProfilesTests` assumes bootstrap selects every catalog entry; `DiagnosticCatalogTests` assumes a fixed catalog count. Replace those assumptions with assertions preserving the original bootstrap selection and proving the new profile's selection. Do not expand bootstrap to satisfy them.
- `ScanEngine` currently uses the catalog hash as `BundleHash`. Catalog additions will invalidate existing baselines under current rules; document that consequence rather than bypassing the compatibility check.

## 1. Verify and register upstream async rules

Inspect `AnalyzerBundle.DescriptorsById` from the existing pinned bundle. Establish exact behavior with real analyzer execution before committing each candidate to the profile.

| Rule | Intended role | Initial profile severity |
| --- | --- | --- |
| `MA0147` | Async-void delegate misuse | warning |
| `MA0155` | Async-void methods | warning |
| `MA0137` | Awaitable-returning methods missing the `Async` suffix | info |
| `MA0032` | Token-accepting overload available when no cancellation token is in scope | info |
| `MA0040` (already registered) | Forward an available cancellation token | warning |
| `MA0042` (already registered) | Blocking operations in async code | warning |
| `MA0134` (already registered) | Unobserved async results in supported contexts | warning |

Treat `MA0032` as a candidate for adding cancellation support, not proof that every async method needs a token. Pure computation and fixed framework contracts need appropriate treatment. Verify whether the two async-void rules overlap, how event handlers are handled, and how naming rules handle overrides, interfaces, task-returning non-`async` methods, and entry points. Select both async-void rules only where their coverage is useful; record any omission and its reason.

Use accurate descriptor titles, help URLs, limitations, and fixer metadata. Retain honest confidence ratings; never raise confidence merely to make the gate block. Keep any new analyzer behavior options in the repository's existing analyzer configuration mechanism.

Completion: every selected candidate exists in the pinned bundle and has an executed failing example, corrected example, and relevant exception/limitation case. Existing source suppression and severity tests remain green.

## 2. Evaluate project settings and report configuration gaps

Add one small concrete implementation in the Analysis project, such as `ProjectConfigurationChecks.cs`. Keep Roslyn/MSBuild dependencies out of Core. Evaluate only selected subject projects when at least one applicable configuration rule is enabled. Respect production-only scope and existing unsupported-project handling.

Prefer the SDK's `dotnet msbuild <project> -getProperty:<comma-separated-properties>` structured output, without invoking build or restore targets, using `ProcessStartInfo.ArgumentList`. Pass the same configuration and supported TFM as the scan. Set the working directory to respect the target repository's `global.json`, and establish that evaluation uses the same SDK context as workspace loading; an unresolved mismatch is an incomplete evaluation. Verify the exact command and JSON shape against the pinned SDK. Use a direct MSBuild evaluation API only if the existing host can do it correctly with less complexity; explain that choice in the implementation handoff.

Evaluate final imported/conditional values, including `Directory.Build.props`, `Directory.Build.targets`, project overrides, and `Directory.Packages.props`. Never infer effective settings from those files merely existing. Avoid parallel SDK evaluations until there is a demonstrated need.

Proposed stable engine IDs and predicates:

| ID | Configuration check | Initial severity |
| --- | --- | --- |
| `RD1001` | Nullable warning analysis is not enabled project-wide: evaluated `Nullable` is neither `enable` nor `warnings` | warning |
| `RD1002` | SDK .NET analyzers are disabled, or analyzer execution during build is explicitly disabled | warning |
| `RD1003` | Compiler/analyzer warnings are not comprehensively treated as build errors | info |
| `RD1004` | Code-style enforcement during build is disabled | info |
| `RD1005` | Central Package Management is not enabled | info |

Query at least `Nullable`, `EnableNETAnalyzers`, `RunAnalyzers`, `RunAnalyzersDuringBuild`, `TreatWarningsAsErrors`, `CodeAnalysisTreatWarningsAsErrors`, `WarningsAsErrors`, `WarningsNotAsErrors`, `EnforceCodeStyleInBuild`, and `ManagePackageVersionsCentrally`. Record `AnalysisLevel` and `AnalysisMode` as context; `Recommended` is a valid choice. Do not require `All`, `latest`, or `ImplicitUsings` for code quality.

For `RD1002` and `RD1003`, first establish the SDK's defaults and precedence with fixtures. Empty values can mean inherited/default behavior, not `false`. Distinguish selective warning promotion from all-warning enforcement and describe exemptions accurately. If a trustworthy predicate cannot be evaluated, report an analysis problem rather than assert that configuration is good or bad.

Configuration findings describe repository policy coverage, not source defects. For example, `RD1001` says project-wide nullable warnings are disabled; it does not claim to have found a null dereference or ignore the possibility of local `#nullable` directives. `RD1005` is an optional package-governance recommendation, not evidence of a vulnerable or inconsistent dependency graph.

Each finding includes the normalized project identity, evaluated configuration/TFM, observed values, expected policy, and specific remediation. Reuse `Finding.Message` and `Remediation`; add structured fields only if a concrete consumer requires them. Use `Location = null` when a precise originating property location is unavailable. Never invent source line numbers or report values as originating from the `.csproj` if imports supplied them.

Completion: fixtures prove inherited settings, project overrides, SDK defaults, conditional settings, and nullable modes are interpreted correctly. Scanning leaves source, project, policy, and restore assets unchanged. No requested evaluation can fail and still produce a passing complete report.

## 3. Integrate policy, reporting, and baselines

- Register engine rules with `kind: project-configuration`, an honest engine source, and the `configuration` category. Use a real documented help link; add anchors to the existing configuration guide if no suitable product documentation URL exists yet. Do not manufacture a hosted URL.
- Add a `project-configuration` analysis stage: disabled when unselected, complete after successful evaluation (including zero findings), and failed/partial when requested evaluation fails. Distinguish disabled, skipped by scope, attempted, completed, and failed rules. Preserve findings from other successful projects when one project fails.
- Apply existing profile selection, repository JSON overrides, severity, gate, and production scope semantics. Source `.editorconfig` diagnostic suppression and source exclusion globs do not suppress project-level configuration findings; document that these rules are controlled through profile/repository JSON.
- Use stable configuration finding fingerprints derived from a versioned rule identity, repository-relative project, configuration, and TFM. Put observed property values in evidence, so a changed value can be explained without making unchanged policy gaps new occurrences. Reuse canonical hashing and deterministic occurrence handling.
- Prove identical rescans, different checkout roots, and a fixed configuration gap compare correctly. Policy changes remain incompatible. Configuration values being checked are observations, not automatically new policy hashes. Preserve existing analyzer-configuration compatibility protections where changes genuinely affect source diagnostic coverage; do not indiscriminately hash every observed property into that digest.
- Retain exit codes `0` complete/pass, `1` complete/blocking findings, `2` incomplete/invalid, and `130` cancellation. Subprocess nonzero exits, malformed output, missing SDK/imports, and timeouts must prevent a clean complete result. Drain stdout/stderr separately, propagate cancellation, and terminate child processes. Diagnostic/progress text stays off JSON stdout.
- Explain evaluation's boundary: this checks settings for the selected scan configuration. It does not certify every CI build configuration, every source-level suppression, or the quality of all code.

Completion: JSON, console, SARIF, explanation commands, baseline comparison, and gate evaluation expose configuration findings and failures consistently, including configuration-only profiles and zero selected rules.

## 4. Ship the opt-in profile

Use a standalone profile selecting only the verified async rules and five configuration checks above, with `scope: all` on each selected rule. Do not silently inherit all bootstrap diagnostics or include dependency audit. The existing default profile and previously vendored profiles remain byte-for-byte unchanged.

Set the experimental profile gate to minimum severity `warning` and minimum confidence `unknown`. This deliberately gates on selected experimental warning rules regardless of confidence; document that adopting this profile is a stricter opt-in policy choice. Naming, missing-token candidates, and configuration recommendations marked `info` remain visible without blocking. Confidence still describes evidence and limitations; it is not inflated to fit policy.

Document adoption by copying the supplied profile into a repository and selecting it with `.repo-doctor.json`, then using existing `scan --config`, `profile validate`, `profile export`, and `rule explain` commands. Validate every published example against the actual CLI. Keep the article attribution and distinguish its recommendations from this selected subset.

Completion: an installed tool can validate, select, export, and execute the new profile; the package contains it; the original default scan still selects its original rules.

## 5. Acceptance checks and handoff

Use existing xUnit infrastructure and small SDK-style fixtures. Cover behavior through the real public scan path; use captured SDK JSON for parser failures so ordinary tests do not depend on live services. Reuse related cases within fixture projects rather than adding a framework or a project per rule.

| Scenario | Required evidence |
| --- | --- |
| Each selected upstream rule | Bad/corrected pair, exact rule identity, useful location, documented exception |
| Async delegates/event handlers/overrides | No accidental blanket policy; selected analyzer behavior and limitations recorded |
| Configuration precedence | Imports and project overrides produce expected evaluated values; conditioned values match the actual scan configuration |
| Partial nullable modes | `warnings` satisfies the warning-analysis check; `annotations` alone does not |
| Analyzer execution and warnings-as-errors | Explicit disabling, default/empty values, selective promotion, and exemptions are handled correctly |
| Profile controls | Original bootstrap unchanged; opt-in selection works; disabled and production-scoped rules behave correctly |
| Gate semantics | Warning/unknown can block this opt-in profile; info does not; operational failure returns 2 |
| Evaluation failures | Invalid JSON, nonzero exit, missing imports/SDK, and timeout cannot produce a clean complete stage |
| Baseline behavior | Repeated gap is existing; fixing it removes the finding; a new gap in another project already in scope is new; changed project membership or policy is incompatible |
| Reporting | JSON is one document on stdout; successful and failed SARIF validate, including locationless findings |
| Package and paths | Installed tool scans a restored fixture from a path containing spaces and loads the new profile on Windows/Linux |
| Read-only scan | Before/after checks show no source, project, policy, or restore-asset edits |

Restore the solution and fixtures, run `dotnet test RepoDoctor.slnx -c Release`, and extend/run `pwsh ./scripts/verify-package.ps1` for this profile. Use the existing Windows/Linux CI matrix. Do not assert hosted CI passed without an actual run.

Run the new profile against RepoDoctor itself and the already selected CircuitBreak checkout, without fixing or suppressing their findings as part of this feature. Record exact revision where available, tool/profile identity, completion state, counts, and a manual review of each new rule's sample findings in an evaluation Markdown file. Call out false positives, useful findings, and legitimate exceptions. CircuitBreak is a sample, not proof of broad ASP.NET Core coverage.

Update `IMPLEMENTATION_STATUS.md`, configuration/adoption documentation, and package verification assertions. Keep unrelated release-readiness backlog items visible rather than declaring the entire MVP complete through this work.

Final handoff must include changed files, selected/omitted rules and reasons, exact verification results, evaluated-property limitations, sample findings, and any unfinished acceptance row. Completion means the implemented profile and its checks pass the acceptance matrix above; documentation and parser unit tests alone are insufficient.
