# Approved overhead correction — proposed schema 38

Base 1d265ef (verified schema 37). Match the controlled erroneous-fact correction workflow for non-project overhead time. Do not change payroll, actual work, wages, reimbursement, or external accounting. This is not authority to remove actual earned hours.

- Existing Work.Correct and Work.Review permissions with employee/project/client visibility, independent actor (including no founding-owner self-invalidation), explicit confirmation and mandatory evidence. Only approved overhead without previous invalidation. Preserve original submission, approval and immutable decision. Atomic audit, race and rollback tests.
- Effective Invalidated state visible in employee and management queues; firm time mix counts separately from rejected and pending, labor cost and combined daily-time limit exclude erroneous invalidated facts. No billable recognition.
- Employee may submit one explicitly linked corrected fact; same employee, new operation, complete intent comparison with predecessor state, new independent review, valid date/category/minutes and shared daily cap. Exact replay of original returns original ID. Reject other correction path or standalone replay. Immutable lineage and insert safeguards.
- Desktop action for authorized reviewer and employee, explicit confirmation; preserve original evidence. Migration/restore, direct SQL and regression tests. Actual payroll correction and attendance correction remain separate.
- Test-first stubs deliberately fail. Full Windows qualification required before promotion; no production data, posting, main merge or executable distribution.

Candidate implementation adds immutable overhead invalidation evidence, existing Work.Correct authorization, daily time and report projections, explicit correction UI and linked replacement. Concurrent invalidation/replacement, SQL guards and audit rollback regressions added. Windows verification and interactive acceptance pending.
