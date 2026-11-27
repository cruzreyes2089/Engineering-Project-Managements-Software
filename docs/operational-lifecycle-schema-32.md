# Operational lifecycle — proposed schema 32

Base e1215a7; PRJ-01 closeout. Decision: append-only project lifecycle events and an effective operational state view, leaving original Pending/Active start authorization and financial state intact. Use explicit OperationalClosed label, not financial or legal closure. No new work authorization by implication.

Acceptance:
- Administrator-seeded Projects.Closeout permission plus project and operations view; record immutable event and audit in one immediate transaction. Explicit confirmation, note, and expected latest event ID (null only before first transition). No duplicate or stale transitions.
- Close only an authorized active project with complete setup, no unfinished tasks, open QA, pending scope decisions or pending work submissions, and current conflict screening. Re-evaluate in the write transaction; zero counts alone never close a project.
- Reopen only operationally closed project, explicit note and confirmation, original intact start evidence, active client and current conflict gate; no automatic reopening. Preserve all history.
- Expose effective state in handoff, project register, task projections and closeout review. Keep physical start state for accounting; late financial classification, billing preparation and reconciliation remain separate.
- While operationally closed block new or edited phases/tasks, new time/expense facts, and additional-scope authorization (including database guards). Exact idempotent replay of existing work facts allowed. Keep document registration, compliance review, QA observation and other evidence capture available so post-close concerns are not suppressed. Closure does not represent final delivery, liability release, collection, or accounting settlement.
- Add schema migration, administrator-only grant, restore version qualification, and migration/immutability/concurrency regressions. Use synthetic fixtures; no production data, distribution or merge to main.

Test-first API stubs are intentionally nonfunctional and isolated. Test failure and full Windows green verification required before promotion. UI interaction and independent release security remain separate acceptance gates.
