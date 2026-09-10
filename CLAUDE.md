# repo-doctor

Local CLI that scans a C#/.NET repository, runs a pinned bundle of established
Roslyn analyzers, explains findings, and compares against an earlier scan.
Humans and coding agents get the same findings and policy decisions.

**Spec:** `dotnet-repo-doctor-mvp.md` is the authoritative product + engineering
brief. Work its section 16 milestones in order. Track progress in
`IMPLEMENTATION_STATUS.md`.

## Layout (brief §4 — keep it small)

- `src/RepoDoctor.Cli/` — commands, arg validation, cancellation, output selection, exit codes
- `src/RepoDoctor.Core/` — profiles, normalized reports, comparison, gate, report writers (**no Roslyn dependency**)
- `src/RepoDoctor.Analysis/` — MSBuild/Roslyn loading, bundled analyzers, project inventory, NuGet adapter
- `tests/RepoDoctor.Core.Tests/`, `tests/RepoDoctor.IntegrationTests/`, `tests/Fixtures/`
- `profiles/`, `schemas/`, `docs/`, `evaluations/`
- `spike/` — Milestone 0 integration-boundary proof (throwaway once `src/` exists)

## Conventions

- **Read `docs/CODING_PRACTICES.md` before writing code.** Standards are enforced
  by `Directory.Build.props` + `.editorconfig` + `Directory.Packages.props`
  (Central Package Management is on — no inline package versions).
- **No code comments, including `///` XML docs** (brief §2). Explain in `docs/`
  and with clear names.
- **Never invent SDK/NuGet versions.** Pinned set + rationale live in
  `docs/COMPATIBILITY.md`. No `latest`/floating/prerelease in delivered setup.
- `System.Text.Json` only. No LLM/network dependency. No speculative abstractions.
- Fixture projects under `**/fixtures/**` opt out of the strict build gate on
  purpose (seeded mistakes).

## Build / test

- SDK pinned by `global.json` (10.0.303, latestFeature roll-forward).
- `dotnet build` / `dotnet test` from repo root.
- Milestone 0 spike: `pwsh spike/run-spike.ps1` packs the spike as a tool,
  installs it to an isolated `--tool-path`, and runs it against a fixture.

## Review

Run `/code-review` (or the `dotnet-clean-code-review` skill) in a **fresh
session**, not the one that wrote the change.
