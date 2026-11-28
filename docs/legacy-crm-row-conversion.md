# Controlled offline legacy CRM conversion

Source-only conversion utility: tools/migration/convert_crm_rows.py. No DAO queries, direct extraction, database mutation, release authorization or production import. This is an intermediate stage between reviewed metadata mapping and native transfer preview. The actual Access row-extraction adapter and independent source census remain unqualified.

## Inputs and explicit review

Keep all inputs and output in a trusted access-controlled private directory outside the checkout. Never commit actual records, schemas or enum review files. Use the existing inventory and mapping formats. Four input files are required:

1. Metadata-only Access inventory.
2. Reviewed CRM field mapping bound to that inventory.
3. Row snapshot: exact root keys format (CRC-Access-CRM-Rows-v1), sourceSha256 (lowercase source file digest), inventorySha256, mappingSha256 (lowercase exact byte digests), clients and contacts. Each array row is an object containing exactly the mapped nonblank source column names for that entity. Values must be explicit JSON scalars appropriate to the mapped DAO type. No unselected columns, SQL, credentials or unrelated data. A qualified adapter must obtain this snapshot from the unchanged source.
4. Enum review: exact root keys format (CRC-Access-CRM-Enum-Review-v1), sourceSha256, inventorySha256, mappingSha256, rowsSha256 (lowercase exact row snapshot byte digest), type and status. Each enum array contains objects with exactly source and target keys; source must have the original DAO scalar type and exact value. Targets must be supported native client types or Active/Inactive. No default or case-insensitive guessing.

Text IDs preserve exact spelling and leading zeros; integer IDs are converted from JSON integers only, within the DAO type bounds. Boolean values require JSON true/false, not numeric coercion. Mapped nulls are intentionally rejected in this first candidate; a future explicit null policy requires reviewed source semantics. Blank rules already permitted by metadata mapping produce an empty string. Ordinary mapped text is retained unchanged in the transfer, with native trimming, lengths, names and phone validation still required. Enum translations do not prove source meaning.

## Execution

From the source checkout with Python 3.12:

    python tools/migration/convert_crm_rows.py PRIVATE_INVENTORY PRIVATE_MAPPING PRIVATE_ROWS PRIVATE_ENUM_REVIEW NEW_PRIVATE_TRANSFER

All five paths must be distinct and outside the checkout. The utility validates the entire snapshot before creating the output; invalid input creates no transfer. Output is created exclusively and cannot overwrite an existing file. An interrupted or failed disk write can leave an incomplete new file; treat it as invalid, preserve the source and inspect privately before retrying with a new path. The CLI prints only counts and qualification flags on success. On field conversion failure it may print the entity and 1-based row ordinal; relationship or package failures use a generic rejection. It never prints raw private values. Exit 2 indicates rejection, not approval to omit a record.

Next independently reconcile identities with the existing source census utility and review all value transformations, native validation, backup, rollback and exact transfer hash. No partial-record import, automatic deletion, or silent deduplication. This stage does not authenticate the source or compare raw values to the actual Access database.

## Verification status

Test-first commit 5bf8ad3ddc51631966d2dbaad7b3c33eb182f79e; implementation 35e5ca321fe43330fff0ee65de94ccaca19cadc6; discovery correction 2148499bbf5005728f1cf89f0384a7fa7a7a86a6; safe rejection diagnostic 3bdfe03082d29765a8c183843717773c6c41eaf2; regression extension e9017b1fcc4bac4dac554a616a6d370efe30a656. Eight test methods are committed and included by the existing unittest discovery workflow. Exact-revision CI status was not returned by the available connector and direct local source download failed due to DNS/network restrictions; no new test pass is claimed. No schema or security hold change.
