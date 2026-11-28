# Initial internal release — scope reconciliation and usability audit

**Scope clarification — 2026-11-28:** Owner clarified that the intended product is free software for public commercial use. This document's internal-only scope is historical/planning context for controlled qualification, not a restriction on the ultimate public release. See [public release design](public-release-design.md) for the public distribution requirements. Existing gates, security hold, acceptance cases and explicitly approved Access migration deferral remain in force; no implicit approval of other feature exclusions.

Status: development proposal, not a release approval. The owner has explicitly directed simple, straightforward, intuitive operation and optional formal document management. Other scope exclusions below are recommendations requiring owner approval; retain original requirements and all 56 acceptance IDs. No data, credentials or private security evidence in the public repository.

## Workflow rule

Default daily use: clients, proposals, project authorization, tasks, time/expenses, review, billing preparation, and reporting. Employees use ordinary project folders. Formal file/revision registration and transmittal tracking are optional; required agreement and conflict evidence are not optional. Advanced tools appear only when useful and authorized. Do not add new screens merely to expose an internal state or audit event. Do not introduce duplicate accounting or duplicate file maintenance.

## Reconciliation of every original feature domain

| Original ID | Proposed initial scope | Actual evidence or remaining gap |
|---|---|---|
| SEC-01 | Required | Native authentication and temporary password change; interactive session and recovery acceptance pending. |
| SEC-02 | Required | Action-level grants and session guards implemented; record-level access policy and local confidentiality must be independently reviewed. No network or multi-firm claim. |
| CRM-01 | Required | Native clients/contacts with search and numbering; fresh-firm desktop acceptance pending. |
| ENG-01 | Required | Proposal, manual issue/acceptance, linked project implemented; final counsel-reviewed agreement and normal external delivery still required. |
| ENG-02 | Required | Signed agreement and separate start authorization, conflict review and declaration evidence; actual legal review remains external. |
| PRJ-01 | Required | Setup checklist, project contacts, status and operational closure; contractual/financial closeout remains separate. |
| DOC-01 | Optional formal register; required legal evidence | Ordinary files remain in project folders; folder template independent; formal register collapsed. Actual safe production folder setup remains an unresolved core usability requirement, not completed. |
| WORK-01 | Required | Own tasks, time, expenses, attendance; exact minutes stored. No payroll claimed. |
| WORK-02 | Required | Factual review, linked corrections, separate billing classification; interactive acceptance required. |
| QA-01 | Required, lightweight | Quality items and approved additional scope; commercial amendment documents still pending. |
| FIN-01 | Required | Billing classification separate from actual facts. |
| FIN-02 | Internal draft preparation only, proposed | Drafts and verified export; issue, payment and void in Manager. No actual posted linkage or integration qualification. |
| FIN-03 | Proposed external Manager workflow | Production receipts not qualified; do not claim native receipt posting. |
| FIN-04 | Proposed external Manager workflow | Retainer, application, refund and reversal not qualified; no duplicate ledger. |
| FIN-05 | Required operational reports; proposed external authoritative accounting | No inferred A/R, revenue, profit or settlement from internal drafts. Actual receivables and reminders in Manager or reviewed manual process until qualified. |
| UX-01 | Required | Milestone 09 navigation, document, setup, work, billing and report simplification committed; WPF acceptance not executed. |
| COM-01 | Basic company setup and recovery required | Existing name/numbering/recovery; branding, templates and commercial license platform deferred only by explicit scope approval. |
| PRJ-02 | Basic scheduling required | Phase/task and staff schedule; workload scenario is optional. No daily allocation claim. |
| HR-01 | Basic directory/licenses required | Dashboard recorded-date reminders; no regulator verification or automatic email delivery. |
| REP-01 | Essential operational/time/cost reports required | Five named legacy types mapped; three unnamed types unresolved. No full eight-report parity claim. |
| COM-02 | Proposed deferred if fresh-firm start approved | Normalized CRM tested; actual Access DAO, historical/financial migration not qualified. |
| COM-03 | Required safe internal deployment | CI only; exact approved executable, signing, installation and recovery acceptance outstanding. Commercial entitlement may be deferred with approval. |
| COM-04 | Proposed later release | Network/API and cross-firm isolation unimplemented. Single-user first does not authorize sharing SQLite over network. |

## Acceptance priorities, not new feature requests

1. Resolve security hold with independent exact-artifact review and explicit isolated execution authorization. Do not execute or redistribute the original blocked archive, bypass warnings, merge, or use live records.
2. Confirm owner scope for fresh-firm single-user start, authoritative accounting in Manager, and which legacy/import/commercial requirements can be deferred. Obtain approved agreement language from counsel.
3. Exercise a simple end-to-end synthetic workflow, from client/proposal to authorized project, task/time/expense, review, draft, report, and recovery. Test actual production-style folder setup only after its source design and safe acceptance are authorized; no activation of candidate schema 48/49 or reconstruction of missing 54 by assumption.
4. Record observable usability: role-relevant navigation, one obvious action, no duplicate registration, no stale project selection, no silent save failure, no unauthorized data, no redundant prompts. Log defects and actual evidence against original IDs; never infer 56-case pass from CI. Review on small screen and with keyboard as well as mouse.
5. Qualify installer, recovery and upgrade on clean Windows; only explicit release authorization can remove hold.

## Baseline and status

PR #37 head 31f01cad5f41f49188d131a18a65810175cb349e: Windows run 46102870223 / job 140849037448; 151 Python tests, 2345 native checks, zero compiler warnings/errors. Actual merge checkout ee59a012d58bb16c4ee249bb37825e5cf87c3504, tree 5bc899896d52b66f4212aa837795da021464f38c; SDK 10.0.404, win-x64 runtime packs 10.0.15; inventory SHA-256 7c0b14012293374c275deba33f916038af3dca51b5f036703a57876c99da74d8, unsigned, releaseAuthorized=false. Synthetic prior-version upgrade 46102870197 passed. DAO qualification 46102870194 failed; no successful legacy extraction claimed. Current supported schema 47; 48/49 isolated only. Interactive acceptance: 0 of 56 executed.

This source review is not independent security clearance, actual desktop testing or an owner-approved release scope. See the original feature register and desktop acceptance plan for retained requirements.
