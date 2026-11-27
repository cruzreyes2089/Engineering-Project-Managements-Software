# Conflict reporting correction

Base: schema 31, `53c16a8`. Scope: reporting only; no schema migration or change to project-start, task-start, scope-approval or declaration policy gates.

## Acceptance

- A project review returned by `RecordConflictReview` reports the same effective Clear/Hold result as the authoritative gate evaluated in that transaction, including missing or concerning declarations.
- History continues to describe the recorded screening answers and associated jurisdiction policy evidence, not reconstructed historical authorization. The UI must label this limitation explicitly and identify the current screening status separately.
- Existing immutable reviews, audit action, revision checks, permissions and clearance rules are preserved.
- Windows build, smoke tests and existing schema tests must pass at the candidate revision before promotion to the active draft development branch.

## Verification

Regression assertions are in `ProjectHandoffTests.cs`. The initial test-only commit is expected to fail at the missing declaration case on the old implementation. Do not describe the correction as complete until the red case and green suite are observed. All evidence is synthetic; no live financial or public-employer data.
