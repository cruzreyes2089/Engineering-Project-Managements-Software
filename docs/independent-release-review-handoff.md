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
