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
