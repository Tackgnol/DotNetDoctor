# Agent workflow

Agents should run `repo-doctor scan <target>` before changing code and retain a JSON report for review. For a regression gate, scan the actual baseline revision and the head revision separately with the same pinned tool and vendored profile, then pass the baseline report with `--baseline`.

```text
repo-doctor init .
dotnet restore
repo-doctor scan ./Application.slnx --format json --output ./artifacts/base.json
repo-doctor scan ./Application.slnx --baseline ./artifacts/base.json --format json --output ./artifacts/head.json
```

Exit code `0` means the requested analysis completed and passed; `1` means completed blocking findings; `2` means incomplete/invalid analysis or comparison; `130` means interruption. Reports can contain repository-derived messages and paths, so treat them as repository data.
