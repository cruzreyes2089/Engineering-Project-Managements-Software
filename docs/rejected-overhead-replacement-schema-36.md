# Linked rejected overhead time replacement — proposed schema 36

Depends on verified schema 35 (candidate 20d1781 at drafting). Extend the same immutable successor pattern to rejected non-project time; do not mix project and overhead identity domains or allow overhead to become billable.

- Work.Own, active employee, owned independently rejected predecessor, one successor per record, original rejection retained. New operation key with full normalized intent and predecessor; exact retry idempotent, different intent or standalone replay denied. Date, minutes and category validated under existing shared daily limit. Rejected successor can be corrected by another linked fact.
- Atomic fact/lineage/audit, monotonic successor identity, unique index, insert guards and immutable row protection. Independent review and existing founding-owner policy unchanged. Reports count pending and rejected separately; no change to approved labor cost or billing eligibility.
- Employee and management views show lineage IDs; UI action from selected unreplaced rejected overhead fact, explicit confirmation. Add rollback, concurrent retry, direct SQL and migration/restore tests. Test-first stubs intentionally nonfunctional. Require observed red and full Windows green before promotion.
- No production posting, distribution, or payroll reversal. Approved record adjustments and attendance correction remain separate workflows.

Candidate implementation mirrors project-work lineage with a nullable immutable self-reference, unique predecessor, monotonic ID, same-employee rejected-source guard and existing combined daily-time protections. UI and reviewer projections expose lineage; original remains rejected. Audit rollback and concurrent retry regression added. Windows verification and interactive desktop acceptance required.
