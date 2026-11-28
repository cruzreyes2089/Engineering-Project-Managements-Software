# CRC Practice Manager — deployment readiness checkpoint

Status: development only; security hold remains. Freeze scope. This checkpoint is not an authorization to install, execute a held artifact, merge, distribute, or process production data.

## Last complete qualified source at review

- PR #54 head df73f5e8183f09cfe3e9eb2a3d3f1bf69ac1623c; Windows run 46111889282, job 140877027181; 2,446 native checks, 157 Python tests, zero compiler warnings/errors.
- Actual checkout d4152c42749f7d77da4c30f492bc627d2d261ae6; tree eefe00596427e850b00c7ec6c1dbf2e420b1303c; SDK 10.0.404; win-x64 self-contained runtime packs 10.0.15.
- 409 published files, 151168857 bytes, same-run byte verification passed. Inventory SHA-256 1bdb54a498f5ddca2935c38d419767692d98e09bba7f6d17e7cda2c83ce711ea; entry point SHA-256 648c2f5200e85b2f314f524bb3a6f22b0b1c5eb150ff8cc661e6613ca6d93bc8. NotSigned; securityApproved=false; releaseAuthorized=false; independent verification=false. Runner-local evidence only, not a signed distributable.
- Prior-source synthetic upgrade 46111889241 passed. DAO 46111889305 failed. PR #55 upgrade 46112054571 passed; DAO run 46112054619, job 140877539289, explicitly reported both preinstalled 64-bit and 32-bit DAO activation unavailable. No actual Access extraction qualified.
- Latest subsequent source changes address staff/invoice refresh and synthetic folder setup dialog reliability. Their full exact-head qualification and interactive evidence must be recorded separately. Do not reuse the prior file hashes.

## Decisions and next deployment gates

| Priority | Actual requirement | Responsibility / prerequisite | Exit evidence |
|---|---|---|---|
| 1 | Freeze exact green source and release scope | Development and owner; initial internal-only scope is proposed, not approved | Exact candidate CI, accepted feature register and explicit exclusions; preserve all original 56 cases |
| 2 | Independent security review | Independent reviewer/vendor; original blocked artifact remains quarantined | Written warning disposition, threat model, source/dependency and exact artifact review, signing and controlled execution approval |
| 3 | Production storage | Owner and reviewer approve local account, encryption/recovery key custody, project root and backup destination design | Separate production identity, file/directory/sidecar access and recovery verified; no DEV fallback |
| 4 | Real installer | Reviewer-approved source design and signing process; independent exact artifact execution approval before interactive tests | Signed per-user installer; clean-machine install/upgrade/uninstall and data retention, interruptions and rollback tested |
| 5 | Interactive desktop acceptance | Private isolated Windows fixture, authorized exact artifact | Actual recorded results against existing 56-case register; 0/56 executed at this checkpoint |
| 6 | Accounting and migration scope | Owner/counsel review and explicit decision; if included, compatible private DAO/provider and independently reconciled evidence | No unqualified live posting, migration or assumed settlement; counsel-reviewed final documents |
| 7 | Production release | All required gates satisfied; owner release decision | Written release authorization and verified distribution path |

See independent-release-review-handoff.md, production-storage-boundary.md, installer-architecture-candidate.md, initial-internal-release-scope.md and desktop-release-acceptance-plan.md for supporting controls. No new simulation is a substitute for a physical installer or independent review. No confidential records, credentials or raw evidence in public repository. Active recovered schema 47 only; 48/49 remain isolated; schema 54 not recovered. No claim of full commercial or legacy parity.

## Stop condition

After the consolidated source passes, limit further changes to material defects found in actual evidence. Keep a single verified baseline and avoid creating additional feature branches solely to increase test totals. Obtain owner scope and independent reviewer decisions before activation, signing or execution beyond authorized synthetic CI. No assumption that a passing build authorizes production.
