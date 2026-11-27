# Synthetic CRM qualification harness

Source-only, opt-in candidate. No actual CRC records, Access source file, or live SQLite database is accepted as input. The harness creates three disposable ACCDB fixtures using the locally installed DAO engine, in separate newly created temporary directories. Do not execute an unapproved held application build or bypass Windows execution policy. Use a trusted isolated Windows machine and a matching Access/DAO engine; Python 3.12 is required.

After source review and authorization to run the scripts in the isolated environment, execute:

    python tools/migration/qualify_synthetic_crm.py --confirm-synthetic --dao-bitness 64

Use 32 instead for a 32-bit DAO engine. The argument selects Windows PowerShell bitness, not the operating system alone. No downloaded provider or elevation is performed. Fixture DDL uses the Access 12.0 file format and DAO's read/write operations only for the newly created synthetic database. See Microsoft's DAO CreateDatabase and DatabaseTypeEnum documentation:

- https://learn.microsoft.com/en-us/office/client-developer/access/desktop-database-reference/dbengine-createdatabase-method-dao
- https://learn.microsoft.com/en-us/office/client-developer/access/desktop-database-reference/databasetypeenum-enumeration-dao

## Checks specified

- Populated, empty-contact and shared-contact-key cases, with a declared parent relationship. The shared-key case has a text primary key which is also its foreign key; both identity roles preserve leading zeros.
- Original text IDs and postal codes preserve leading zeros; contact integer IDs normalize to text.
- Explicit integer client-type and boolean status/primary conversions.
- Independent source identity projection and separate mapped-field projection.
- Fixed expected value comparison of every normalized CRM field, independent of row order.
- Ignored source fields and unrelated table are outside the mapped projections.
- Source SHA-256 remains unchanged after inventory, extraction, and rejected operations.
- Existing fixtures and output files cannot be overwritten; stale plan and source hashes are rejected without output.
- Substituted source identity is rejected.
- Nine Python unit tests cover fixture expectations, exact field and scalar comparison, shared-key projection and native-mode limits without DAO.

The harness captures child-process output and returns only check labels or a generic failure. It uses the trusted local temp directory; failure or process interruption may leave synthetic files, and cleanup failure is not a pass. It does not automatically remove existing external files. A successful harness run would qualify only these particular synthetic Access extraction and conversion scenarios, not live source integrity, hidden/linked-table adversarial fixtures, all DAO providers, actual Access schemas, native SQLite import, field reconciliation after native normalization, or security release approval.

## Verification

New fixture generator a684fc911e2500bf29d682daea1fdd9fb977ecae; harness b8318150da87b29a8fece38fdbdbcae5813755d5; offline tests bb67f7714770663f3b572715abe912adb2353dd5; CI parser step c7d55f5016acc976dd1261ad2341086b43c51490; stale source regression a41a103896e7e4d0ab43697d69bbc5689b094956. The CI workflow parses both PowerShell scripts and discovers the Python tests; it does not execute the opt-in DAO harness. Actual Windows execution and exact-revision CI remain unverified; see the subsequent offline verification below.

Next: qualify the exact generated transfer in an isolated native smoke-test harness, verifying all stored values, mapping/numbering, permissions, fault-injected rollback and exact repeat-import behavior. Existing native legacy CRM tests use separate handcrafted synthetic packages; they do not establish the end-to-end DAO path. No production import, security-hold change or main-branch merge.

## Extended negative-path and shared-key qualification

Fixture generator 2e2096c5f771f5967d31e06aa145c4e375dd5d6c; qualification harness 636a7f99fc631a449d45fae0b57cef9181223707; fixture tests 217153623d7c6314e170684b44b2a5bd458a67e3.

Each of the three cases now checks both inventory and extraction refusal for exclusive-created .ldb and .laccdb sentinel files, randomized new output names under checkout, and nonexistent alternate data streams of a newly created synthetic base file. A successful run requires no rejected output and unchanged source bytes. The checkout test requires a private, writable, isolated source checkout with no concurrent writers; only its randomized synthetic output is eligible for cleanup if a regression creates it. NTFS is required for meaningful alternate-stream qualification. Marker tests do not establish behavior under a genuine competing DAO session. Ancestor junctions/reparse points, hidden/linked sources, actual concurrent access, and other real DAO behaviors still require separate qualification.

Optional native smoke runs are restricted to the populated and empty-contact fixtures, which have fixed native expectations. The shared-key case is DAO/conversion/census-only; the result explicitly lists native cases and reports sharedKeyNativeQualified=false. No production or actual CRC records.

Offline verification: 38/38 Python tests passed in an isolated local directory (7 mapping, 8 conversion, 6 extraction plan/binding, 8 reconciliation, 9 fixture qualification). The six migration module and five test file copies match their exact committed Git blob hashes, including harness 43a27ed58fe6884b9b82771de586e081c4b0edf0 and fixture tests 4c08c0447d44d9efc89e2d5a6caa1297ea069163. This verifies offline logic only; Windows-only subprocess checks and native smoke were not executed. No schema change or release authorization.
