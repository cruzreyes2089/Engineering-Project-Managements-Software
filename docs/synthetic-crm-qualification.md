# Synthetic CRM qualification harness

Source-only, opt-in candidate. No actual CRC records, Access source file, or live SQLite database is accepted as input. The harness creates two disposable ACCDB fixtures using the locally installed DAO engine, in separate newly created temporary directories. Do not execute an unapproved held application build or bypass Windows execution policy. Use a trusted isolated Windows machine and a matching Access/DAO engine; Python 3.12 is required.

After source review and authorization to run the scripts in the isolated environment, execute:

    python tools/migration/qualify_synthetic_crm.py --confirm-synthetic --dao-bitness 64

Use 32 instead for a 32-bit DAO engine. The argument selects Windows PowerShell bitness, not the operating system alone. No downloaded provider or elevation is performed. Fixture DDL uses the Access 12.0 file format and DAO's read/write operations only for the newly created synthetic database. See Microsoft's DAO CreateDatabase and DatabaseTypeEnum documentation:

- https://learn.microsoft.com/en-us/office/client-developer/access/desktop-database-reference/dbengine-createdatabase-method-dao
- https://learn.microsoft.com/en-us/office/client-developer/access/desktop-database-reference/databasetypeenum-enumeration-dao

## Checks specified

- Populated and empty-contact cases, with a declared parent relationship.
- Original text IDs and postal codes preserve leading zeros; contact integer IDs normalize to text.
- Explicit integer client-type and boolean status/primary conversions.
- Independent source identity projection and separate mapped-field projection.
- Fixed expected value comparison of every normalized CRM field, independent of row order.
- Ignored source fields and unrelated table are outside the mapped projections.
- Source SHA-256 remains unchanged after inventory, extraction, and rejected operations.
- Existing fixtures and output files cannot be overwritten; stale plan and source hashes are rejected without output.
- Substituted source identity is rejected.
- Four additional Python unit tests cover fixture expectations and field comparison without DAO.

The harness captures child-process output and returns only check labels or a generic failure. It uses the trusted local temp directory; failure or process interruption may leave synthetic files, and cleanup failure is not a pass. It does not automatically remove existing external files. A successful harness run would qualify only these particular synthetic Access extraction and conversion scenarios, not live source integrity, hidden/linked-table adversarial fixtures, all DAO providers, actual Access schemas, native SQLite import, field reconciliation after native normalization, or security release approval.

## Verification

New fixture generator a684fc911e2500bf29d682daea1fdd9fb977ecae; harness b8318150da87b29a8fece38fdbdbcae5813755d5; offline tests bb67f7714770663f3b572715abe912adb2353dd5; CI parser step c7d55f5016acc976dd1261ad2341086b43c51490; stale source regression a41a103896e7e4d0ab43697d69bbc5689b094956. The CI workflow parses both PowerShell scripts and discovers the Python tests; it does not execute the opt-in DAO harness. Actual execution and exact-revision CI remain unverified. No new passing tests are claimed.

Next: qualify the exact generated transfer in an isolated native smoke-test harness, verifying all stored values, mapping/numbering, permissions, fault-injected rollback and exact repeat-import behavior. Existing native legacy CRM tests use separate handcrafted synthetic packages; they do not establish the end-to-end DAO path. No production import, security-hold change or main-branch merge.
