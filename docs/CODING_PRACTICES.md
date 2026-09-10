# Coding practices for this repository

These are the standards `repo-doctor`'s own source follows. They combine the
implementation brief's rules (`dotnet-repo-doctor-mvp.md`) with the guardrail
approach from
[making-ai-write-high-quality-code-in-dotnet.md](references/making-ai-write-high-quality-code-in-dotnet.md):
**a rule the build enforces gets followed every time; a rule that only lives in
a document gets skipped once context gets busy.** So we push everything we can
into `Directory.Build.props`, `.editorconfig`, and `Directory.Packages.props`,
and keep this document for the judgement calls a config file cannot make.

## Enforced by the build (you cannot merge past these)

| Guardrail | Where | Effect |
| --- | --- | --- |
| `Nullable` + `ImplicitUsings` enable | `Directory.Build.props` | null-flow warnings on; fewer `using` lines |
| `AnalysisLevel=latest`, `AnalysisMode=Recommended` | `Directory.Build.props` | current Microsoft quality rules on |
| `EnforceCodeStyleInBuild=true` | `Directory.Build.props` | IDE style rules run in `dotnet build` and CI |
| `TreatWarningsAsErrors=true` + `CodeAnalysisTreatWarningsAsErrors=true` | `Directory.Build.props` | any analyzer/style warning fails the build |
| Central Package Management | `Directory.Packages.props` | every version in one file; an inline `PackageReference` version is a build error |
| SDK CA analyzers (`AnalysisMode=Recommended`) + `.editorconfig` | build | our self-linting gate. `Meziantou.Analyzer` is **bundled into the shipped tool** (its real job) but acquired via `PackageDownload`, so it does **not** lint this repo's own source |
| `CS8600–CS8604` = error | `.editorconfig` | nullable violations are hard errors |
| Async methods suffixed `Async` | `.editorconfig` (IDE1006 naming) | warning today, tightened to error as the codebase settles |
| `this.` qualification off | `.editorconfig` | consistent member access |

`AnalysisMode` starts at `Recommended` rather than the article's `All` so the
first milestones are not buried under hundreds of pre-existing style nits. Raise
it toward `All` per project as each area stabilises; record the change here.

**Exception for fixture projects.** Everything under `**/fixtures/**` and
`tests/Fixtures/**` deliberately contains seeded mistakes and must *not* inherit
this gate. Each fixture tree carries its own minimal `Directory.Build.props`
that stops the inheritance walk.

## Judgement rules the build cannot check

From the review skill in the reference article — apply these when writing or
reviewing code here:

1. **No `async void`.** Suffix async methods with `Async` and take a
   `CancellationToken` on anything that does I/O or analysis work. `scan` must
   propagate cancellation into Roslyn and subprocesses (brief §9.5).
2. **Return the most specific useful type.** `IReadOnlyList<T>` for materialised
   results, not `IEnumerable<T>`; findings and report collections are ordered and
   materialised.
3. **No boolean flag parameters.** Split the method or pass an enum/option record.
4. **No `Manager` / `Helper` / `Utils` class names.** Name the type after its one
   job (`ProfileResolver`, `BaselineComparer`, `SarifWriter`), or use an
   extension method next to the type it works on.
5. **No `#region`.** Split the class instead.
6. **No interface for a single implementation** unless a test needs a stub or it
   is a real boundary. The brief says the same (§4: "Do not introduce interfaces
   for every record or one-line calculation"). Keep interfaces for subprocess
   execution and filesystem access used in integration tests.
7. **Composition over inheritance.** Decorator over base-class chains.

## Rules specific to this project (from the brief)

- **No code comments, including XML doc comments** (brief §2). Explanations go in
  `docs/` Markdown and in clear names. This overrides any habit of documenting
  with `///`.
- **Never invent SDK or NuGet versions.** Resolve a compatible stable set, pin
  it, record it in `docs/COMPATIBILITY.md`. No `latest`, floating, or prerelease
  in delivered setup (brief §2, §16 M0).
- **`Core` must not depend on Roslyn.** `Analysis` converts Roslyn/NuGet output
  into `Core` models; keep comparison and gate evaluation pure and testable
  without loading a solution (brief §4).
- **`System.Text.Json` only** for JSON; no third-party serializer (brief §4).
- **No speculative extension points** — no plugin containers, event buses,
  databases, web services, LLM API dependencies (brief §2, §3.3).
- **Determinism:** report ordering and hashing are defined once and stable;
  timing fields are excluded from comparisons (brief §10).

## Review is a separate gate

Per the reference article, run code review in a **fresh session**, not the one
that wrote the change (`/code-review`, or the `code-review` /
`dotnet-clean-code-review` skills). The build catches analyzer-shaped problems;
the review catches naming, shape, and the judgement rules above. Human review is
last.
