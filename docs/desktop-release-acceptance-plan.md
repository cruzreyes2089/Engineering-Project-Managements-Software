# Desktop and release acceptance execution plan

**Status: not executed.** Source regression baseline 25122753f95a91239778135375c9df6ce3810620, schema 46, Windows run 45951746285 (22 Python tests + 1618 native checks; zero compiler errors/warnings). This is a procedural companion to the unchanged legacy acceptance register, not a substitute for interactive acceptance or a release certificate. No executable is approved for distribution.

## Prerequisites and fixture

- Independent release-security review must authorize execution of the exact build before any desktop test. Use a dedicated clean Windows VM or isolated profile, no real project files, credentials or production database. Disable network for internal workflow acceptance. Do not bypass any security warning.
- Use the separately labeled synthetic acceptance workspace, record session ID, exact source commit and approved executable hash. Use synthetic firm, two clients and projects, distinct employee and reviewer accounts, test-only agreement PDFs and no actual external transactions. Record dates, contractual caps, rates, approved time and expense amounts before comparing reports.
- Desktop steps use available controls; service/fault injection, accounting, migration and installer procedures require their corresponding controlled harness or qualified adapter. A missing capability is Blocked, not Pass. A source regression result alone is not a desktop result. Deferred scope must be explicitly accepted by owner; never erase a requirement.
- Keep individual test evidence, raw observations, reviewer and actual results privately. Record test ID, environment, artifact SHA256, commit, acceptance session, fixture ID, observed result, evidence reference, date, result (Pass/Fail/Blocked/Not run), and defect ID if applicable. A Pass requires all original acceptance invariants; partial evidence cannot close the original row.

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

## Schema 47 historical archive supplement (seven additional required cases)

Use the version-2 evidence register: all 42 original IDs, seven existing supplemental IDs, and the seven below (56 total). Initialize/validate with `--schema47`; a v1 manifest is not sufficient for schema 47. All seven remain **Not run** interactively. Source-level and prior-source upgrade results are supporting evidence only.

| ID | Procedure | Required observation |
|---|---|---|
| CRC-ARC-01 | In the approved isolated synthetic acceptance workspace, import reviewed synthetic CRM, open Historical Project Register before archive import; compare ordinary workspace and role grants. | Empty archive; import offered only in synthetic workspace with administrative and both view grants. |
| CRC-ARC-02 | Prepare fixture using the script below; preview correct four digests and files, then preview wrong-parent census with its own matching digest; change one hash or selected file after valid preview. | Valid preview two records, one duplicate number group; wrong parent denied; changed input invalidates commit. No writes. |
| CRC-ARC-03 | Preview valid package; attempt missing note/checkbox, cancel final dialog, then explicitly review and commit. Reopen and preview exact repeat. | Cancel does not write; one sealed batch; exact repeat not offered for commit; original IDs and whitespace retained; no operational project created. |
| CRC-ARC-04 | Search by P01, OLD-001, title, site, nonexistent term; filter legacy client 001 then 002; inspect detail and clear filters. | Accurate counts and client selection, both duplicate numbers retained, inactive client history visible, Work authorized: No, no editing. |
| CRC-ARC-05 | With separate staff session, grant only archive view, then client view, then revoke import/view independently using approved test controls. Attempt import as non-admin. | Service denies missing grant; restricted evidence absent for staff; no import. Open register clears restricted data at activation or next guard tick, within expected 20s plus dispatcher availability. |
| CRC-ARC-06 | With modal confirmation open, revoke administrator import grant using separate controlled session; confirm after guard invalidates review. Repeat with window closed/cancelled. | No import despite prior preview; no stale success or retained reviewed bytes accessible through UI. |
| CRC-ARC-07 | Reopen sealed archive in isolated recovery copy; exercise missing/wrong audit evidence through native disposable fault harness, verify desktop failure state and no data shown. | Valid copy preserves history, no current work authority; damaged copy refused or register fails closed. No replacement of live database. |

### Fixed synthetic input preparation

From the reviewed source checkout, with Python 3.12, use the following on a trusted private local machine. The destination must not yet exist and its parent must exist outside checkout.

```
python tools/migration/prepare_synthetic_archive_desktop.py "C:\\PrivateAcceptance\\archive-fixture" --confirm-synthetic
```

The six fixed files include a synthetic provenance marker (not a real Access file), CRM, project transfer, valid census, wrong-parent census and manifest. Use manifest hashes exactly; for the negative case use its separately listed digest. The generator takes no source data. It does not prove independent real-world census provenance, desktop execution or source authenticity. Keep all files private; no need to execute the quarantined binary to prepare inputs. Actual interactive steps remain prohibited until exact-artifact independent security approval and an isolated acceptance environment are available.

Initialize a new **private** manifest only after the exact approved artifact digest and source revision are available:

```
python tools/release/validate_acceptance_evidence.py PRIVATE_NEW_MANIFEST.json --commit FULL_COMMIT --artifact-sha256 FULL_SHA256 --initialize --schema47
```

Revalidate with the same options except `--initialize`. Do not overwrite v1 evidence or copy prior Pass statuses without exact-revision evidence. Structure validation never authorizes release.


## Milestone 08 candidate-specific retest instructions

Retain all 56 existing scenario IDs; the following are additional observations within their existing rows, not new or passed cases. The approved exact artifact and independent reviewer authorization are still prerequisites.

| Existing ID | Added observation |
|---|---|
| SEC-03 | Open project reports with incomplete financial grants and with full grants; verify navigation and tabs agree with service denial. Revoke a grant with a report visible and verify session lock and removal of restricted display. |
| SEC-05 | Start a delayed synthetic accounting diagnostic, sign out and sign in as the same actor; confirm prior-session response cannot appear. Test separately with the native loopback harness. |
| PRJ-03 | On a pending, active and operationally closed project, select planned, started, cancelled and discontinued tasks; verify action enablement and historical evidence visibility. Change lifecycle in a second session and refresh. |
| CRC-LC-01 | Enter closure reason and refresh unchanged state; verify reason remains. Close/reopen in a second session, refresh and verify stale reason cleared. Race two reopening attempts in the native harness. |
| REP-01 | Reconcile approved project minutes with date-filtered employee detail and financial report; include overhead separately, missing rates, rejected and invalidated submissions, and held invoice drafts as a subset, not additional value. |
| REL-01 | Require exact source checkout/tree, SDK and published runtime identities, inventory digest and entry-point hash for the approved package. Verify installer signature and independent authorization separately; runner-local inventory is not install evidence. |
| REL-02 | Verify restricted staging ACL before raw writes, destination collision refusal, preservation of existing target, no live replacement, accounting hold, and no residual staging after normal completion. Exercise interrupted installation only in an independently approved isolated environment. |

Do not execute or redistribute the original blocked archive. No installer or real desktop acceptance has been executed by this update. Record observed results privately against the exact approved candidate, not a predecessor's CI results.


## Milestone 09 — simplicity first

Owner direction: keep the software simple, straightforward, intuitive and user-friendly. Freeze discretionary feature expansion. Use familiar engineering-office terms, sensible defaults, role-relevant navigation, one obvious primary action, and technical details on demand. Keep validation, audit trails, concurrency checks, accounting authority and conflict safeguards in services; do not require staff to manage hashes, revision tokens or technical state. Keep confirmation for consequential decisions, not repeated confirmation of ordinary file saves. Do not simplify by bypassing agreement/start authorization, permission, QA or accounting controls.

Add observations to existing UX-01, SEC-03, DOC-01 and REP-01: ordinary users see only permitted navigation; authorized administrative tools remain accessible in the collapsed group; sign-out resets navigation; document list and review history hide technical columns by default but reveal them on request; report export opens the save dialog directly, with fresh-snapshot and protected-storage notice visible, and still denies stale filters, unauthorized access and overwrites. These are acceptance criteria, not executed WPF results.

Future target: consolidate project setup, folder selection and Send Outs around one project context. Automatic creation outside the dedicated synthetic workspace and actual issuance remain blocked; no schema activation or bypass of security review. Do not add modules or confirmation dialogs unless tied to a documented requirement and measurable user benefit.


### Optional formal document management — owner scope decision

Default workflow: ordinary drawings, calculations, reference documents and outgoing files remain in the firm's existing project folders. No routine duplicate registration, revision tracking, transmittal logging or package preparation is required by the practice manager. The standard folder template remains independently accessible; automated folder creation is still synthetic-only. Formal document management is an optional, collapsed project tool, available to authorized staff; this is a UI default, not a new persistent feature toggle. Preserve existing records and audit history.

Do not interpret the optional register as a waiver of signed agreement or required conflict declaration evidence. Signed agreements retain their separate review/start control; a required declaration can now be attached directly from the declaration screen and reviewed there without opening the general register. Attachment and declaration review remain separate recorded actions; a saved PDF does not establish clearance. No actual legal or technical issuance automation.

UX-01 / DOC-01 / ENG-04 through ENG-06: demonstrate normal project setup and authorized start without ordinary production file registration; select optional register only when needed; verify existing records remain accessible under current grants; add a restricted declaration directly and verify no clearance until recorded review and fresh screening. Preserve all original acceptance rows, including optional-module tests when that module is in scope. No interactive acceptance executed.
