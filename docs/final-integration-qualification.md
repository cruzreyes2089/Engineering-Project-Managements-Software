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
