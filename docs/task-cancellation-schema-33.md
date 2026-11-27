# Planned task cancellation — proposed schema 33

Base 4fac950 (verified schema 32). The work planning design excluded cancellation; rejected additional-scope tasks can remain Planned indefinitely and block operational closeout. Decision: separate immutable cancellation evidence for planned, unstarted tasks only, retaining original physical task state and plan revision. Started or completed task correction and reassignment, time correction and reversal are separate packages; no deletion or retroactive financial adjustment.

Acceptance:
- Existing Projects.Plan plus project view and employee view for manager-level cancellation and evidence access. Explicit confirmation, nonblank reason, exact project/task/revision, open operational state, no prior cancellation, no recorded work facts. Atomic evidence and audit. No automatic cancellation on scope rejection.
- Retain original task row, employee, dates, estimate and scope. Show effective Cancelled in project register, staff schedule and own work; exclude from live estimate totals and outstanding task counts; count cancelled separately in operations reports. Schedule includes history, not a daily allocation model.
- Block edits, starts and new work on cancelled task in service and database; cancellation is irreversible in this slice. Create new task for replacement, subject to current scope and authorization. Do not permit cancelled tasks to block legitimate closeout; retain history and phase boundaries.
- Update schema migration, restore version, historic migration fixtures, operational lifecycle SQL guard and read projections. Add concurrency, rollback and direct SQL regression; UI manager cancellation with required reason and confirmation. No changes to accounting posting controls.

Test-first branch adds throwing API stubs and report contract only. Full Windows red then green verification required. No main merge, production data or executable distribution.
