# Profile contribution

Profiles are portable JSON policy files. The scanner loads only the pinned analyzer bundle and never executes profile-provided code, downloads profiles, or resolves parent profiles.

## Submission checklist

- Include the profile JSON, license, semantic version, authors, intended repository context, and rationale for meaningful differences.
- If derived from another profile, keep its immutable `id`, `version`, and canonical SHA-256 in `derivedFrom`.
- Run `repo-doctor profile validate ./profile.json` and record the tool version, profile hash, and reproducible scan commands.
- Report seeded defects, legitimate lookalikes, reviewed real-repository findings, rule coverage gained/lost, runtime, and scan completeness.
- Mark unknown real-world findings as `unreviewed`; do not claim recall or false-positive rates from unreviewed samples.

Maintainers publish acceptance or rejection in a reviewable document or pull request, including the evidence and any appeal path. Promotion changes a versioned recommendation manifest in a future tool release; it never edits profiles already vendored by users.
