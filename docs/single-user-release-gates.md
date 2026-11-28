# Single-user release gate plan

## Baseline and release boundary

Exact verified schema 46 and isolated desktop workspace commit 25122753f95a91239778135375c9df6ce3810620, Windows run 45951746285, 22 Python tests plus 1618 native checks, no compiler warnings/errors. The upgrade rehearsal remains a candidate. This plan prioritizes a usable single-user engineering practice application. Commercial/network deployment, multi-firm licensing, and shared database operation are separate phases. No production authorization or security-hold change.

## Gate order and evidence required

| Gate | Evidence required before passing | Status |
|---|---|---|
| G1 — Internal workflow freeze | Exact green source revision; confirm CRM, engagement, project authorization, setup, parties/conflict, work, QA, scope, closeout, rate and internal draft workflows; enumerate remaining known limitations; role-specific acceptance script | Source regression only; desktop acceptance outstanding |
| G2 — Accounting scope and reconciliation | Preserve all approved drafts and source allocations; explicit review evidence; separate issuance, receivable and settlement state only from qualified authoritative accounting evidence; no inferred payment or nonissuance | Synthetic-only; production qualification outstanding |
| G3 — Legacy data migration | Trusted metadata-only inventory from closed copy of Access database; explicit table/field mapping, source counts, historical ID/currency/relationship reconciliation, staged dry run and rejected rows; backup and rollback; source unchanged | Normalized CRM transfer tested; actual DAO/schema/mapping unqualified |
| G4 — Final legal and document acceptance | Owner/counsel-approved final agreement/proposal content, actual signed evidence and issuance/delivery controls, branding and document integrity; no invented approved terms | Review documents only; final templates and delivery outstanding |
| G5 — Clean-machine desktop acceptance | Perform legacy acceptance register scenarios on a clean isolated Windows environment, with synthetic fixtures, role-specific access and screen/CSV/report/print checks; record exact build, fixture, result and defect for each test | Specified, not executed |
| G6 — Recovery/upgrade | Clean installation without Access/Office dependency for core workflows; isolated backup/restore, existing supported schemas, interrupted upgrade and safe uninstall; verify data remains intact and accessible | Restore source checks exist; installer and field qualification outstanding |
| G7 — Independent release security | Resolve original dangerous-file block through publisher/vendor review; independent security assessment of source, dependencies, exact release artifact and signing/provenance; approved distribution path | HOLD; prior zero-detection scans do not establish false positive |
| G8 — Release decision | G1–G7 accepted at exact version, documented accounting limitations, support/recovery instructions, owner approval and appropriate legal/security review | Not ready |

## Accounting release choices

The internal workflow can be evaluated without actual Manager writes. Any initial internal-only release must be explicitly scoped: manual accounting remains in Manager, no automated issuing/posting/receipt/credit/retainer or duplicate AR/GL, no assertion of posted revenue or profit from draft values. This is not approval to release or bypass security. Full integrated release requires all intended financial capabilities independently qualified, including exact document/tax/due-date/line allocation, receipts, payment and retainer application/refund, credits and reversals, concurrency and timeout recovery, durable attempt witnesses after restore, and authoritative report reconciliation.

Prior operator-assisted PowerShell fixture successes do not qualify C# production posting. Preserve accepted fixtures and guards; never repeat their POSTs. Use new disposable synthetic fixtures only with explicit scope authorization. Current source comparison is restricted to CRC ACCOUNTING TEST and observed balances do not prove actual payment. Do not expand production provider access until evidence supports it.

## Work sequencing

1. Schema 46 and combined metadata preflight are verified; qualify the isolated upgrade rehearsal. Avoid adding further local accounting state simply to increase test counts. Use qualified provider observations to determine the next narrowly testable accounting milestone.
2. In parallel, prepare actual source mapping and follow the [desktop acceptance execution plan](desktop-release-acceptance-plan.md), without exposing private data in this public repository. The metadata-only Access inventory script has syntax qualification but no actual DAO acceptance. Do not claim import readiness from its existence.
3. Identify only necessary missing source work against the full legacy feature and acceptance registers. Keep original scenarios and record any supported scope change explicitly; do not erase deferred features.
4. Execute desktop and security gates before any distribution. No executable uploads, renamed/repacked artifacts to evade detection, or main-branch merge.

## Evidence discipline

Each gate records exact commit and run/build hashes, environment and synthetic fixture identity, reviewer, observed result, and unresolved limitations. Keep actual customer/project/financial data, credentials, raw responses and private screenshots out of the public repo. A passing regression is not real-data migration, desktop execution, production accounting, or independent security acceptance. No percentage is computed from passing check counts.

## Historical archive development update

Run 46057682471 at 641b9f00570d64b26d08f416265ae37ccc3022b0 qualified isolated schema 47 (83 Python, 1,702 ordinary native, 47 schema-46 archive and 58 schema-47 archive checks). Subsequent schema 47 integration and read-only register require full branch regression and desktop acceptance. G3 and G6 remain open; G7/G8 remain HOLD. No real-data import, production accounting or distribution approved.

## Schema 54 scope freeze and next acceptance sequence

Latest verified baseline at this update: 277fd84edacb8004ef93ace3d3f9c6aa4123258d, run 46064575362, 91 Python and 2,103 native checks, zero warnings/errors. See the updated verification matrix. Storage configuration and logical assignment are complete source-level development candidates; the separate read-only Windows observation needs exact revision qualification. Treat physical storage as a deferred capability rather than delay unrelated single-user workflow and migration qualification. Before any later physical activation require: (1) supported volume and dedicated root identity; (2) independent effective-access tests for actual Windows user/group roles and inherited ACLs; (3) safe handle-relative creation or equivalently qualified exclusive boundary, including hostile replacement tests; (4) no implicit adoption or relocation; (5) exact project binding, durable intent and outcome, crash recovery, content backup/restore qualification; (6) independent review and explicit activation. Network/cloud storage remains out of scope. No folder operations in production. G3 next: qualify exact synthetic DAO projection and identity census on the matching trusted Windows engine; offline contract regression does not substitute for execution. G5–G8 remain outstanding. G7 remains HOLD. Do not increase schema merely to add tests or report percentages from check counts.

## Installed-engine migration qualification

G3 now has a source-level CI gate that checks whether the hosted Windows runner has the matching preinstalled DAO COM engine and, if so, executes only the fixed synthetic historical/CRM extraction and native import/recovery fixtures. A missing engine is explicitly NotQualified. No actual Access copy is requested or used, no installation is attempted, and no real data migration or distribution is authorized. This is not clean-machine desktop acceptance or independent security review. Exact revision evidence pending.

## Consolidated current qualification baseline

Exact completed revision 7f8e1b9c89e0a221ab08e8002176b1d4cabd9a68, run 46088669251: 2304 native checks, 104 Python tests, zero desktop compilation warnings/errors; static Windows x64 deployment passed. Integrated three-account workflow and populated legacy SQLite upgrade/recovery included. DAO NoRegisteredEngine and Windows metadata handle rename exclusion remain explicitly unqualified. The subsequent invoice polish revision was still running at this checkpoint. Current schema 54, version 0.4.12. Original 42+7 desktop acceptance scenarios are unchanged. G1 source regression passed at the stated revision, not interactive acceptance; G2/G3/G4/G5/G6 remain outstanding in their actual release scope; G7 HOLD and G8 not ready. No claim of production posting, actual Access migration, physical folder activation, installer execution or security clearance. This batch adds exact recorded folder-base restoration, not a filesystem operation. Exact new revision regression pending. A saved storage binding remains historical and must be reviewed again after a changed selection.

## Owner-approved sequencing — 2026-11-27

See [Stage 1 release scope](stage-1-release-scope.md). Internal operational qualification is first; subsequent integration and commercial milestones remain required on the roadmap. No release approval or change to security HOLD.
