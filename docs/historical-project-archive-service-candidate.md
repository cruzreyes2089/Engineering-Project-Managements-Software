# Historical project archive service integration candidate

Source-only candidate at tools/migration/prototypes/HistoricalProjectArchiveCandidate.cs, outside the SDK persistence project and not included in application compilation. Schema 46, deployed permissions, startup migrations and the desktop UI are unchanged. No production entry point is activated.

## Proposed native behavior

- Preview and import use the existing authenticated LocalPractice session and administration check, plus HistoricalProjects.View, Administration.HistoricalProjectImport and Clients.View. Seed the two new grants only in an approved future migration; ensure existing revocations are preserved and bootstrap matches upgrade behavior.
- Parse the exact CRM, project and census bytes again inside the operation, verify all independently retained digests, validate existing CRM mapping history and resolve every parent through that history. No operational projects, engagement records, work authorizations, invoice records or numbering changes.
- Explicit confirmation and a nonempty review note precede writes. A single immediate transaction inserts one archive batch, all source rows, one seal and one audit event. Read back all source fields and native client identities, with matching digest headers and expected counts, before committing. Disposal rolls back on any failure.
- Repeat the exact package only after the same complete readback, including the audit marker; do not add another audit event on replay. A different package is not mergeable; incomplete evidence is an error. The audit count check detects missing/duplicate markers, not tamper-proof provenance or independently verified actor identity.
- Read-only register requires HistoricalProjects.View and Clients.View every time, returns only sealed, count-consistent archive data with one audit marker, omits authorization evidence references and explicitly reports WorkAuthorized=false. Historical labels are not present-day approvals. An archive entry's default string representation is redacted. Raw evidence stays in the confidential archive and is never treated as a filesystem path.

## Verification evidence

Candidate 249fe75b891e748410a95f132b78df283d5aefa6, audit-marker hardening a426497adbc3b7faa7171e9e12f30d58a0e058ff (blob 5431878c26b2d6ec2b902ac8a906deab78b84a7b), test source 624dbf96a0806f6861e6af707454ead70d3cf47d.

Local Python unittest discovery passed 12/12 on a disposable in-memory SQLite database (six previous archive prototype tests, six new candidate SQL tests). The local test files and prototype SQL were checked against their exact Git blobs: 8e2a8f0c326c78d970385f6b0acd6d557ff59dfd, 16d28a23a3f997c3461278c873f9d61449ea8ff5 and b0222865bb6f2e26a8363562d0659aff695a7c72. All six SQL constants were copied verbatim from the fetched candidate source into a local extraction fixture; the full C# file was not executed or hash-verified locally. The tests exercise initial counts, digest and value readback, preserved source whitespace, no operational numbering/project writes, unsealed/unaudited states, invalid-parent rollback, injected audit failure rollback even after sealing, and second-batch rejection. They do not exercise the C# logic, actual permission checks, exact replay behavior at service level, full schema, or native SQLite provider.

## Remaining activation gates

No combined CI result was available for the preceding head 248dab30fc8ac233c22167f5aaed8cf18021e45d, and the local environment has no .NET compiler. Review the candidate against native persistence, run exact-revision Windows source regression, then prepare a separately reviewed archive schema migration and recovery/restore compatibility. In a disposable native database, verify grants (including forced password change, logout, revoked view/import/admin), all digest and parent checks, transactional audit/row/seal fault injection, exact replay including all fields and changed-package denial, incomplete state failure, redacted register projection, and no operational or accounting side effects. Only then wire the desktop archive register. Keep the current release hold; no live migration or security bypass.
