# Desktop and release acceptance execution plan

**Status: not executed.** Verified historical source regression baseline f40527f490a576e40f777062014be2fcb4cb61fc, schema 54, Windows run 46091633837 (110 Python tests + 2310 native checks; zero compiler errors/warnings). Later revisions require their own evidence. This is a procedural companion to the unchanged legacy acceptance register, not a substitute for interactive acceptance or a release certificate. No executable is approved for distribution.

## Prerequisites and fixture

- Independent release-security review must authorize execution of the exact build before any desktop test. Use a dedicated clean Windows VM or isolated profile, no real project files, credentials or production database. Disable network for internal workflow acceptance. Do not bypass any security warning.
- Use the separately labeled synthetic acceptance workspace, record session ID, exact source commit and approved complete deployment inventory SHA-256 (not merely the executable hash). Use synthetic firm, two clients and projects, distinct employee and reviewer accounts, test-only agreement PDFs and no actual external transactions. Record dates, contractual caps, rates, approved time and expense amounts before comparing reports.
- Desktop steps use available controls; service/fault injection, accounting, migration and installer procedures require their corresponding controlled harness or qualified adapter. A missing capability is Blocked, not Pass. A source regression result alone is not a desktop result. Deferred scope must be explicitly accepted by owner; never erase a requirement.
- Keep individual test evidence, raw observations, reviewer and actual results privately. Record test ID, environment, artifact SHA256, commit, acceptance session, fixture ID, observed result, evidence reference, date, result (Pass/Fail/Blocked/Not run), and defect ID if applicable. A Pass requires all original acceptance invariants; partial evidence cannot close the original row.

The private acceptance CLI requires the exact compact UTF-8 inventory via `--inventory` and its independently retained complete digest via `--artifact-sha256`. Both inventory and evidence remain outside the checkout. The optional `--stage1` report retains all 49 master results, with mixed and deferred scope separately visible. This verifies inventory bytes and structure, not installed file contents, evidence authenticity, signature or security approval.

## Procedures covering all 42 original acceptance scenarios

| ID | Mode | Procedure | Required evidence / limitation |
|---|---|---|---|
| SEC-01 | Desktop | Sign in with wrong password; verify no workspace. Disable a synthetic staff account and attempt login. Exercise expiry with controlled service fixture, not altered workstation time. | Denied access; no secret in log; workspace unavailable. |
| SEC-02 | Migration + service | Create a synthetic staff account with temporary password; confirm sign-in requires reset and new password works. Migrated accounts require an actual migration adapter. | Temporary credential blocked from domain; old legacy hash not imported. Migration portion remains blocked. |
| SEC-03 | Service + desktop | Remove a synthetic role grant; attempt corresponding action in desktop and direct authorized test harness; restore grant and sign in again. | Denied with no domain mutation; UI not sole authorization boundary. |
| SEC-04 | Service + desktop | Use two synthetic projects and restricted documents. Attempt known foreign record ID and list/export/download access; cross-firm case is network scope. | No unauthorized data or counts. Per-project and multi-firm isolation remain unqualified; do not pass entire scenario. |
| SEC-05 | Desktop | Employee opens My time & expenses, then attempts rates, reports, review and billing controls with ordinary grants. | No rates, compensation or classifications exposed; no unauthorized approvals. |
| CRM-01 | Desktop | Create two clients with distinct contacts; search absent, unique and shared terms; select correct client, edit contact, cancel changes, inactivate one client. | Search and client IDs remain correct; inactive client cannot add contacts; no silent record reassignment. |
| ENG-01 | Desktop | Create synthetic proposal in Proposals & projects; screen draft, record issued with current frozen snapshot, record acceptance with evidence; inspect resulting project. | Exactly one linked engagement and Pending project; not start-authorized. |
| ENG-02 | Service | Use two independent authenticated service instances on a synthetic copy; race acceptance; inject failure in transaction via regression harness. | One linked project/engagement; failed transaction leaves no partial state. |
| ENG-03 | Desktop + service | Attempt issuance/acceptance without required clearance or with stale revision; inspect state and frozen snapshot. | Rejected invalid transition; history unchanged. |
| ENG-04 | Desktop | Open selected project; register a synthetic signed PDF with explicit review; decline authorization. | Agreement evidence may exist; project remains Pending. |
| ENG-05 | Desktop + service | Attempt authorization without signed PDF; test missing/changed evidence using disposable fixture and existing regression harness. | No start authorization; record actionable rejection. |
| ENG-06 | Desktop | Review synthetic signed evidence, parties and compliance; authorize project start explicitly with required evidence. | Active project only after authorization; traceable agreement and audit. |
| PRJ-01 | Desktop | Open project handoff and setup; record one item In Progress; refresh and reopen. | Setup status reflects actual recorded state; not complete merely because row exists. |
| PRJ-02 | Desktop | Create two projects; open planning/edit dialog for first, cancel or switch context; reopen second and try stale selection. | No changes against wrong project; stale command rejected or confirmation required. |
| PRJ-03 | Desktop + service | Create phases/tasks and assign synthetic employee within known inclusive date range; view My work and Staff schedule across boundaries. | Matches exact task assignment and dates; no unrelated employee work. |
| DOC-01 | Service + desktop | Register synthetic unrestricted and restricted files; review with ordinary staff. In disposable harness test traversal, absolute path and reparse point. | No unauthorized contents or external file read. |
| DOC-02 | Service | Use existing fault-injection harness for staging and metadata commit; inspect recovery and signed-evidence state. If no matching failure fixture, mark blocked. | No false evidence or success after incomplete operation. |
| WORK-01 | Desktop + service | Clock in using synthetic employee; attempt concurrent second clock-in; clock out; review attendance history and correction. | One open interval; original and correction history retained. |
| WORK-02 | Desktop | Start assigned task; submit actual time and expense; independent reviewer rejects one, approves one; submit linked correction and classify separately. | Unapproved/rejected work excluded; immutable facts and replacement lineage retained. |
| QA-01 | Desktop | Create QA item, close with evidence; attempt invalid edit; propose additional service and attempt work start before authorization. | Closed QA history preserved; unauthorized additional service cannot start. |
| FIN-01 | Desktop | Approve time independently but leave unclassified; check billing queue; classify nonbillable, then inspect permitted report. | Only eligible billable approved facts enter candidate queue; explicit classification history. |
| FIN-02 | Desktop + service | Record synthetic contract billing policy; create two internal drafts using controlled work allocation; exercise concurrent numbering and rollback in harness. | Unique internal draft numbers and atomic allocations. Actual issued invoices still unqualified. |
| FIN-03 | Accounting | Test actual issued invoice void/reversal only in separately approved disposable accounting fixture, including paid/duplicate cases. | Authoritative accounting reconciliation required; protective hold is not a void. Blocked. |
| FIN-04 | Accounting | Use qualified receipt adapter in separately approved disposable fixture to attempt zero, negative and excess receipt. | No prohibited settlement. Native production adapter not qualified; blocked. |
| FIN-05 | Accounting | Race two receipts against same authoritative remaining balance with recovery witness. | No overcollection or duplicate remote commit. Blocked. |
| FIN-06 | Accounting | Qualify deposit and same-project application with authoritative Manager accounting treatment. | No duplicate cash or overapplied advance. Blocked. |
| FIN-07 | Accounting | Exercise wrong project, excess refund/application and repeat reversal in qualified fixture. | No partial financial transaction; blocked. |
| FIN-08 | Accounting | Inject failure between local attempt and remote application acknowledgement; verify authoritative reconciliation and retry guards. | No duplicate posting; explicit uncertainty and recovery. Blocked. |
| FIN-09 | Accounting + desktop | Compare qualified actual receivable and settlement states with authorized reports, including credits and reversals. | No inferred cash or posted balances from internal draft values. Full scenario blocked. |
| FIN-10 | Migration + accounting | Exercise decimal precision, midpoint and legacy floating-point conversion exceptions using actual reviewed source mapping. | No silent precision loss. Full financial migration blocked. |
| REP-01 | Desktop | Generate project operations and financial reports for a known range; compare actual approved time, expense, internal draft and hold totals with fixture; verify denied staff access. | Correct date/filter scope and totals; full legacy eight-report parity requires separate mapping. |
| MIG-01 | Migration | Run metadata mapping preflight on reviewed synthetic inventory; preview normalized transfer with orphan, duplicate or unmapped values. | No import on mismatch; actual DAO mapping and non-CRM scope remain unqualified. |
| MIG-02 | Migration | Reconcile complete legacy copy with explicit field mappings, counts, original IDs, relationships, historical financial balances and sequences. | Full historical migration blocked pending actual source metadata and adapters. |
| MIG-03 | Migration | Preview normalized CRM transfer; inject failure and repeat import with same bytes/hash; preserve original Access copy. | No duplicate or partial CRM records; original source unchanged. Actual DAO extraction remains pending. |
| REL-01 | Release | After security approval only, install exact signed release in clean isolated Windows VM without Access/Office; exercise login, workflow and uninstall. | Installer and data preservation accepted; currently blocked. |
| REL-02 | Release + service | Create synthetic recovery copy, retain hash separately, restore to new path, rehearse old schema upgrade and failure; test interrupted installer when available. | Source intact; no live replacement, recovery hold retained. Installer portion blocked. |
| REL-03 | Commercial | Qualify invalid/expired offline entitlement and continued export of owned data. | Commercial licensing deferred; not passed. |
| NET-01 | Network | Exercise cross-firm and stale-role requests against future server API. | Server isolation unimplemented; deferred. |
| NET-02 | Network | Exercise parallel writes and retries over future server transport. | Network concurrency unimplemented; deferred. |
| HR-01 | Desktop | Create synthetic employees with expired and upcoming license dates; inspect expiry status; sign in as ordinary staff and attempt compensation/rate access. | Correct expiry boundary and no restricted rates; alert delivery separately outstanding. |
| UX-01 | Desktop | Test empty firm, zero/multiple CRM matches and switching selected projects, close/cancel dialogs and reopen session. | No silent selection or wrong-record writes; consistent navigation. |
| COM-01 | Commercial | Create new synthetic firm with distinct prefix; assess logo, templates and firm scoping. | Branding, template and commercial multi-firm requirements incomplete; deferred. |

## Supplemental lifecycle and billing safety cases

These supplement, not replace, the original register. All remain not executed at desktop level.

| ID | Procedure | Expected |
|---|---|---|
| CRC-LC-01 | Complete authorized project setup, close with pending work/QA then clear blockers and close; attempt ordinary new work. | Pending blockers prevent close; closed status prevents ordinary work. |
| CRC-LC-02 | Submit historical actual time on eligible started task through current closure cutoff; attempt later date and stale closure; independently review. | Only eligible historical record accepted, no reopen or conflict override. |
| CRC-LC-03 | Invalidate approved unallocated fact with evidence; submit linked correction and independently review; attempt second successor. | Original and lineage retained; one replacement; only effective approved work contributes. |
| CRC-BILL-01 | Cancel unapproved internal draft; reconcile erroneous allocation using exact reviewed evidence; attempt same action against approved draft. | Cancelled-only route permitted with evidence, approved source protected. |
| CRC-BILL-02 | Hold approved draft, attempt CSV/DOCX export, review history; attempt self-release then independent valid release and renewed hold. | Held export blocked, allocations/caps unchanged, no external issue/credit. |
| CRC-ACCT-01 | In separately qualified synthetic Manager fixture only, compare exact candidate and explicitly capture fresh observation; remove provisional link. | Historical evidence retained; new capture denied; no external write or actual settlement inference. |
| CRC-REC-01 | Create new acceptance session, close and reopen; try invalid session ID and changed marker. | Exact session persisted; no fallback or default-store mutation. |

## Exit criteria

All required single-user scenarios must pass on the exact approved release revision or have an explicit owner-approved scope exclusion; blocked accounting, migration, installer or security requirements cannot be treated as passed. No claim of full commercial parity until deferred scenarios are executed. Preserve defect/retest records and prior evidence.
