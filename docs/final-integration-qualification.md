# Final integration qualification — held

Baseline PR #86 at 09ade8410aee28ae3807e61f8d16a623fa585a4d: 2728 native checks and 168 Python tests, zero compiler warnings/errors, synthetic prior upgrade passed; DAO failed, source security was in progress at observation. Latest revision requires fresh CI.

## Authenticity and handoff

Read-only Windows Authenticode candidate uses WinVerifyTrust generic verify v2, file handle and path, whole-chain revocation checks using cached data only, no UI, lifetime signing policy and MD2/MD4 rejection. It reads the actual verified provider leaf certificate and compares SHA-256 to an independently provisioned certificate pin, additionally hashing all bytes before and after verification. The same-process sealed handoff requires the exact release evidence, package, coordinator and helper hashes and a common pinned publisher. There is no trust anchor, certificate procurement, signed release or production approval. Negative synthetic tests do not prove the positive signed-file path, current online revocation, timestamp policy, multiple signatures, or execution-time file identity. The native API and provider structure layout require independent review and positive Windows qualification. No executable installer may be built or distributed while held. See Microsoft Learn WinVerifyTrust and WINTRUST_DATA API references.

## One acceptance register

1. Exact-head automated build, native/Python regression and synthetic prior upgrade; document legacy DAO failure or explicitly approve excluding legacy import.
2. Independently review original warning, source and precise proposed artifact, approve controlled isolated qualification before any installer execution. No unsigned or unapproved artifact execution; no security-control bypass.
3. Positive signed-artifact and negative trust qualification, pin custody/rotation, protected extraction and cross-process handoff, interrupted reservation review.
4. Synthetic clean Windows machine: install, launch, upgrade, interruption cases, uninstall, retained business data and registration readback. Verify exact recovery evidence and schema-specific workspace identity; no automatic live restore or downgrade.
5. Consolidated interactive desktop acceptance: representative Administrator and nonfinancial role, project and folder workflows, time/expenses, invoicing and sandbox-only accounting, backup and recovery. Capture actual observed evidence; no production credentials or business data.
6. Independently reviewed exact release, acceptance evidence, explicit release authorization and documented support/rollback procedure. Until then coordinator ApprovedRelease returns null and NSIS compile hold is unchanged.

## Integrated review boundary

The same-process InstallerIntegrationReview composes detached signature and artifact verification, sealed extraction review, executable publisher verification and synthetic recovery snapshot binding. Upgrade requires recovery evidence; fresh installation refuses it; removal is a separate lifecycle. The review repeats snapshot and envelope checks but does not provide a launch lease across process boundaries, live backup freshness, independently authenticated recovery approval, or production authorization. Generic rehearsal rejects nonzero SQLite application IDs; schema-specific workspace identity review is separately tested and ordinary startup remains at schema 47. Seven additional scope/refusal assertions added. Positive end-to-end signed qualification remains outstanding. The production hold is unchanged.

## Independent process and interruption qualification

Revision 80b269d7a08cf42250e25c1ac7c12965a9d5c329 passed 2785 native checks and 168 Python tests; Windows run 46168516655, source security 46168516736 and prior-version upgrade 46168516616 passed. DAO 46168516643 remained blocked by provider availability. The synthetic one-use named pipe checks the expected peer PID and nonce response while the parent retains its reservation; it cannot authorize execution. The next candidate bounds the whole channel lifetime from creation, makes cancellation/disconnection fail closed, and tests wrong response, disconnect, cancellation, changed evidence and non-reuse. No actual signed installer, helper execution, live database, trust provisioning or release approval. Positive signed-artifact qualification and independent clearance remain mandatory.

## Final readiness record

Exact revision 11564af08f5a12bc04f1bbc82bd96c6a61bd9066: 2792 native and 168 Python checks, zero compiler warnings/errors; Windows 46169061381, prior upgrade 46169061446 and source security 46169061392 passed. DAO 46169061480 blocked. Remaining work is real qualification, not further simulation for its own sake.

New private structural gate validator: tools/release/validate_release_gate_record.py. It reconciles all eight gates in single-user-release-gates.md and all 56 original/supplemental acceptance scenarios, binds full commit and independently retained package/coordinator/uninstaller/desktop/inventory digests, rejects missing or duplicate gates, refuses a recorded desktop pass with incomplete scenarios, and rejects a final pass with outstanding gates. It never authenticates evidence or authorizes release, even if all records say Pass. No scope waivers supported; owner scope decisions must be independently reviewed without deleting requirements. Use only after exact candidate identifiers exist and keep inputs outside the checkout. Example command (replace placeholders with independently retained full lowercase identifiers):

    python tools/release/validate_release_gate_record.py PRIVATE/gates.json PRIVATE/acceptance.json --commit COMMIT --packageSha256 PACKAGE --coordinatorSha256 COORDINATOR --uninstallerSha256 UNINSTALLER --desktopSha256 DESKTOP --inventorySha256 INVENTORY --initialize

The acceptance manifest must already exist in v2 format; initialize never overwrites the gate record. The current production security hold is unchanged.
