# Independent release-review handoff — schema 47

Status: HOLD, development only. This document is a source-review handoff, not an independent security assessment, vendor disposition, installer certificate or release authorization.

## Exact evidence available

| Evidence | Identity / observed result |
|---|---|
| Original blocked archive | v0.4.12 build #28; reported archive SHA-256 c5840d6dcc5944483ef8b6a254a635dbf247b0155069f814256bc6a8a5ec9a14; repeated Chrome block. Not approved for execution or redistribution. |
| Prior scanner evidence | Exact archive and extracted members had zero detections with the same ClamAV engine; owner reported no current threats in Windows scan. No independent second-engine verdict or vendor clearance established. |
| Last fully verified development candidate at review | 623627b2e35605f88829a3189a6f3d623223801f; merge checkout d1f70d8 into 2244b6ab917003e7213092bf25c00907c1baacf0; run 46039131759, job 140642052134; 130 Python tests, 1844 native checks, zero warnings/errors. |
| Published inventory (that run only) | 409 files, 150538033 bytes; compact inventory SHA-256 4bfa68b27b44d59fca3443ecfca06b0b483a899adce8c24ca48004b4c6e10385; entry point SHA-256 997c60faede2223642faca99bf34ef1f1d534c6f545660b7e20ea634478b7f80; Authenticode NotSigned. Ephemeral runner output, not a release package. |
| Dependency observations (that run only) | Microsoft.Data.Sqlite/Core 10.0.15, SQLite 3.53.4, SQLitePCLRaw bundle/config/core/provider 3.0.5; smoke project also reports ProtectedData 10.0.15. No vulnerable packages reported by the scoped NuGet advisory commands. Not a malware verdict. |
| Actual prior-source synthetic upgrade | Run 46039457485 succeeded at candidate 4e743d536ada027ecd0e10d4f23e77cbea67600a; pinned prior df2cdf61c0ab02b200e34cdfdead304e96e74b2c. |
| Latest filesystem candidate | 4e743d536ada027ecd0e10d4f23e77cbea67600a; run 46039457464 schema-test stage passed, full regression not complete at review. Text-only pointer and journal fixtures, no physical installer. |
| DAO | Run 46039457536 failed before database extraction because provider activation unavailable. Not a successful migration qualification. |

CI records identify development builds only. Do not assign the old inventory or test results to a new build. The original warning concerned a different artifact. A newer unsigned build, source hash, good test count, or no-detection scan does not resolve that warning.

## Findings for independent reviewer

- Verified candidate Windows workflow blob 27dbcfb1d084ea5472f197d78b6663289f7c0b97: read-only contents permission, three pinned first-party actions, no upload step, publish only to runner-local development directory. This review did not enumerate all historical branches or workflows and cannot certify repository-wide distribution controls.
- SDK configuration uses latestFeature roll-forward from 10.0.100 and self-contained win-x64 publish. Deterministic compiler flag does not establish bitwise reproducibility, pinned complete dependency graph, pinned runtime pack or signed build provenance. Exact SDK/runtime, dependency resolution and final file inventory must be captured again for the reviewed release.
- Local development/acceptance paths are explicitly not a security sandbox (DevelopmentWorkspace.cs blob d9a15135ab51ac06b8c514cf185c6949bc621b66). Existing directory and marker checks do not establish whole-ancestor path safety, race-free access, ACLs or at-rest data protection. Production path not implemented. Review local threat model, credential storage, filesystem permissions, backup protection, archive import bounds, document export and external accounting trust boundaries.
- Static payload validator checks inventory structure and expected layout, not independent binary authenticity. Installation transaction, journal and text pointer tests are prototypes; no physical installation, Windows power-loss, disk-full, cross-process lock, signed-package or uninstall acceptance.

## Reviewer acceptance checklist

| ID | Required deliverable | Current state |
|---|---|---|
| SR-01 | Independent reviewer scope, identity, environment, exact reviewed source tree, date, findings and remediation/retest evidence | Not obtained |
| SR-02 | Original warning vendor/publisher case and disposition or independently documented resolution accepted by security reviewer; no override or repackaging | Blocked |
| SR-03 | Threat-model and source review of authentication, permissions, local storage and recovery, document/import paths, network/Manager adapter and secret handling | Pending independent review |
| SR-04 | Exact build provenance, dependency/runtime inventory, vulnerability findings and independently verified binary hashes; documented scanner scope, version and definitions | Development evidence only |
| SR-05 | Publisher identity, protected signing process, timestamp/signature chain, exact signed artifact hashes and approved distribution channel | Not established |
| SR-06 | Explicit written authorization of the exact artifact for isolated desktop/installer acceptance, not general release | Not obtained |
| SR-07 | 56-case desktop acceptance and real installation/upgrade/uninstall evidence, including interruption and data retention, with separately retained recovery witness | Not executed |
| SR-08 | Owner-approved accounting and migration scope, counsel-approved legal documents, explicit release decision after all required gates | Not obtained |

Keep actual reviewer reports, vendor correspondence, signatures, private evidence, account details and actual CRC records outside the public repository. Record only non-sensitive status and exact source/run references. Never send credentials or company data to public scanners. No claim that any independent review or appeal has been submitted. Do not download or execute the held artifact, bypass browser or endpoint protections, or distribute new binaries until the required review authorizes the exact artifact. No main merge.

## Work sequencing decision

Stop extending installer simulations merely to increase check counts. The next material work requires a reviewed release design and actual authorized test environment. In parallel, source-level security hardening and internal workflow gap fixes may continue if supported by concrete findings. Real DAO qualification needs an isolated Windows environment with a compatible installed provider. The initial internal-only release is a proposed scope decision, not approved: Manager remains authoritative and all unqualified financial posting, settlement and legacy financial migration remain excluded until separately accepted. Obtain owner decisions before removing any existing requirement.


## Milestone 08 checkpoint — 2026-11-27 Pacific

Last fully verified Windows candidate at this review: PR #22 head b1cee87ec9128b831763568aa01410a708f67386, run 46099647353, job 140838945025: 151 Python tests and 2280 native checks, zero compiler warnings/errors. Inventory: 409 files, 151087641 bytes, SHA-256 bf1a168cc63c9d36202ae7c6586b356b84feea34f9d858ad012e5f42440a81d4; unsigned. Prior-version upgrade run 46099647342 passed. PR #23 head 3d9e4762ad1941442c49e076df0027361cd5006b upgrade run 46099770667 passed; Windows build qualification still in progress at review. DAO run 46099770673 failed at provider probe before database access (job 140839333184). No actual extraction qualified.

The current repository branch listing was exhausted (53 branch names); no later schema-54 branch was identified by name. This is not proof that the source never existed. Do not recreate or activate missing schema changes without an exact source or reviewed migration lineage. Current recovered baseline is schema 47 with isolated 48/49 acceptance paths.

Next external prerequisites, not tasks that can be certified by another CI run: independent reviewer/vendor resolution of original browser warning and exact-artifact execution authorization; private clean Windows acceptance environment; compatible DAO provider on isolated machine if legacy import is required; approved encryption/recovery custody design and signing arrangement; owner-approved initial scope, counsel-approved documents. Keep all 56 acceptance rows and record exclusions explicitly rather than converting blocked rows to pass. See updated desktop plan for milestone 08 retest observations.

No binary distribution, no main merge, no production authorization. Source-only improvements do not resolve the original warning.


## Milestone 10 consolidated checkpoint

Verified PR #44 head 62bc2e5e1d75407f61c56f92fe169c8019452415: Windows run 46106347229, 2390 native and 151 Python checks, zero compiler warnings/errors. Upgrade run 46106347144 passed; DAO run 46106347141 failed. Prior integrated solo-practice regression (#40 run 46103482143) passed on ordinary schema 47, including reviewed agreement, conflict clearance, time and expense, fixed-fee draft, operational closeout, report reconciliation and isolated recovery. Those tests do not constitute interactive acceptance. PR #45 head eab7b688c1e43e3598af1d491e1e8acc0a9670b2: upgrade 46108011612 passed; Windows 46108011570 still running at review; DAO 46108011636 failed. Do not reuse earlier artifact hashes for either candidate.

Current source revision adds a startup check for exact restricted Windows directory DACLs on normal development and synthetic acceptance root/session folders. Missing or broadened access fails closed without changing an existing ACL, opening a database or falling back to a different workspace. New acceptance directory creation is refused under an unqualified acceptance root. This is a conservative point-in-time directory-layout check, not encryption, file-level ACL verification, a whole-machine effective-access check, or race-resistant filesystem isolation. An older development workspace with different permissions may now be refused; retain it unchanged for independent review and recovery, do not automatically rewrite its ACL. Windows test results for this new source change are pending.

### Actual release blockers

- Production data directory and folder automation are not qualified or enabled. Design and review the production storage boundary, encryption and recovery custody before activation; synthetic folder rehearsal is not production proof.
- No physical signed installer, verified clean-machine install/uninstall, independent security assessment or exact-artifact authorization. Do not extend simulation just for test counts. Preserve hold.
- Desktop acceptance remains 0/56. Required cases cannot be marked passed by CI. After independent exact-artifact approval, execute the actual desktop workflows and clean-machine installation in a private isolated Windows environment.
- Initial internal-only accounting/migration exclusions remain proposals, not owner-approved scope changes. No real accounting posting or actual Access migration qualification; final legal documents require counsel approval.
- Current recovered supported schema remains 47, with 48/49 isolated only; no reconstruction of missing schema 54 by assumption. No merge, binary distribution or release authorization.
