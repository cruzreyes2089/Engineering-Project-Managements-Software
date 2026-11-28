# Single-user release gate plan

**Scope clarification — 2026-11-28:** Owner clarified that the intended product is free software for public commercial use. This document's internal-only scope is historical/planning context for controlled qualification, not a restriction on the ultimate public release. See [public release design](public-release-design.md) for the public distribution requirements. Existing gates, security hold, acceptance cases and explicitly approved Access migration deferral remain in force; no implicit approval of other feature exclusions.

## Baseline and release boundary

Latest verified combined schema-47 candidate 35a56a22a58cec995fa78dfade07063b92fd4e48, Windows run 46037732194, 108 Python tests plus 1844 native checks, zero compiler warnings/errors. Actual prior-source synthetic upgrade run 46037732290 passed. The active schema-46 branch is unchanged; neither draft PR is merged. Subsequent installer static validation changes require their own CI evidence. This plan prioritizes a usable single-user engineering practice application. Commercial/network deployment, multi-firm licensing, and shared database operation are separate phases. No production authorization or security-hold change.

## Gate order and evidence required

| Gate | Evidence required before passing | Status |
|---|---|---|
| G1 — Internal workflow freeze | Exact green source revision; confirm CRM, engagement, project authorization, setup, parties/conflict, work, QA, scope, closeout, rate and internal draft workflows; enumerate remaining known limitations; role-specific acceptance script | Combined schema-47 regression passed at the baseline; interactive acceptance outstanding |
| G2 — Accounting scope and reconciliation | Preserve all approved drafts and source allocations; explicit review evidence; separate issuance, receivable and settlement state only from qualified authoritative accounting evidence; no inferred payment or nonissuance | Synthetic-only; production qualification outstanding |
| G3 — Legacy data migration | Trusted metadata-only inventory from closed copy of Access database; explicit table/field mapping, source counts, historical ID/currency/relationship reconciliation, staged dry run and rejected rows; backup and rollback; source unchanged | Normalized CRM and shared-key native transfer tested; hosted DAO run 46037732177 blocked before extraction; actual source mapping unqualified |
| G4 — Final legal and document acceptance | Owner/counsel-approved final agreement/proposal content, actual signed evidence and issuance/delivery controls, branding and document integrity; no invented approved terms | Review documents only; final templates and delivery outstanding |
| G5 — Clean-machine desktop acceptance | Perform legacy acceptance register scenarios on a clean isolated Windows environment, with synthetic fixtures, role-specific access and screen/CSV/report/print checks; record exact build, fixture, result and defect for each test | 56 procedures defined (v2 evidence); none executed interactively |
| G6 — Recovery/upgrade | Clean installation without Access/Office dependency for core workflows; isolated backup/restore, existing supported schemas, interrupted upgrade and safe uninstall; verify data remains intact and accessible | Synthetic native recovery and actual prior-source upgrade passed; installer architecture/static preflight candidate only, clean-machine installation and field qualification outstanding |
| G7 — Independent release security | Resolve original dangerous-file block through publisher/vendor review; independent security assessment of source, dependencies, exact release artifact and signing/provenance; approved distribution path | HOLD; prior zero-detection scans do not establish false positive |
| G8 — Release decision | G1–G7 accepted at exact version, documented accounting limitations, support/recovery instructions, owner approval and appropriate legal/security review | Not ready |

## Accounting release choices

The internal workflow can be evaluated without actual Manager writes. Any initial internal-only release must be explicitly scoped: manual accounting remains in Manager, no automated issuing/posting/receipt/credit/retainer or duplicate AR/GL, no assertion of posted revenue or profit from draft values. This is not approval to release or bypass security. Full integrated release requires all intended financial capabilities independently qualified, including exact document/tax/due-date/line allocation, receipts, payment and retainer application/refund, credits and reversals, concurrency and timeout recovery, durable attempt witnesses after restore, and authoritative report reconciliation.

Prior operator-assisted PowerShell fixture successes do not qualify C# production posting. Preserve accepted fixtures and guards; never repeat their POSTs. Use new disposable synthetic fixtures only with explicit scope authorization. Current source comparison is restricted to CRC ACCOUNTING TEST and observed balances do not prove actual payment. Do not expand production provider access until evidence supports it.

## Work sequencing

1. Schema 47 combined regression and actual prior-source synthetic upgrade are verified at the baseline; qualify the remaining installer, recovery and desktop gates. Avoid adding further local accounting state simply to increase test counts. Use qualified provider observations to determine the next narrowly testable accounting milestone.
2. In parallel, prepare actual source mapping and follow the [desktop acceptance execution plan](desktop-release-acceptance-plan.md), without exposing private data in this public repository. The metadata-only Access inventory and extraction scripts have syntax qualification but no actual DAO acceptance; both hosted provider bitness probes failed. Use an isolated machine with compatible preinstalled DAO when independently authorized. Do not claim import readiness from its existence.
3. Identify only necessary missing source work against the full legacy feature and acceptance registers. Keep original scenarios and record any supported scope change explicitly; do not erase deferred features.
4. Complete independent security review and exact-artifact approval before interactive execution or distribution; then execute desktop and clean-machine installer gates. No executable uploads, renamed/repacked artifacts to evade detection, or main-branch merge.

## Evidence discipline

Each gate records exact commit and run/build hashes, environment and synthetic fixture identity, reviewer, observed result, and unresolved limitations. Keep actual customer/project/financial data, credentials, raw responses and private screenshots out of the public repo. A passing regression is not real-data migration, desktop execution, production accounting, or independent security acceptance. No percentage is computed from passing check counts.
