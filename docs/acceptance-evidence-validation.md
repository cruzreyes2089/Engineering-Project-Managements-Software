# Private acceptance evidence workflow

The acceptance evidence helper is a source-only qualification tool. It does not install, launch, sign, publish or approve the application, and it does not modify a database. The original 42 acceptance scenarios and seven supplemental scenarios retain their exact identities. No scenario has been accepted by this tool.

## Procedure

After independent security approval for the intended candidate, retain the independently verified full 40-character source commit and full 64-character SHA-256 of the exact executable. Keep a private, access-controlled evidence directory outside the repository. Do not use production data, real credentials, or private customer records. Do not commit evidence, screenshots, actual manifests or secrets.

With Python 3.12, run the following from the source checkout, substituting an existing private directory and the independently recorded candidate identifiers:

    python tools/release/validate_acceptance_evidence.py "PRIVATE_DIRECTORY/acceptance.json" --commit FULL_COMMIT --artifact-sha256 FULL_SHA256 --initialize

This creates a new file containing all 49 scenarios as Not run and refuses to overwrite any existing file. It does not check executable bytes or establish the provenance of the supplied identifiers. Keep the prior manifest and raw observations as private history before editing a working copy or recording a retest. If creation is interrupted, treat a partial file as invalid; preserve prior evidence.

Update individual results only from actual observed tests using the original acceptance register and the desktop execution plan:

- Not run: testId and status only.
- Blocked: testId, status and reason.
- Pass: testId, status, reviewer, date (YYYY-MM-DD), environment, session (32 lowercase hex), fixture, observed and evidenceRef.
- Fail: the Pass fields plus defectId.

For WPF tests use the real synthetic acceptance session ID. For service-only or other controlled harness tests, use a separately recorded unique synthetic evidence-session ID linked to the private execution log; do not imply that a desktop session occurred. A partial result cannot pass the entire scenario. Reference private evidence rather than copying raw logs or confidential content. No automatic waiver or exclusion is supported; maintain any explicit owner-approved scope decision separately.

Revalidate with the same command without --initialize. Exit code 0 means only that the manifest structure is valid; exit code 2 means rejection. The report contains counts only, always sets evidenceAuthenticityVerified and releaseAuthorized to false, and never interprets recorded Pass values as independent proof. An all-pass manifest cannot clear the security hold.

The CLI checks that the resolved evidence path is outside the current source checkout, but it is not an OS-level data-loss prevention or confidentiality boundary. Use a trusted private directory and appropriate access controls. The helper does not authenticate a reviewer, inspect evidence references, validate historical retests, bind the entire distributed package, or qualify the installer.

## Verification record

Implementation candidate 9b25eb0ee1475186764bff718382c0e9e00ba368; validator Git blob 7aa8feeb7fd3e652262cf298da210e406d300114. An independent local Python harness executed 24 successful assertions against byte-identical validator source (Git blob verified) and a synthetic CSV with the 42 original identifiers. The harness covered candidate binding, initialization, overwrite refusal, malformed evidence, exact coverage, invalid dates, failure recording and all-pass denial of release authority. This is not the full repository test suite. Windows combined CI and interactive acceptance remain unverified; no security hold change. Repository regression tests are included in the existing Python unittest discovery path.
