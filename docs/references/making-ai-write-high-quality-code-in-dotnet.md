> Archived reference. Source: <https://antondevtips.com/blog/how-to-make-ai-write-high-quality-code-in-dotnet>
> Author: antondevtips.com — published Sep 1, 2026. Retrieved 2026-09-07.
> Marketing/newsletter footer trimmed; technical content kept verbatim.
> This repo's adopted subset lives in [`../CODING_PRACTICES.md`](../CODING_PRACTICES.md).

# How to Make AI Write High-Quality Code in .NET

AI writes .NET code faster than anyone on your team. It can also write code that looks good but can fall apart during careful review.

Most developers try to fix this by putting the rules in the prompt. The rules work for a few files, maybe a few iterations in a session. But as the session goes on, the context gets loaded with information, and your rules start to be ignored.

So only two things actually work reliably:

- **Static compiler guardrails.** If bad code doesn't compile, the agent sees the error, fixes it, and you never review that mistake at all.
- **A skill for reviewing code.** It's used in another session, so it never competes with the original task.

Topics:

1. Set Project-Wide Standards with Directory.Build.props
2. Add Static Code Analysis Packages
3. Enforce Coding Standards with .editorconfig
4. Centralize Package Management
5. Code Reviews: AI First, Human Second
6. Why Coding Rules in Prompts Don't Work
7. Build a Clean Code Review Skill for Claude
8. Make Sure the Review Catches Every Issue

## 1. Set Project-Wide Standards with Directory.Build.props

Every .NET solution should start with a `Directory.Build.props` file. It defines project-wide settings that apply to all projects in the solution, created in the same directory as the `.sln` file.

```xml
<Project>
    <PropertyGroup>
        <Nullable>enable</Nullable>
        <ImplicitUsings>enable</ImplicitUsings>
        <AnalysisLevel>latest</AnalysisLevel>
        <AnalysisMode>All</AnalysisMode>
        <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
        <CodeAnalysisTreatWarningsAsErrors>true</CodeAnalysisTreatWarningsAsErrors>
        <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
    </PropertyGroup>
</Project>
```

What each setting does:

- **Nullable:** Enables nullable reference types. The compiler warns when you might use a null value incorrectly.
- **ImplicitUsings:** Adds common namespace imports to every file.
- **AnalysisLevel:** Sets code analysis to the latest version.
- **AnalysisMode:** Turns on all code analysis rules.
- **TreatWarningsAsErrors:** Stops compilation if there are any warnings.
- **CodeAnalysisTreatWarningsAsErrors:** Same strict treatment for code analysis warnings.
- **EnforceCodeStyleInBuild:** Runs code style checks during build, not just in the IDE.

An AI agent doesn't read your standards. It runs `dotnet build` and reads the output. `TreatWarningsAsErrors` turns every analyzer rule into a build error. The agent detects the error, fixes it, and rebuilds — before you ever open the diff. A rule the build enforces gets followed every time; a rule that only lives in a document gets skipped as soon as the agent has busy context.

## 2. Add Static Code Analysis Packages

Static code analyzers examine your code without running it. They catch common mistakes, enforce coding standards, and find potential bugs before they reach production — too long or complex methods, too many parameters, too much nesting, unused code.

```xml
<ItemGroup>
    <PackageReference Include="Meziantou.Analyzer" Version="2.0.257">
        <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
    <PackageReference Include="SonarAnalyzer.CSharp" Version="10.16.0.128591">
        <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
    <PackageReference Include="Roslynator.Analyzers" Version="4.14.1">
        <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
    <PackageReference Include="xunit.analyzers" Version="1.26.0">
        <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
</ItemGroup>
```

- **SonarAnalyzer.CSharp:** code quality, security holes, code smells, complex methods, duplicated code.
- **Meziantou.Analyzer:** performance issues, security holes, incorrect API usage; async/await, LINQ, string handling.
- **Roslynator.Analyzers:** analysis and refactoring suggestions for cleaner, more idiomatic C#.
- **xunit.analyzers:** correct test structure; catches missing assertions or wrong test attributes.

Aim for zero warnings. Warnings often point at real problems that will cause bugs later.

## 3. Enforce Coding Standards with .editorconfig

Analyzers find issues; `.editorconfig` decides which rules matter and how strict they are (error, warning, suggestion, off). Place it beside the `.sln` file so every developer and every AI agent follows the same rules regardless of IDE.

```ini
root = true

[*]
charset = utf-8
indent_style = space
indent_size = 4
insert_final_newline = true
trim_trailing_whitespace = true

[*.cs]
# Nullable reference types
dotnet_diagnostic.CS8600.severity = error
dotnet_diagnostic.CS8601.severity = error
dotnet_diagnostic.CS8602.severity = error
dotnet_diagnostic.CS8603.severity = error
dotnet_diagnostic.CS8604.severity = error

# Code style rules
dotnet_style_qualification_for_field = false:warning
dotnet_style_qualification_for_property = false:warning
dotnet_style_qualification_for_method = false:warning
dotnet_style_qualification_for_event = false:warning

# Naming: interfaces begin with I
dotnet_naming_rule.interface_should_begin_with_i.severity = error
dotnet_naming_rule.interface_should_begin_with_i.symbols = interface
dotnet_naming_rule.interface_should_begin_with_i.style = begins_with_i
dotnet_naming_symbols.interface.applicable_kinds = interface
dotnet_naming_style.begins_with_i.required_prefix = I
dotnet_naming_style.begins_with_i.capitalization = pascal_case

# Async methods should end with Async
dotnet_naming_rule.async_methods_end_in_async.severity = error
dotnet_naming_rule.async_methods_end_in_async.symbols = any_async_methods
dotnet_naming_rule.async_methods_end_in_async.style = end_in_async
dotnet_naming_symbols.any_async_methods.applicable_kinds = method
dotnet_naming_symbols.any_async_methods.applicable_accessibilities = *
dotnet_naming_symbols.any_async_methods.required_modifiers = async
dotnet_naming_style.end_in_async.required_suffix = Async
dotnet_naming_style.end_in_async.capitalization = pascal_case
```

Move every rule you can into this file. Whatever `.editorconfig` can express, express there, and save review time for what a config file can't check.

## 4. Centralize Package Management

Central Package Management (CPM) keeps all package versions in one `Directory.Packages.props` beside the `.sln`/`.slnx`.

```xml
<Project>
    <PropertyGroup>
        <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
    </PropertyGroup>
    <ItemGroup>
        <PackageVersion Include="xunit" Version="2.9.3" />
        <PackageVersion Include="xunit.runner.visualstudio" Version="3.1.5" />
        <PackageVersion Include="Microsoft.NET.Test.Sdk" Version="18.0.1" />
        <PackageVersion Include="Meziantou.Analyzer" Version="2.0.257" />
        <PackageVersion Include="SonarAnalyzer.CSharp" Version="10.16.0.128591" />
        <PackageVersion Include="Roslynator.Analyzers" Version="4.14.1" />
        <PackageVersion Include="xunit.analyzers" Version="1.26.0" />
    </ItemGroup>
</Project>
```

Project files then reference packages without a version. With CPM enabled, a `PackageReference` that carries its own version is a build error. An agent writes package versions from memory; CPM forces every version into a single file you review on a single line.

## 5. Code Reviews: AI First, Human Second

Four phases:

- Review with Claude, locally or in GitHub
- Specialized AI review tools such as CodeRabbit
- Enterprise quality tools such as SonarQube or Qodana (track trends across the whole repo over time)
- Manual review

Run the review in a **separate session** from the one that wrote the code. An agent that reviews its own work still has every reason it made those choices in front of it — it defends the code instead of reading it. A fresh reviewer sees more issues.

## 6. Why Coding Rules in Prompts Don't Work

No analyzer will tell you that `ShipmentHelper` is a bad class name, or that `Send(order, true)` is a bad signature. Those are issues only humans (and a review skill) catch.

Rules in the prompt work, then stop working: by the time the agent names a class, your rules are thousands of tokens back, competing with everything it has read since.

- **`CLAUDE.md`** is always-on context — facts true for the whole project: tech stack, conventions, folder layout. Keep it short (~100 lines).
- **A skill** is an on-demand capability — the procedure for a specific kind of task, loaded only when that task comes up.

A review fits a skill: known moment, same steps every time, long rule list that would be dead weight in every other conversation.

## 7. Build a Clean Code Review Skill for Claude

A skill is a folder with `SKILL.md` (plus optional `references/`), committed under `.claude/skills/` so teammates get it on pull. The frontmatter `description` decides when it loads: name the concrete checks, list the phrases you'd type, and say when **not** to use it.

The seven rules checked on every review:

1. **Never `async void`; suffix async methods with `Async`; accept a `CancellationToken`.** `async void` can't be awaited and its exceptions terminate the process. A missing suffix hides that the call is awaitable. A method without a token keeps working after the client disconnected.
2. **Return the most specific useful type.** `IEnumerable<T>` on an in-memory result invites double enumeration and re-running the query. Return `IReadOnlyList<T>` when the data is materialized.
3. **Never pass boolean flags as parameters.** `Send(order, true)` says nothing at the call site and usually means the method does two jobs. Split it (`SendDraftAsync(order)`).
4. **Avoid `Manager`, `Helper`, `Utils` class names.** They convey no responsibility, so the class grows forever. Name the class after its one job, or move each method next to the type it works on as an extension.
5. **Delete `#region` blocks.** A region hides code instead of removing it. A class that needs regions should be split.
6. **Don't extract an interface for a single implementation just for DI.** .NET injects a concrete class fine. A one-implementation interface costs a file and a "go to definition" jump and buys nothing until a second implementation, a test stub, or a cross-module contract exists.
7. **Prefer composition over inheritance.** Inheritance ties you to the exact order of classes in a chain; changing one forces changing the others. Composition (often the Decorator pattern) composes behaviors dynamically.

`references/clean-code-rules.md` carries twelve rules (naming, nesting, early returns, magic numbers, ...) each with before/after, opened only during review.

## 8. Make Sure the Review Catches Every Issue

Ask any model to "review this for clean code" against a 500-line diff and it does it poorly. Three things in the skill prevent that:

- **A fixed order of steps.** `Step 1 Decide the scope → Step 2 Run the detection → Step 3 Judgement pass → Step 4 Write the report`.
- **A grep command for every rule that can have one.**

  ```bash
  rg -n --glob "*.cs" "\basync\s+void\b"
  rg -n --glob "*.cs" "^\s*#region"
  rg -n --glob "*.cs" -e "\b(class|record|struct|interface)\s+[A-Za-z0-9_]*(Manager|Helper|Helpers|Utils|Utility|Utilities)\b"
  ```

- **A report shape that makes a skipped rule visible.** Every rule gets a row whether it found anything or not:

  | Rule | Result |
  | --- | --- |
  | 1. async void / Async suffix / CancellationToken | 1 finding |
  | 2. Most specific return type | PASS |
  | 3. Boolean flag parameters | PASS |
  | 4. Manager / Helper / Utils names | 2 findings |
  | 5. #region blocks | PASS |
  | 6. Single-implementation interfaces | PASS |
  | 7. Composition over inheritance | PASS |
  | 8. Readability (naming, nesting, magic values) | 3 findings |

Use a new session on purpose — the session that wrote the code is its worst reviewer.

## Summary

- **Guardrails beat instructions.** `Directory.Build.props` with `TreatWarningsAsErrors` turns standards into build errors the agent fixes in its own loop.
- **Analyzers do the work of a thousand prompt lines.** Meziantou, SonarAnalyzer, Roslynator, xunit.analyzers cover async mistakes, LINQ traps, and test errors for no tokens.
- **Move every rule you can into `.editorconfig`.**
- **Central Package Management stops invented versions.**
- **Reviews are the second gate.** CodeRabbit clears routine feedback; SonarQube/Qodana track the trend across the repo.
- **Rules in prompts fade, rules in skills don't.**
- **Make the review measurable** — fixed step order, a grep per rule, a report row for every rule.

None of this makes the AI a better engineer. It makes your standards enforceable.

> P.S.: `TreatWarningsAsErrors` can be applied per project, or overridden in legacy class libraries that would otherwise fire thousands of errors at once.
