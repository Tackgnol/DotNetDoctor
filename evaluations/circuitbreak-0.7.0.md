# CircuitBreak 0.7.0 evaluation

- Revision: `a30251d65742d7a1517acd3e5e935ee519b20ab1` (clean checkout)
- Tool: `repo-doctor 0.1.0`
- Profile: `bootstrap/server-core@0.1.0`, SHA-256 `adbb0eca5f161d7a8ca84cfbb82bacc637ce74b584d16f3b5b62a1ceb6182012`
- Command: `repo-doctor scan CircuitBreak.sln --format json --output artifacts/circuitbreak-scan.json`
- Result: complete, 5/5 projects analyzed, 8 findings, 0 analysis problems, 7.413 seconds

## Manual review

| Rule | Count | Review | Rationale |
| --- | ---: | --- | --- |
| MA0009 | 4 | legitimate exception | The generated regexes parse local diagram/reference files. The patterns use bounded character classes or reluctant linear scans and show no evident catastrophic-backtracking shape. A timeout remains reasonable hardening if these APIs later accept untrusted or very large input. |
| MA0042 | 4 | legitimate exception | Synchronous file writes occur in a one-shot demo generator. Its top-level program is async for unrelated rendering work; changing these small sequential writes to async has no demonstrated product benefit. |

The run exposed a scanner defect: CircuitBreak enables `CodeAnalysisTreatWarningsAsErrors`, and the first report mislabeled profile warnings as errors. The scanner now derives finding severity from the resolved analyzer policy/configuration and ignores build-wide warning promotion. The final report contains eight `warning` findings with `policySource: profile`.

No confidence value was raised. This single repository supplies useful negative examples but is not enough evidence to generalize either rule's precision.
