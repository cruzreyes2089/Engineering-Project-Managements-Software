# Started-task discontinuation — proposed schema 34

Base 18d1d25 (verified schema 33). A started assignment may need to stop because of a change in scope, employee availability or conflict concerns. Do not mark unfinished work Completed, cancel it as if never performed, or overwrite employee assignments.

Design decision: separate immutable discontinuation evidence for physically InProgress tasks only, with task revision, reason, actor, UTC timestamp and local stop date recorded at decision time. Manager authorization uses Projects.Plan, Projects.View, Clients.View and Employees.View. No conflict clearance required to stop work. No changes to original work, approval, billing classification or invoice allocations. No reinstatement or retroactive reassignment; any remaining work is a separately authorized new task.

Acceptance:
- Confirmed decision with exact project/task/revision, nonblank reason, no existing cancellation/discontinuation, operationally active project and original start authorization. Atomic audit and evidence. Deny stale, duplicate, wrong-project or unauthorized calls. DB guards preserve original task and decision.
- Project register, own work and staff schedule show Discontinued as historical, not Completed; exclude from outstanding count and active estimate totals; report discontinued separately.
- Stop future work authorization immediately. Existing work facts remain immutable, reviewable and billable under existing policies. Exact retry stays idempotent. While project remains operationally active, permit actual historical time/expenses within original task dates through stop date inclusive, including same-day late reporting; never interpret submission as authorization to perform work. Date precision is calendar day, not verified time of day. Future dates blocked. After operational close, existing closure rules apply; late correction after closure requires a controlled separate workflow. Do not suppress actual work or alter payroll by implication.
- No financial amounts or management decision notes in ordinary employee task projection. Preserve closeout pending work counts and financial totals. Discontinuation is not client termination, contract amendment, payroll reversal or conflict clearance.
- Migration, restore, historical fixture and UI updates; add concurrency, rollback and SQL negative checks. Synthetic tests only. No production posting, main merge or executable distribution.

Test-first stubs intentionally nonfunctional; require observed red then full green Windows CI before promotion.

Candidate implementation: schema 34 records a local calendar stop date using SQLite's local clock, UTC evidence, immutable decision and original assignment, and blocks post-stop dated facts at database and service boundaries. Added rollback, concurrent decision, SQL and employee projection regressions. Historical work entry UI explicitly identifies discontinued assignments. Calendar-day granularity and later closeout corrections remain limitations. Full Windows verification and interactive acceptance pending.
