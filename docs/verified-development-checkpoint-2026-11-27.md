# Verified development checkpoint — 2026-11-27 (Pacific time)

Development only. Do not merge, release, distribute executable artifacts, override security warnings, or post accounting transactions.

## Authoritative source and evidence

| Item | Identity / status |
|---|---|
| Last verified integrated candidate | eb3126fbfb595c3a4df68285525a0e5d3954f32d (draft PR #10) |
| Windows regression | https://github.com/cruzreyes2089/Engineering-Project-Managements-Software/actions/runs/46097025595 — 151 Python tests, 2208 native checks, zero compiler errors/warnings; dependency review and runner-local inventory passed |
| Prior source upgrade | https://github.com/cruzreyes2089/Engineering-Project-Managements-Software/actions/runs/46097025616 — passed |
| DAO | https://github.com/cruzreyes2089/Engineering-Project-Managements-Software/actions/runs/46097025660 — blocked; provider unavailable |
| Build inventory | 409 files, 151071945 bytes; layout inventory SHA-256 18300b450bbbed33d7f286d2cdaeff8583fd0ead2ebb77890628b312b5ce5e7e; app unsigned; not independently verified or released |
| SQLite connection hardening | 5bca534cd378f63259df57f09e521955ade22c80 (draft PR #11); run 46097211804 pending at checkpoint; upgrade run 46097211755 passed |

## Source reconciliation

The normal startup schema is 47. Schema 48 budget acceptance and schema 49 document acceptance exist in isolated synthetic workspaces. No normal startup activation of either. Source inspection supersedes stale PR descriptions. Any previously reported higher schema or test counts without corresponding Git tree, source archive and run evidence are **unverified** and must not be presented as a recoverable implementation. Do not create a replacement database at that schema number, infer its DDL, or downgrade an existing database. Preserve newer work if recovered and compare changes against this checkpoint before integrating.

For every subsequent source change retain commit SHA, branch/PR, exact CI checkout (including merge SHA where applicable), results, and limitations. Keep historical source and evidence distinct from current qualification. No binary or private business data in public repository.

## Next implementation and acceptance work

- Finish PR #11 exact revision regression; review failures before merging any isolated candidate. Retain branch #10 as known-good development baseline, not a production release.
- Compare actual schema 49 document and budget features against legacy acceptance register. Record missing requirements and use additive migrations only with explicit reviewed activation/rollback. Retain existing document folders, QA/QC, structural drawings, plan-check responses and outgoing documents terminology when integrating. Do not assume folder creation or actual issuance exists simply because registration and synthetic attestations exist.
- Maintain existing release gates in docs/single-user-release-gates.md and docs/independent-release-review-handoff.md. DAO migration needs a compatible isolated Windows host; no provider bypass or actual database extraction in public CI. Manual desktop acceptance and physical installer acceptance remain unexecuted.
- Manager.io remains authoritative; synthetic and read-only evidence does not qualify live posting. No repeat posting of prior test invoices.
- Independent security review and disposition of original blocked artifact remain mandatory; clean regression, advisory review and unsigned newer build do not clear the hold.
- Do not add unnecessary features or simulations simply to increase test count. Combine related work and only seek owner input for essential decisions or authorized local acceptance.

This document records evidence; it does not change application code, schema, or release authorization. It is isolated from the validated source candidate to avoid altering its exact revision.

## Subsequent verified candidate — cycle 12D

- PR #11 head 5bca534cd378f63259df57f09e521955ade22c80, Windows run https://github.com/cruzreyes2089/Engineering-Project-Managements-Software/actions/runs/46097211804: passed 2212 native checks and 151 Python tests; zero compiler warnings/errors, dependency review passed, 409 files / 151072713 bytes, layout inventory SHA-256 f9aa22a01958c44a0cf07b720c4c708915878e8031848f663291dcf26d13e619. Unsigned, not independently verified, no distribution. Prior-source upgrade #57 passed; DAO #56 blocked.
- This supersedes PR #10 as verified development baseline. Normal schema 47 and isolated schemas 48/49 unchanged. No schema 54 recovery.
- Source review confirms acceptance-only folder creation service already exists (dedicated synthetic root, reviewed plan, existing directory preservation, audit). No production or shared storage qualification. CRC template defaults unchanged.
- PR #12 head 27dc15dd154de8b35f4db131b1eb40f7a09b3e08 contains shared reserved-device path checks plus 16 additional synthetic assertions. Exact-revision CI pending. No production changes or release approval.

## Subsequent verified candidate — cycle 12E

- PR #12 head 27dc15dd154de8b35f4db131b1eb40f7a09b3e08, Windows run https://github.com/cruzreyes2089/Engineering-Project-Managements-Software/actions/runs/46097594665: passed 2228 native checks and 151 Python tests, zero compiler warnings/errors, all 16 additional path assertions and dependency review passed. 409 files / 151073253 bytes; layout inventory SHA-256 be5f07603a904912b176104ee7d0b2dde247991013835c5c4e43176fca247523. NotSigned, not independently verified, no release. Prior-source run 46097593993 passed; DAO run 46097594099 failed (provider unavailable in prior qualification; exact latest failure not separately investigated).
- PR #12 supersedes PR #11 as verified development baseline. No normal or isolated schema changes.
- PR #13 head c74ef76db21abcb3a2dbf3908f04dd6a4d642e93: document registration shares Windows reserved-device validator; six filename/unchanged-register checks and thirteen live grant tests added. CI pending. No production documents, real issue/delivery, accounting writes, distribution or release authorization. Schema 54 source gap remains.

## Cycle 12F — recovery publication integrity candidate

- Latest fully verified baseline remains PR #12 / 27dc15dd154de8b35f4db131b1eb40f7a09b3e08 (run #493). PR #13 Windows #500 (46097803373) still in progress; prior-source upgrade #59 (46097803425) passed; DAO #58 failed, exact reason not separately investigated in this cycle.
- PR #14 head acff7ba364c0ce2530bbe8308e41122291e96f8a is stacked on PR #13 and adds staged and published file length/hash verification for backups, isolated restore and document exports, matching recovery creation/restore size limits, SQLite connection safeguards and seven synthetic assertions. Exact-head CI pending. No physical durability, independent security, encryption or race-free path assurance. No new production schema activation.
- Source inspection of offline accounting rehearsal confirms operation fingerprint, approval, unresolved-attempt block and sticky lifecycle hold. No actual Manager access or posting in this cycle. Independent release/security, actual Access migration, desktop and physical installer qualification, and higher-schema source reconciliation remain outstanding.
