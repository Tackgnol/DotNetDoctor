# .NET Repository Doctor: MVP implementation brief

Version: 1.0  
Prepared: 2026-09-07  
Audience: an implementation agent such as Sonnet or Sol  
Status: executable product and engineering specification; the application does not exist yet  
Working command name: `repo-doctor`; package and repository names are provisional

## 1. Build this product

Build a local command-line tool that scans a C#/.NET repository, runs a small selection of established analyzers, explains its findings, and identifies regressions against an earlier scan. Humans and coding agents must receive the same findings and policy decisions.

The first release proves the scan/report/compare workflow. It does not attempt to compete on the number of rules.

The product has two equally important goals:

1. Catch useful, reproducible problems without burying users in stylistic noise.
2. Let people share, inspect, adapt, and improve the policy that determines which checks matter.

The engine supplies analysis capabilities. A versioned profile selects rules and their reporting policy. Repository owners retain the final choice. The project's initial profile is a provisional starting point, and the project must make it possible for a better community profile to become the recommended starting point.

Success means an agent can scan a repository, understand a specific finding, fix the underlying problem, run the relevant tests, and demonstrate that the finding disappeared without concealing other regressions.

### 1.1 What inspired the design

React Doctor combines existing analysis tooling, custom checks, project discovery, diagnostic comparison, and an agent workflow. Its source inspection informed this brief. Its implementation is not the implementation plan for C#.

Roslyn exposes C# syntax and semantic information and supports analyzer execution. Reuse that infrastructure. Do not build a text-matching approximation of the C# compiler. See the [Roslyn overview](https://learn.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/) and [analyzer execution API](https://learn.microsoft.com/en-us/dotnet/api/microsoft.codeanalysis.diagnostics.compilationwithanalyzers).

Implement original orchestration code. Do not copy React Doctor source or prompts into this project. The inspected [React Doctor license](https://github.com/millionco/react-doctor/blob/ff7dd679e8b9939a7dd8f828a530559a275836f7/LICENSE) contains additional restrictions. Record the licenses and redistribution notices for the dependencies actually shipped.

## 2. Instructions to the implementation agent

Read this entire document before editing. Then inspect the target repository, its `AGENTS.md` files, existing conventions, and installed SDKs.

- Work through the milestones in section 16 in order.
- Finish a runnable vertical slice before building additional abstractions.
- Keep a short `IMPLEMENTATION_STATUS.md` with completed milestones, evidence, known limitations, and the next concrete task.
- Make ordinary implementation decisions autonomously within this specification.
- Ask only when a missing input or incompatible existing requirement prevents correct progress. An unavailable SDK or failed restore is a concrete blocker; a preference for a different JSON library is not.
- Do not add comments to code, including XML documentation comments. Put explanations in Markdown documentation and use clear names.
- Avoid speculative extension points, generic plugin containers, event buses, databases, and web services.
- Never invent SDK or NuGet versions. Resolve a compatible stable combination, pin it, and record it.
- Do not use `latest`, floating package versions, or prerelease dependencies in the delivered reproducible setup.
- Do not introduce an LLM API dependency. This tool must work without an AI account.
- Do not solve failing fixtures by disabling the rule being tested or weakening the gate.
- Do not silently remove acceptance criteria to make the build pass. Record a limitation explicitly and leave the relevant milestone incomplete.
- Do not publish packages, create remote repositories, or deploy services merely because this specification discusses distribution. Produce a local package and reviewable changes first.

Treat examples below as proposed contracts, not proof that the commands or package names already exist.

## 3. Scope and deliberate limits

### 3.1 Required for MVP completion

| Capability | Required behavior |
| --- | --- |
| Repository discovery | Accept an explicit C# project or solution, or discover an unambiguous target in a directory |
| Semantic loading | Load supported projects with their actual references, compiler settings, and necessary generated sources |
| Analyzer execution | Run selected diagnostics from a fixed, pinned bundle of existing analyzers |
| Shareable profiles | Load, validate, export, and explain local JSON profiles |
| Repository control | Apply documented local overrides and respect source suppressions |
| Output | Console, versioned JSON, and SARIF 2.1.0 |
| Baselines | Compare a full current scan against a compatible earlier full scan |
| CI gate | Distinguish policy failures from incomplete or failed analysis |
| Dependency audit | Optional NuGet vulnerability audit, clearly separated from static findings |
| Reliability | Explicit project and stage completeness, cancellation, and useful failure messages |
| Packaging | Installable local .NET tool package, tested outside the source checkout |
| Evaluation | Seeded defects, legitimate lookalikes, and a small manually reviewed real-repository sample |
| Community process | Profile schema, contribution template, public evaluation criteria, and a documented default-selection process |

### 3.2 Initial compatibility boundary

- Implement the CLI in C#, targeting `net10.0` unless an existing repository requirement prevents that choice.
- Validate SDK-style C# projects targeting `net8.0` and `net10.0`.
- Support ASP.NET Core server projects, ordinary supporting class libraries, and C# test projects in the same solution.
- Support `.csproj`, `.sln`, and `.slnx` targets using compatible platform libraries.
- Support single-target projects in MVP. Detect `TargetFrameworks` with more than one target and report the project as unsupported/incomplete. Do not analyze the first framework and claim coverage of all frameworks.
- Validate on Linux and Windows. Other platforms are unverified until tested.
- Respect `global.json`. Do not silently select a different SDK when the repository's requested SDK is unavailable or incompatible with the scanner host.
- Exclude legacy non-SDK projects, .NET Framework, F#, VB, Unity, MAUI, WPF/WinForms, and Razor/Blazor source analysis from the advertised support boundary.
- A server project that happens to load successfully does not establish support for every SDK feature. Record and test the actual compatibility matrix.

The CLI runtime, selected build SDK, target framework, and Roslyn assembly versions are different things. Record them separately.

### 3.3 Explicitly deferred

- A large custom rule library or broad EF Core query analyzer.
- Whole-program dead-code detection, unused public API deletion, or unused package deletion.
- Automatic architecture inference or mandatory controller/service/repository layering.
- Runtime tracing, profiling, load testing, or application startup.
- Automatic fixes, `fix all`, IDE extensions, or agent-specific installers.
- A numeric health score. Start with counts, findings, completeness, and regression status.
- A website, accounts, profile registry, voting backend, marketplace, analytics, or telemetry.
- Downloading arbitrary analyzers or executing scripts named by a profile.
- Recursive profile inheritance, multiple parent profiles, or remote profile resolution.
- Automated checkout of baseline commits, affected-project optimization, file-level caches, or parallel analysis infrastructure.

The earlier discussion mentioned roughly 20–40 selected diagnostics. For this implementation, start with the 12 candidates in section 8. Add more only when evaluation justifies them. A diagnostic count is not a release target.

## 4. Keep the architecture small

Start with three production projects and two test projects. Add a separate project only for a demonstrated packaging or runtime boundary.

| Path | Responsibility |
| --- | --- |
| `src/RepoDoctor.Cli/` | Commands, argument validation, cancellation, output selection, and exit codes |
| `src/RepoDoctor.Core/` | Profile models/resolution, normalized reports, comparison, gate evaluation, and report writers |
| `src/RepoDoctor.Analysis/` | MSBuild/Roslyn loading, bundled analyzers, project inventory, and NuGet adapter |
| `tests/RepoDoctor.Core.Tests/` | Policy, normalization, comparison, and report contract tests |
| `tests/RepoDoctor.IntegrationTests/` | Real project loading, packaged tool execution, and CLI behavior |
| `tests/Fixtures/` | Small supported projects, seeded mistakes, suppressions, and failure cases |
| `profiles/` | Versioned JSON profiles shipped with the tool |
| `schemas/` | Profile, repository configuration, and report schemas |
| `docs/` | Usage, rule guidance, compatibility, and profile contribution documentation |
| `evaluations/` | Small public fixture manifests and reviewed profile results |

`Core` must not depend on Roslyn. `Analysis` converts Roslyn and NuGet output into core models. `Cli` orchestrates both. Keep comparison and gate evaluation pure so they can be tested without loading a solution.

Use straightforward services with explicit inputs. Interfaces are appropriate at real boundaries such as subprocess execution or filesystem access used in integration tests. Do not introduce interfaces for every record or one-line calculation.

Prefer SDK/platform libraries and a small, maintained CLI parsing dependency. Use `System.Text.Json`. Pin test and analysis dependencies. Do not add a logging stack or terminal UI framework unless ordinary stderr output cannot meet an actual requirement.

## 5. CLI contract

The intended installed executable is `repo-doctor`. The examples assume it is available on `PATH`. Also document invocation through a pinned local .NET tool manifest after verifying the packaged tool's behavior.

```bash
repo-doctor init .
repo-doctor scan ./Application.slnx
repo-doctor scan ./Application.slnx --format json --output ./artifacts/current.json
repo-doctor scan ./Application.slnx --baseline ./artifacts/base.json --format json --output ./artifacts/current.json
repo-doctor scan ./Application.slnx --format sarif --output ./artifacts/current.sarif
repo-doctor scan ./Application.slnx --audit --format json --output ./artifacts/audited.json
repo-doctor profile validate ./profiles/team-api.json
repo-doctor profile export . --output ./profiles/team-api.json
repo-doctor config explain .
repo-doctor rule explain MA0040
```

### 5.1 Command responsibilities

| Command | Contract |
| --- | --- |
| `init <directory>` | Copy the release's exact recommended profile into `.repo-doctor/profile.json` and create `.repo-doctor.json`; refuse to overwrite either file |
| `scan <target>` | Run the full selected scope; default output is console |
| `profile validate <file>` | Validate JSON shape, supported IDs, values, version compatibility, and file limits without loading MSBuild |
| `profile export <directory>` | Export the selected profile with repository JSON rule/gate overrides flattened; retain attribution; omit repository-specific exclusions and suppressions |
| `config explain <directory>` | Show selected profile identity/hash, local overrides, exclusions, gate, analyzer bundle identity, and the documented role of `.editorconfig` |
| `rule explain <rule-id>` | Explain the shipped diagnostic, source package, known limitations, relevant exceptions, and upstream documentation |

For `scan`, implement `--config <path>`, `--baseline <path>`, `--format console|json|sarif`, `--output <path>`, `--audit`, and `--timeout-seconds <positive-integer>`. Default timeout is 300 seconds. It is an operational limit, not a performance promise.

`--config` selects a repository configuration file. Paths inside it remain relative to that file's directory. Do not make them relative to the terminal's current working directory.

All supported commands expose `--help`; expose a tool version command. Invalid options fail before project loading.

### 5.2 Output behavior

- JSON stdout contains exactly one JSON document. Progress and ordinary diagnostics about the tool go to stderr.
- If `--output` is supplied, write the selected format there and keep stdout free of an alternative human report.
- Write a valid failure report when report format/path are known, even if loading or analysis fails.
- Write output atomically through a temporary sibling file. Never leave a truncated report that looks successful.
- Refuse an output path that would overwrite the supplied baseline or selected config/profile file.
- No prompts in scan or CI paths. Ambiguous discovery returns candidates and a usage error.
- Keep file names and locations out of terminal-only decorations that break copy/paste.

## 6. Shareable configuration is a first-class feature

### 6.1 Profile files contain policy data

A profile is ordinary JSON. Users can put it in Git, copy it between repositories, review its diff, and fork it without asking the tool's author.

MVP distribution is a checked-in local file. Someone may obtain that file through their own Git workflow. The scanner never fetches profiles during a scan.

A profile may define identity, attribution, intended context, selected diagnostics, diagnostic reporting severity, diagnostic scope, and a gate. It cannot define executable code, shell commands, analyzer package URLs, custom assembly paths, or dynamic expressions.

The following is a valid small example, not the complete shipped seed:

```json
{
  "schemaVersion": 1,
  "id": "community/server-core",
  "version": "1.0.0",
  "title": "Server core",
  "description": "A small correctness-focused profile for C# server repositories.",
  "targetContext": "ASP.NET Core services with supporting libraries and tests",
  "authors": ["Community contributors"],
  "derivedFrom": [],
  "engineMajor": 0,
  "rules": {
    "MA0040": { "severity": "warning", "scope": "all" },
    "MA0042": { "severity": "warning", "scope": "all" },
    "MA0100": { "severity": "error", "scope": "all" },
    "MA0134": { "severity": "warning", "scope": "all" },
    "CA2012": { "severity": "error", "scope": "all" },
    "CA2200": { "severity": "warning", "scope": "all" }
  },
  "gate": {
    "minimumSeverity": "warning",
    "minimumConfidence": "high"
  }
}
```

The shipped initial profile uses an honest identity such as `bootstrap/server-core`, version `0.1.0`. Do not present a maintainer's first configuration as community consensus. `community/server-core` above illustrates a possible later profile.

### 6.2 Repository configuration

```json
{
  "schemaVersion": 1,
  "profile": "./.repo-doctor/profile.json",
  "rules": {
    "MA0040": { "severity": "none" },
    "CA2200": { "severity": "error" }
  },
  "exclude": ["**/Migrations/**"],
  "gate": {
    "minimumSeverity": "error",
    "minimumConfidence": "high"
  }
}
```

Repository overrides may enable any diagnostic registered by the fixed engine bundle, including a diagnostic absent from the selected profile. An explicitly enabled rule needs both severity and scope if the profile provides neither.

`exclude` controls reported source locations, not compilation inputs. Excluded files can still be necessary for semantic analysis. Display exclusions in the effective configuration and record them in report policy metadata.

Use a tested glob matcher with documented semantics. Normalize separators to `/`. Do not accidentally introduce different glob case rules on Linux and Windows; specify case-sensitive matching on normalized repository-relative paths.

### 6.3 Version 1 schema rules

- Unknown JSON properties are errors, except data inside a documented future extension mechanism. Do not implement an extension mechanism in MVP.
- Reject duplicate JSON property names before deserializing. In particular, duplicate rule keys cannot silently choose a winner.
- Reject unknown diagnostic IDs and unsupported schema/engine major versions.
- Profile rule severities are `none`, `info`, `warning`, and `error`.
- Scope is `all` or `production`. Tests are identified by evaluated project metadata, with documented detection fallbacks. Filename guesses are not sufficient to classify an entire project.
- An omitted rule is disabled. An omitted scope on a complete profile rule is invalid.
- Repository override objects inherit unspecified fields from the selected profile rule.
- Gate minimum severity is `info`, `warning`, or `error`.
- Confidence values are `unknown`, `medium`, and `high`, ordered in that sequence.
- Limit each profile/config file to 256 KiB, JSON depth to 16, and rule entries to 2,000. Reject excessive input with a clear message.
- Require semantic version strings for published profile versions. Treat every published `(id, version)` as immutable by convention, but never trust that pair without its content hash.
- A new unpublished local profile may be edited normally. Its hash changes, and baseline compatibility rules apply.
- `derivedFrom` is an array of `{ "id", "version", "sha256" }` objects. It records attribution only and does not load parent profiles.
- Require selected profile files to resolve inside the repository root after canonicalization, including symlinks. Vendor a shared profile into the repository before use.

### 6.4 Configuration precedence

Keep rule selection separate from analyzer configuration and source suppression:

1. The bundled catalog determines which diagnostics exist.
2. The selected profile determines the initial enabled set and reporting policy.
3. `.repo-doctor.json` overrides profile fields.
4. Explicit repository per-rule/per-file `.editorconfig` severity settings take precedence over profile severity for an already selected rule. They do not activate a rule absent from the resolved profile/JSON selection.
5. Standard source suppressions and `NoWarn` exclusions remain effective. A scanner profile must not resurrect a deliberately suppressed diagnostic.
6. Repository source exclusions and rule scopes filter report locations, while preserving the compilation context.

The MVP profile schema does not share arbitrary analyzer behavior options. Preserve existing repository analyzer options through Roslyn. Sharing typed behavior options is a later extension after an allowlisted mapping is designed.

For the scanner's selected diagnostic set, profile severity is the fallback when the repository has no explicit per-rule setting. The SDK's default disabled state or broad `AnalysisMode` choice must not make a supposedly enabled profile silently ineffective. Conversely, explicit source suppression must still work.

Implement and test this behavior with the supported Roslyn configuration APIs. Merely changing `CompilationOptions.SpecificDiagnosticOptions` or rewriting severity after analysis may be insufficient: tree-specific configuration and disabled-by-default diagnostics must work too. Preserve the project's `AnalyzerConfigOptionsProvider`, additional files, parse options, and relevant compiler options. If an overlay is required, keep it in memory and test it.

`TreatWarningsAsErrors` is a build policy. Do not silently turn all scanner warnings into errors because it is enabled. Compiler errors are tracked separately from scanner findings; compiler warnings promoted by build policy are not automatically evidence of an invalid semantic model.

Document any unsupported configuration semantics rather than claiming exact parity with `dotnet build`. The [official analyzer configuration documentation](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/configuration-options) describes several interacting sources of settings; the acceptance fixtures below define this tool's narrower contract.

### 6.5 No configuration required for exploration

Without `.repo-doctor.json`, use the profile embedded in that exact tool release and print its identity and hash. Do not contact a service to resolve a moving `recommended` alias.

`init` copies that exact profile into the repository. A local .NET tool manifest pins the tool version. Together, the vendored policy, repository configuration, and pinned tool make the adopted behavior reviewable.

`config explain` resolves JSON policy without MSBuild. It must say that final per-file `.editorconfig` and source suppression effects are resolved during scanning. A scan report records effective per-project policy digests and lists selected rules that produced no diagnostics separately from rules that were disabled or skipped.

## 7. Community ownership and selection of defaults

### 7.1 The product promise

Anyone can publish a compatible profile. It does not need approval to work with the scanner. Inclusion in the project's recommended distribution has a documented evaluation process.

The engine must not contain a hidden preference that overrides a community profile merely because its choices differ from the author's preferences. The immutable parts of the engine are operational truth: what ran, what failed, what evidence was found, and which settings were applied.

A community profile can disable a rule or weaken a gate. The report must make that visible. A profile cannot relabel an incomplete scan as complete, alter upstream diagnostic IDs, fabricate confidence, or conceal the selected policy identity.

Confidence assessments and their evidence are public catalog data and can also be challenged through the contribution process. Keeping a profile from inventing evidence is not permission to make the catalog author's judgments unreviewable. Until a confidence dispute is resolved, a repository can change its selected rules or minimum confidence transparently.

### 7.2 What “best” means

There is no useful universal winner across web APIs, libraries, desktop applications, and games. Initially evaluate profiles for the supported server-repository context. Later, use separate recommended profiles for other contexts.

Do not rank profiles by resulting repository health score, number of enabled rules, number of downloads, GitHub stars, or fewest warnings. A configuration that disables everything would win several of those competitions.

Evaluate candidates using:

| Evidence | Why it matters |
| --- | --- |
| Detection of known seeded defects | Measures whether the profile retains useful coverage |
| Legitimate lookalikes left alone | Measures false-positive behavior on known cases |
| Reviewed findings on real repositories | Tests whether the synthetic examples transfer to ordinary code |
| Breakdown by rule, category, and repository | Prevents an aggregate from hiding a serious weakness |
| Runtime and completion rate | A profile that routinely times out is not useful |
| Practical remediation quality | A finding should lead to a correct change or a justified exemption |
| Differences from the existing recommendation | Shows exactly what users would gain and lose |

Unknown real-world findings remain `unreviewed`; do not count them as false positives or confirmed defects. Report sample sizes. Any recall claim applies only to the explicitly seeded defects, not all bugs in a real repository. Do not invent benchmark percentages.

### 7.3 Minimal governance deliverables

Create `docs/PROFILE_CONTRIBUTING.md`, a profile submission template, and a small checked-in evaluation manifest. A submission contains:

1. Profile JSON, license, version, authors, and attribution for the profile it derives from.
2. Intended repository context and rationale for meaningful differences.
3. Reproducible evaluation commands, source revisions, tool version, and profile hash.
4. Reviewed finding labels and known false-positive examples.
5. Rule coverage lost or gained, including disabled important checks.
6. Runtime and scan-completeness results.

For MVP, maintainers publish the recommendation decision in an ordinary reviewable document or pull request. Community feedback and alternative proposals must be visible. Record why a candidate was accepted or rejected and permit an appeal with new evidence. Do not pretend a bootstrap project already has representative community governance.

Promotion changes a versioned recommendation entry in a future tool release. It never edits already-vendored profiles. Users adopt a new recommendation through an explicit dependency/configuration update and inspect the diff.

Voting, if introduced later, is advisory and needs abuse resistance. It must not cause a runtime service to replace a repository's pinned policy. The MVP requires no voting infrastructure or online registry.

### 7.4 Policy changes invalidate simple regression comparisons

Changing a profile may reveal old defects or hide previous findings. Therefore ordinary baseline comparison requires identical effective policy and analyzer bundle identities.

To evaluate a new profile, rescan the baseline revision and the head revision with the same new profile and tool. Separately review the policy change itself. Never announce “12 fixes” when the only change was disabling 12 diagnostics.

## 8. Initial diagnostic bundle

Use pinned releases of `Microsoft.CodeAnalysis.NetAnalyzers` and `Meziantou.Analyzer`, subject to the compatibility proof in milestone 0. Load their analyzer assemblies from the installed tool's own distribution. Do not modify the scanned repository's package references to add them.

Roslynator and other analyzer families are later candidates, not required dependencies for MVP.

Register a small allowlist of supported diagnostic IDs. Do not promise that a profile can enable every diagnostic contained in the upstream DLLs. A newly supported ID requires catalog metadata and evaluation fixtures; it does not require writing a new analyzer.

### 8.1 Seed candidates

Verify availability and exact behavior against the pinned analyzer packages. The table gives selection intent; upstream implementations define the actual detector behavior.

| ID | Selection intent | Initial profile severity |
| --- | --- | --- |
| `MA0040` | An available cancellation token is not propagated | warning |
| `MA0042` | Blocking operation in an async method | warning |
| `MA0100` | Resource lifetime ends before associated async work completes | error |
| `MA0134` | Async result is not observed in supported contexts | warning |
| `MA0022` | A task-returning method returns null | error |
| `MA0033` | Thread-static attribute used on an instance field | error |
| `MA0054` | Rewrapped exception loses its original exception chain | warning |
| `MA0009` | Regex evaluation lacks a timeout in the rule's supported cases | warning |
| `CA2000` | An owned disposable may be left undisposed | warning |
| `CA2012` | Incorrect consumption of a ValueTask | error |
| `CA2200` | Rethrow loses useful stack information | warning |
| `CA5359` | Supported certificate-validation bypass pattern | error |

Upstream references: [Meziantou diagnostic catalog](https://github.com/meziantou/Meziantou.Analyzer), [Microsoft diagnostic catalog](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/quality-rules/), and [CA5359's specific scope](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/quality-rules/ca5359). Do not describe CA5359 as comprehensive TLS security analysis.

For each supported ID, store source package/version, upstream category, normalized category, short guidance, known limitations, help URL, available upstream fixer metadata, and qualitative confidence. Confidence is an engine/candidate-evaluation assessment of evidence strength, not a probability and not a community popularity score. Start unevaluated diagnostics at `unknown`; only raise confidence after relevant positive and negative fixtures and documented review. Findings without a high-confidence classification remain visible even if the bootstrap gate does not block on them.

All seed rules initially have scope `all`. If review shows test-specific noise, change the versioned profile explicitly or improve the supported option mapping later. Do not secretly ignore all tests in the engine.

### 8.2 Rule additions are deliberately expensive enough to evaluate

Each rule needs a positive fixture, a legitimate negative example, relevant suppression coverage, and a documented remediation. Include API lookalikes where name matching would be wrong.

If a candidate is absent or unusable in the compatible bundle, remove it from the seed and explain why. Do not create a custom approximation merely to preserve the list length.

Do not initially enable naming rules, formatting preferences, universal `ConfigureAwait(false)` advice, mandatory repository classes, or a requirement for more interfaces. These are either context-sensitive or policy preferences that need their own justified profile.

### 8.3 Avoid duplicate engines and duplicate findings

The scan executes the bundled analyzer instances required by selected IDs. Do not also execute the scanned repository's versions of those analyzers. Repository source generators may still be required to obtain the correct compilation; this is a different responsibility.

Some analyzer classes expose several diagnostic IDs. Disable unselected IDs through analysis configuration and filter final output as a second guard. Ensure selected disabled-by-default diagnostics really run.

Within a project/target, remove only exact duplicate emissions from the same diagnostic source and source range. Do not collapse different locations merely because the messages match. If two different rules overlap, prefer resolving that overlap in the profile rather than guessing that all their findings are equivalent.

Do not use `dotnet format` as the finding collector. Its verification behavior is centered on changes that formatting/code-fix operations would perform, and cannot serve as the complete report contract. See the [command documentation](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-format).

## 9. Project loading and execution

### 9.1 Discovery

An explicit file path wins. For a directory, look for solution files in that directory, then a single project in that directory if no solution exists. Do not recursively guess among many unrelated projects. Return the candidates if the target is ambiguous.

Set repository root to the directory of the selected repository config, otherwise the enclosing Git worktree root if available, otherwise the target directory. Do not search for configuration above the enclosing Git root. For an explicit config, validate that the target belongs to its root.

Use repository-relative canonical project paths as identity. Do not use absolute checkout paths, randomized Roslyn IDs, or display names as stable keys. Discover transitive project references required for compilation; record which projects are scan subjects versus semantic dependencies.

### 9.2 Load the actual build context

- Register the intended MSBuild instance before touching conflicting MSBuild assemblies.
- Use `MSBuildWorkspace` and a compatible `Microsoft.Build.Locator` integration.
- Load solutions through the supported solution APIs; do not write a custom `.sln` or `.slnx` parser.
- Use evaluated project values for SDK type, target framework, `IsTestProject`, references, and conditional settings. Reading raw XML alone does not resolve imported `Directory.Build.props` or conditions.
- Preserve language version, nullable context, preprocessor symbols, generated global usings, project references, and source-generated types needed for binding.
- Subscribe to workspace, analyzer-load, and generator diagnostics. Classify warnings deliberately instead of swallowing them or marking every harmless warning fatal.
- Avoid running target-repository diagnostic analyzers merely because they are referenced. Execute the bundled selected diagnostic analyzers; preserve generator execution needed for supported compilation.
- Do not start the application or test suite during `scan`.

Microsoft documents relevant APIs through [MSBuildLocator](https://learn.microsoft.com/en-us/dotnet/api/microsoft.build.locator.msbuildlocator) and [CompilationWithAnalyzers](https://learn.microsoft.com/en-us/dotnet/api/microsoft.codeanalysis.diagnostics.compilationwithanalyzers). SDK/Roslyn incompatibility is a real integration risk; a reported example concerns [generator loading with newer SDKs](https://github.com/dotnet/roslyn/issues/84137). Test the chosen combination instead of assuming matching major numbers are sufficient.

### 9.3 Restore and execution boundary

MVP requires the target to be restored before scanning. Do not perform an implicit restore or download missing SDKs. If assets are missing, report the required restore command and return an incomplete scan.

A source scan must not modify tracked source, project files, or policy. Build evaluation and generators can still execute code and write generated/intermediate files. Do not market semantic loading as a sandbox or as safe execution of an arbitrary public repository.

Run trusted local repositories or isolated CI jobs. Never run a fork's build logic with privileged secrets. Shared JSON profiles themselves are data-only, but that does not make MSBuild evaluation harmless.

### 9.4 Generated code and excluded code

Keep generated code available to compilation. Exclude generated source locations from ordinary findings, honoring Roslyn/project conventions and `generated_code` configuration. Preserve compiler/generator failures as engine problems even when they refer to generated files.

Do not turn `bin`, `obj`, migration, or test source exclusions into missing compilation inputs. A file can be outside the reporting scope and still affect the behavior of included source.

### 9.5 Fail honestly

Missing SDK, restore assets, required reference, generator output, unsupported project, analyzer crash, timeout, and cancellation are analysis-state events. They are not clean scans and cannot be suppressed through a profile rule.

Continue supported independent projects when practical, but mark aggregate completeness as partial. A dependency load failure also affects dependent projects' semantic validity. Do not publish high-confidence semantic findings for a project with unresolved essential compilation state.

Use one scanner process per invocation and sequential project analysis initially. Cancellation should propagate to Roslyn and subprocesses. Terminate an audit subprocess on timeout. In-process analyzer execution is not a hard isolation boundary; document that limit and rely on an outer CI job timeout for an uncooperative analyzer. Do not claim that cancelling a task kills arbitrary analyzer code.

## 10. Diagnostic and report contracts

Version the JSON schema from the first release. Separate analysis status, findings, and gate result. Zero findings is not evidence that all selected checks ran.

### 10.1 Top-level report fields

| Field | Required content |
| --- | --- |
| `schemaVersion` | Integer, initially 1 |
| `tool` | Tool version, analyzer package versions, Roslyn version, and bundle hash |
| `policy` | Profile ID/version/hash, canonical effective policy hash, selected rules, gate, and exclusions |
| `environment` | Selected SDK, CLI runtime, OS, and supported compatibility identification |
| `scope` | Repository-relative target path, subject projects, semantic dependency projects, target frameworks, configuration, and file counts |
| `provenance` | Git commit and dirty state when available; nullable when no Git metadata exists |
| `analysis` | Overall completeness, stages, per-project status, attempted/completed rule counts, and problems |
| `findings` | Stable, deterministically ordered normalized findings |
| `comparison` | Baseline status, new/existing/fixed records or counts, and incompatibility reasons |
| `gate` | Passed/failed/not-evaluated status and exact blocking findings |
| `timing` | Start timestamp and elapsed durations; excluded from deterministic comparisons |

Use `analysis.completeness` values `complete`, `partial`, and `failed`. Use stage statuses `complete`, `disabled`, `unsupported`, `failed`, and `cancelled`. A deliberately disabled optional audit does not make static analysis incomplete. A requested audit that fails does.

### 10.2 Normalized finding fields

- `id`: stable report-local identifier derived from the matching fingerprint and occurrence index.
- `ruleId` and `source`: original upstream diagnostic ID and source package, or the defined audit rule family.
- `category`: a normalized product category.
- `severity`: the effective reporting severity.
- `confidence`: `unknown`, `medium`, or `high` with a catalog rationale reference.
- `message`: the diagnostic explanation.
- `projectPath`, `targetFramework`, and `configuration`.
- `location`: repository-relative file path and 1-based start/end coordinates, or null for a finding without a source location.
- `relatedLocations`: optional supporting source locations.
- `symbolId`: stable containing-symbol identity when available.
- `evidenceHash`: hash of normalized diagnosed source evidence, when available.
- `fingerprint`: versioned matching key.
- `helpUrl` and `remediation`: actionable rule guidance.
- `fixAvailability`: `upstream-fixer`, `manual`, or `unknown`; this does not mean the CLI applies it.
- `baselineState`: `uncompared`, `new`, or `existing`.
- `policySource`: profile/repository/explicit analyzer-config origin where determinable.
- `dependency`: nullable structured audit evidence containing package ID, resolved version, advisory identity/URL, upstream advisory severity, and direct/transitive relationship when known.

Source end coordinates are exclusive, matching the Roslyn span convention after conversion to 1-based coordinates. Keep upstream advisory severity separate from the profile's reporting severity.

Do not fabricate a source line when none exists. Audit findings may refer to a project and resolved package instead of a literal line in `Directory.Packages.props`.

Default reports contain locations, diagnostic messages, and evidence hashes, not full source snippets. Messages can themselves contain identifiers or source-derived text, so treat reports as repository data. Do not upload them automatically or claim they contain no sensitive information.

Sort findings by normalized project path, target framework, file path, source range, rule ID, and fingerprint. Use stable English/invariant diagnostic messages for matching; localized presentation can be added later.

### 10.3 Console and SARIF

The console report starts with analysis completeness, gate status, selected profile/version, and the number of subject projects successfully analyzed. Group findings by severity with copyable file locations. Print both new and existing counts when comparing.

For SARIF 2.1.0, emit tool metadata, rule descriptors, levels, locations where present, help links, partial fingerprints, baseline state, and execution notifications. Represent execution failure through invocation status/notifications. Include effective policy identity in properties. A SARIF report with no results and a failed invocation must not be presented as a successful clean scan.

Validate at least one representative SARIF artifact against the official schema. JSON is the scanner's baseline format; SARIF is an interchange format, not a second baseline implementation.

## 11. Baseline comparison

### 11.1 Baseline format and workflow

A baseline is an earlier complete JSON scan report. Do not invent another baseline file format or a database.

```bash
repo-doctor scan ./Application.slnx --format json --output ./artifacts/base.json
repo-doctor scan ./Application.slnx --baseline ./artifacts/base.json --format json --output ./artifacts/head.json
```

The caller creates the first report from the actual baseline revision. For local work this can be a scan before edits. For PR workflows it should come from the configured target revision or merge base. Do not silently scan the current checkout twice and label the first scan “main.”

The scanner does not modify Git checkouts. Document a CI workflow that restores and scans the base and head separately using the same pinned scanner and policy, transfers the baseline artifact, then runs the head comparison.

### 11.2 Compatibility requirements

Require compatible report/fingerprint schema, exact tool/analyzer bundle identity, effective policy, semantic analysis options, normalized subject scope, supported SDK environment, and per-existing-project analyzer configuration. Ignore absolute checkout directory, timestamps, and durations.

For v1, require the same selected SDK version and OS family for a baseline/head pair; cross-platform baseline reuse is deferred. Source-file counts are informational and must not require equality, because adding a source file is an ordinary code change. Hash resolved configuration content and relevant evaluated analysis settings, not a list of every source path or the entire source tree.

A different target path, changed project membership, changed target framework, changed conditional compilation settings, or changed effective rule policy is a baseline incompatibility in MVP. This conservative behavior is preferable to falsely calling removed coverage a fix. Explain how to establish a new common scope and regenerate the baseline from the base revision. Automatic reconciliation of added/removed projects is deferred.

Ordinary source edits and ordinary application dependency updates are allowed between baseline and head. Record their provenance, but do not require identical source-tree or package-lock hashes. Source generators can change with application dependencies; failures still make the scan incomplete. Analyzer bundle changes are policy/tool changes and require a fresh base scan.

When a source-level `.editorconfig` changes effective analysis policy, return incompatibility rather than silently counting suppressed findings as fixed. Record the policy/config digests required to detect that change without hashing unrelated source files.

If compatibility fails, retain current findings, mark comparison invalid, do not produce fixed/new claims, and return exit code 2. A missing or partial requested baseline is also an error. Never silently fall back to a whole-repository gate after the caller asked for a regression gate.

### 11.3 Matching algorithm v1

For source diagnostics, build a key using source package family, rule ID, normalized project identity, target framework, configuration, stable containing-symbol identity where available, normalized diagnosed syntax tokens, and stable diagnostic message/arguments.

Normalization removes formatting trivia while preserving identifiers, literal values, and relevant operators. Hash structured components with explicit field boundaries. Do not concatenate ambiguous strings or normalize away semantically different literals.

Use the smallest syntax node that covers a nonempty diagnostic span as the evidence source, with a documented fallback for token-only or zero-width locations. Do not fingerprint the entire containing method when the diagnostic identifies one invocation: an unrelated edit elsewhere in the method should not change that finding's identity.

Compare a multiset of findings, not a set. If one existing problem is copied into a second location, one occurrence remains existing and the extra occurrence is new.

Prefer a same-file evidence match. Then allow a unique same-project cross-file match with equal symbol/evidence identity. Do not use line number as identity. Moving an unchanged member into another file in the same project should not automatically create a regression.

For ambiguous matches or missing evidence, use a conservative same-file key that includes available message and symbol information. If identity still cannot be established, treat the head occurrence as new and explain the limitation. Do not implement fuzzy similarity scoring in MVP.

If a containing symbol changes identity, reporting a new finding is acceptable in v1. Document that refactorings can require review even when the underlying issue is similar.

### 11.4 The scanner does not infer fixes from absence alone

`fixed` means a baseline occurrence is absent after a complete compatible current analysis. It is not proof of correct runtime behavior.

Suppressions in source can remove a finding. When a suppression change is detectable, label that removal as suppressed/policy-related rather than a confirmed code fix. At minimum, avoid prose that asserts every absent finding was repaired. Report the count as `noLongerReportedCount`; use `fixed` only as the conventional baseline state with this documented meaning.

## 12. Gate behavior and exit codes

Evaluate the gate only when all requested stages and the requested comparison are complete and valid.

| Exit code | Meaning |
| --- | --- |
| 0 | Requested analysis completed and the applicable gate passed |
| 1 | Requested analysis completed and policy-blocking findings exist |
| 2 | Invalid configuration/arguments, unsupported or incomplete analysis, invalid baseline, analyzer/audit failure, or report-write failure |
| 130 | User interruption when the process can report cancellation cleanly |

Error precedence is interruption, then incomplete/invalid operation, then policy failure, then success. A partial scan with serious findings returns 2 and includes those findings. Do not hide failure behind code 1.

Without a baseline, consider all reported findings. With a valid baseline, consider only new findings. A finding blocks when both effective severity and confidence meet the configured minimums.

The bootstrap profile uses minimum severity `warning` and minimum confidence `high`. A zero-rule profile can complete, but the report must prominently state `enabledRuleCount: 0` and describe the result as an empty policy evaluation, never “healthy.”

Do not implement scores, estimated hours of technical debt, a leaderboard, or comparisons between scores from different profiles. If scoring is added later, it must be local, documented, profile-aware, and subordinate to findings and completeness.

## 13. Optional NuGet audit

`scan` does not contact vulnerability services by default. `--audit` explicitly requests dependency auditing in addition to static analysis. No profile may turn network requests on silently.

Reuse the SDK's machine-readable vulnerability audit capability and include transitive packages. For a validated .NET 10 SDK, the intended subprocess shape is:

```bash
dotnet package list --project ./src/Api/Api.csproj --vulnerable --include-transitive --format json --output-version 1 --no-restore
```

Verify this exact command in the chosen SDK. Use `ProcessStartInfo.ArgumentList` or an equivalent structured argument API; do not concatenate a shell command. Scan only selected subject projects. Do not accidentally audit an unrelated solution discovered from the working directory.

The [SDK command documentation](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-package-list) specifies version-dependent behavior, including automatic restore in newer SDKs and JSON output. This adapter deliberately requests no restore.

- Parse and validate output schema. Capture stdout and stderr separately.
- Inspect SDK warning/error records as well as the process exit code. A successful exit with a warning that vulnerability data could not be retrieved is an incomplete audit; an empty vulnerability list alone cannot establish success.
- Record audit timestamp, SDK version, configured source identity where safe, and any feed/cache provenance the SDK actually exposes.
- If feed freshness cannot be established, say so. Do not manufacture an advisory-database version.
- Map findings by project, framework, package ID, resolved version, advisory identity, and severity. Avoid duplicated occurrences of the same advisory/package/framework.
- Keep audit findings in a separate category and use a documented `NUGET-VULNERABILITY` rule family. The profile may set its reporting severity/scope; it runs only with `--audit`.
- The bootstrap profile includes this rule with error severity and scope `all`; it is a thirteenth catalog entry alongside the 12 source diagnostic candidates. The engine assigns high confidence in the existence of a successfully retrieved published advisory. That confidence does not claim exploitability in the application. When `--audit` is absent, record this rule's stage as deliberately disabled.
- A feed outage, parse failure, permission problem, or timeout is an incomplete requested audit, not “no vulnerabilities.”
- A newly published advisory can produce a new audit finding without a code change. Label audit comparison results as newly observed advisories; do not call them source-code regressions.
- Compare static and audit findings separately. Ordinary baseline comparison requires the same stage selection. An audited current scan needs an audited baseline if comparison was requested.
- When advisory data changes, absent audit findings are only no longer observed. Do not attribute the change to an upgrade without corresponding package evidence.

Use captured SDK-output fixtures for routine tests. Live-network audit checks are a separate integration task, not a requirement for every local test run.

## 14. Acceptance tests that protect real behavior

Use the repository's existing test framework; otherwise use xUnit. Use real Roslyn compilations/project fixtures for analysis behavior and pure tests for policy/comparison. Do not write tests that merely reproduce the implementation's formula or assert property getters.

### 14.1 Analyzer and loading matrix

| Scenario | Required result |
| --- | --- |
| Known selected violation | Correct diagnostic ID and meaningful source location |
| Legitimate lookalike API | No diagnostic caused merely by a matching method/property name |
| Corrected example | Original finding disappears |
| Selected disabled-by-default upstream rule | Runs when enabled by the profile |
| Rule disabled in profile | No reported finding for that rule |
| Repository JSON override | Overrides the profile as documented |
| Explicit per-file `.editorconfig` severity/suppression | Effective severity/suppression matches the precedence contract |
| Source suppression and `NoWarn` | Suppression is honored |
| Existing repository analyzer package | No duplicate execution of its bundled counterpart |
| Cross-project reference | Correct symbol binding across projects |
| Source-generated type | Correct binding when supported generator output is available |
| Missing SDK/assets/reference/generator | Explicit incomplete result and exit 2 |
| Multi-target project | Unsupported/incomplete, not a scan of an arbitrary first target |
| One failed project in a solution | Other valid results retained; aggregate incomplete |
| Excluded source file | Still available for compilation; ordinary findings not reported from it |
| Test project | Classified from evaluated metadata; scope `production` behaves correctly |

### 14.2 Baseline matrix

| Change | Required result |
| --- | --- |
| Identical input/policy | Zero new findings |
| Blank lines inserted before a violation | Existing finding |
| Formatting changes within diagnosed syntax | Existing finding when tokens are unchanged |
| Unchanged member moved to another file in the same project | Existing finding when identity matches uniquely |
| Existing violation copied | One additional new occurrence |
| One of two identical occurrences removed | One occurrence no longer reported |
| Relevant literal/operator changes | Do not erase the semantic change through normalization |
| Profile/tool/analyzer bundle changes | Incompatible baseline, exit 2 |
| Scope/project-membership/target-framework changes | Incompatible baseline, exit 2 |
| Ordinary implementation dependency update | Comparison allowed if remaining compatibility conditions hold |
| Partial or malformed baseline | Invalid comparison, exit 2 |
| Current analyzer failure | No claim that missing findings were fixed |
| Different absolute checkout directories | Compatible when normalized scope and environment match |

### 14.3 Profile/community matrix

- Two valid profiles over identical code produce the expected different selections and gates.
- A copied profile works without registry access or author approval.
- Unknown properties, duplicate keys, unknown rule IDs, oversized files, and executable fields are rejected.
- Identical profile ID/version with different contents produces a different hash and fails compatibility.
- `profile export` preserves effective JSON rule/gate choices, excludes local source exclusions, and records derivation.
- `init` never overwrites existing policy files.
- Updating the tool's recommended profile does not modify a previously vendored repository profile.
- A community profile can be selected without importing code or new analyzer DLLs.
- An all-disabled profile is reported transparently as an empty policy evaluation.

### 14.4 Reporting, audit, and packaging matrix

- JSON parses when the gate fails and when analysis fails.
- No progress text appears on JSON stdout.
- SARIF passes schema validation and includes execution failure state where appropriate.
- Audit parse fixtures cover direct/transitive duplicates, no advisories, multiple frameworks in SDK output, malformed JSON, and SDK-reported failure.
- Requested audit failure prevents a passing aggregate gate.
- Paths with spaces work; no shell interpolation is needed.
- Baseline/output path collision is rejected.
- Cancellation is recorded and returns the documented status where possible.
- Pack the tool, install it from the local package into an isolated tool path, and scan a fixture outside the source checkout.
- Verify that bundled analyzer DLLs, dependencies, profiles, schemas, and rule guidance are present in that installation.
- Before/after tracked-file checks confirm that `scan` did not edit the target's source or policy files.

### 14.5 Small real-world evaluation

Use at least one representative supported ASP.NET Core solution in addition to controlled fixtures. Use an owned/authorized repository or an explicitly selected public repository in an isolated environment. Pin the revision. Never put private repository source or findings into public evaluation artifacts without authorization.

For each sampled finding, record `confirmed-useful`, `false-positive`, `preference`, or `unreviewed`, a brief reason, rule ID, repository revision, and profile/tool identity. Report the number reviewed and the sampling method. Include an unchanged second run to demonstrate reproducibility after normalizing timing fields.

Do not claim public-production readiness merely because synthetic fixtures pass. Release notes should state the compatibility coverage and real-repository sample actually exercised.

## 15. Guidance for the agent consuming this tool

Write a short `docs/AGENT_WORKFLOW.md`; do not create an automatic agent installer in MVP.

The workflow must tell a coding agent to:

1. Read the repository's instructions and chosen policy.
2. Run a full scan or use a compatible pre-change baseline.
3. Check completeness before interpreting missing findings or a passing gate.
4. Inspect the cited code and rule explanation before editing.
5. Fix a small group of related, supported findings.
6. Run relevant existing tests, adding behavior tests when the fix changes meaningful behavior or exposes an untested bug.
7. Rescan and inspect new findings as well as findings no longer reported.
8. Report unresolved issues, uncertainty, and any justified policy discussion separately.

An agent must not improve the result by deleting tests, weakening the gate, adding broad exclusions, suppressing diagnostics without justification, or adopting a more permissive community profile behind the user's back.

Code changes and policy changes are separate review concerns. A valid disagreement with a rule should lead to a documented local exception or a proposed profile improvement, not a concealed alteration of what “passing” means.

An upstream fixer being available does not establish that applying it everywhere is safe. The MVP supplies guidance and findings only.

## 16. Implementation milestones

### Milestone 0: Prove the integration boundary

Inspect the repository and installed SDKs. Choose compatible stable SDK, Roslyn workspace, MSBuild locator, analyzer, CLI parser, and test package versions. Pin them in the appropriate repository files and record the supported matrix in `docs/COMPATIBILITY.md`.

Build a minimal spike that loads one restored SDK-style C# project, runs one bundled diagnostic without editing its project file, honors one explicit suppression, and emits a diagnostic with a file location.

Then pack the spike and prove analyzer discovery works from the installed tool directory. Analyzer references used during development do not automatically mean their assemblies are shipped as runtime assets. Check actual package contents and redistribution notices.

Exit criterion: a real externally installed executable reports the seeded finding and suppresses it correctly. If this fails, solve it before building profiles, rich output, or additional rules.

### Milestone 1: Models, catalog, and profile resolution

Create core report/policy/finding models, version 1 JSON schemas, the explicit diagnostic catalog, strict JSON validation, canonical hashing, and local profile resolution. Implement the small example profile and repository override fixture.

Define canonical JSON hashing once: recursively sort object keys, preserve array order, use invariant compact JSON, and hash UTF-8 bytes with SHA-256. Apply the same procedure to policy metadata. Arrays whose order has no meaning must be normalized deliberately before hashing. Record a hash-algorithm version.

Exit criterion: profile validation, override behavior, duplicate-key rejection, hashing, and unsupported-field tests pass without MSBuild.

### Milestone 2: A complete basic scan

Implement target/root discovery, evaluated project inventory, workspace loading, the selected analyzer execution path, failure collection, stable output ordering, and console/JSON reports. Start with the rules already proven by fixtures and expand to the seed candidates through tests.

Implement per-project completeness and partial-solution behavior now. Do not postpone failure reporting until after a happy-path demo.

Exit criterion: a supported solution scans, known findings appear, unsupported/loading failures return 2, and tracked source/policy files remain unchanged.

### Milestone 3: Effective analyzer configuration

Complete the precedence contract, including per-file `.editorconfig`, `NoWarn`, source suppressions, generated code, explicit profile enablement, scopes, and preservation of analyzer behavior options from the repository.

Exit criterion: the analyzer/configuration matrix in section 14 passes. Update `docs/CONFIGURATION.md` with behavior established by those tests. Do not claim settings are honored because their values were read into memory.

### Milestone 4: Baseline comparison and CI gate

Implement compatible-report validation, normalized evidence, stable symbol identities where available, multiset comparison, deterministic occurrence IDs, and the documented exit codes.

Exit criterion: changed line numbers do not create regressions; copied violations do; policy changes do not masquerade as fixes; partial analysis never passes.

### Milestone 5: Profile usability and community artifacts

Implement `init`, `profile validate`, `profile export`, `config explain`, and `rule explain`. Ship the versioned bootstrap profile and a second clearly labeled example profile that demonstrates an intentional policy difference.

Export creates a self-contained profile. Require the caller to edit the exported identity/version before publishing a derived profile; preserve original attribution in `derivedFrom` rather than impersonating the original author.

Write contribution/evaluation templates and the minimal default-promotion process. Put the release's recommendation in a versioned data manifest, not hidden C# selection logic. Initially the manifest contains one supported context and points to the bootstrap profile.

Exit criterion: a user can share a JSON file, adopt it in another repository, inspect the resulting differences, and keep it pinned across a tool update.

### Milestone 6: SARIF and NuGet adapter

Implement SARIF from the normalized report. Add the optional NuGet subprocess adapter with captured-output tests, transitive package handling, stage completeness, and separation of audit changes from code regressions.

Exit criterion: SARIF validates, JSON remains clean, and an unavailable audit source never produces a clean audited result.

### Milestone 7: Packaging, CI, and evaluation

Finish .NET tool packaging, local installation instructions, Linux/Windows CI checks, packaged executable tests, and a concrete baseline/head CI example. The example must pin its tool and policy, obtain the correct baseline revision, restore both revisions, preserve scanner exit status, and retain reports even after a gate failure.

Run the small real-repository evaluation and record actual findings, false positives, completion behavior, and timings. Adjust the seed through a documented versioned profile change if evidence requires it. Do not hide poor results by cherry-picking only fixtures that pass.

Exit criterion: every required MVP acceptance criterion has evidence, the tool package is usable outside the checkout, and limitations are documented without claiming deferred features exist.

### Milestone 8: Final implementation review

Review the change against this document. Remove unused abstractions and accidental scope additions. Verify no telemetry, remote profile resolution, source modifications during scans, automatic fixes, or numeric score slipped into MVP.

The final handoff contains what was implemented, how to install/run it, exact tests and evaluations completed, known unsupported cases, and a short list of justified next steps. Do not substitute a long future roadmap for incomplete MVP behavior.

## 17. Definition of done

- [ ] Installed tool scans supported `.csproj`, `.sln`, and `.slnx` inputs outside its own checkout.
- [ ] The declared Linux/Windows and SDK compatibility matrix has been exercised.
- [ ] Selected upstream analyzers are pinned, shipped, and executed without altering target package references.
- [ ] Profile selection and repository configuration affect actual execution as documented.
- [ ] Source suppressions and generated-code handling are tested.
- [ ] JSON and SARIF are valid for both successful and failed analysis.
- [ ] Scan completeness cannot be weakened through a profile.
- [ ] Baseline matching handles formatting shifts and duplicate occurrences.
- [ ] Incompatible baselines fail explicitly.
- [ ] Optional dependency audit reports network/tool failures accurately.
- [ ] Versioned local profiles can be shared and exported without an online service.
- [ ] Recommendation changes leave previously adopted profiles untouched.
- [ ] Community profile submission and recommendation criteria are documented and reviewable.
- [ ] Known diagnostic examples and legitimate exceptions are covered by meaningful tests.
- [ ] At least one representative supported repository has a documented, authorized evaluation.
- [ ] No tests, rules, or gates were weakened solely to make the implementation appear complete.
- [ ] Packaging includes necessary dependency licenses and notices.
- [ ] No code comments were added.
- [ ] `IMPLEMENTATION_STATUS.md` and usage documentation match the actual implementation.

## 18. Decisions that should remain open after MVP

Only revisit these after the basic tool has earned trust:

- Additional contexts and per-context community recommendations.
- Typed shareable analyzer behavior options.
- Profile comparison tooling and explicit migration reports.
- A public profile index with immutable content hashes and author verification.
- Independent reviewers and more formal recommendation governance as the community grows.
- Additional upstream analyzer families and carefully justified custom diagnostics.
- Multi-target analysis and reconciliation of changed project scopes.
- Hard process isolation for analyzer execution and a compiler-version worker strategy.
- Higher-quality architecture and EF Core analysis where evaluation demonstrates a gap.
- Optional runtime investigations.
- A documented local score, only if it adds information beyond findings and completeness.

The first release should make it easy for somebody to say “your default is wrong for my repository, here is a better configuration and the evidence,” and then actually use that configuration. It should not require building a social platform before the scanner works.
