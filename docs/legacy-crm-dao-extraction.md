# Controlled Access DAO extraction candidate

Source-only development. Requires Windows PowerShell with bitness matching the installed Access DAO engine. Actual DAO behavior, PowerShell 5.1 compatibility and the integrated Windows build have not been qualified. Synthetic closed local copies only until independent approval and successful qualification; never open the original or production database. No deployment or security-hold change.

## Private workflow

Keep all files in a private access-controlled directory outside the source checkout. Begin with a trusted closed synthetic Access database copy, metadata inventory, and independently reviewed mapping. Run the existing mapping preflight, then:

    python tools/migration/prepare_crm_extraction_plan.py INVENTORY MAPPING NEW_PLAN

Review the projection, source hash, inventory hash, mapping hash and exact plan digest independently. The plan includes only the selected local table names, mapped columns, DAO scalar types and identity-column references. The PowerShell candidate requires that exact plan SHA-256; it is not an authentication signature. Never substitute an arbitrary hand-written plan.

Run the identity-only query first, separately from conversion, retain its digest and bytes privately, then run the row query:

    .\tools\migration\Export-LegacyCrmSnapshot.ps1 -DatabaseCopy COPY -PlanFile PLAN -ExpectedPlanSha256 DIGEST -Mode Identity -OutputFile NEW_IDENTITIES.json -ConfirmTrustedSyntheticCopy
    .\tools\migration\Export-LegacyCrmSnapshot.ps1 -DatabaseCopy COPY -PlanFile PLAN -ExpectedPlanSha256 DIGEST -Mode Rows -OutputFile NEW_ROWS.json -ConfirmTrustedSyntheticCopy

The script requests an exclusive read-only DAO database opening and read-only snapshot recordsets, checks actual local table/column metadata, rejects linked and hidden tables, restricts projection identifiers, bounds row and byte counts and rechecks source hash after closing DAO. No caller-supplied SQL, Access macros, database repair, password handling or write queries. File checks are not a sandbox; use a trusted isolated directory without other writers. A before/after hash cannot exclude a change-and-restore race. Exclusive DAO opening, locking behavior, COM scalar marshaling, cleanup and source integrity require real Windows qualification. No output file is created before extraction and validation complete, but interrupted disk writes can leave a partial new file; preserve source and reject incomplete outputs.

The separate identity snapshot has format CRC-Access-CRM-Raw-Identity-v1, source/inventory/mapping digests and clients/contacts arrays of objects with legacyId and, for contacts, legacyClientId. Scalar IDs retain DAO values until typed normalization. Row snapshot uses the previously documented CRC-Access-CRM-Rows-v1 format. Explicit mapped nulls are retained in extraction, rejected by conversion until reviewed null semantics exist.

Prepare the independently reviewed enum mapping and run the existing conversion tool, then:

    python tools/migration/bind_crm_source_census.py INVENTORY MAPPING IDENTITIES TRANSFER NEW_CENSUS

The binder converts the independent identity values using the mapped DAO types, compares exact identities and parent relationships to the transfer, and writes a census bound to its exact bytes. Run the existing reconciliation utility again. None of these stages establishes source authenticity or approves native import. Preserve all input bytes and hashes privately, including the identity export. Review all field values and native preview before any separately authorized migration.

## Qualification gates

- Verify PowerShell parser and Python regressions at exact combined source revision.
- On a trusted isolated Windows environment with matching DAO bitness, create a disposable synthetic Access fixture with local tables, a declared relationship, leading-zero text IDs, integer IDs, explicit booleans, inactive records, and deliberately unmapped, hidden and linked data.
- Verify separate identity-only and row projections, actual scalar types, empty contact arrays, source SHA before/after, no linked reads, no unintended columns, and normal COM cleanup.
- Reject stale plan/source hash, locked copy, linked selected table, malformed identifier, changed column type, duplicate/orphan identity, unsupported scalar and oversized output. Verify existing files are not overwritten and failed validation produces no new output.
- Complete conversion, independent census binding, native preview, transactional synthetic import, rollback and repeat-import tests. Compare counts, every selected field and historical identity mapping. Actual firm records remain out of scope until explicitly authorized.

## Source status

Plan 9fe5dd4956c0acda65aa78701e8f157c889a6668; DAO candidate 34c399ba8f2f06184c8fdae041ba4d6fe13a321c; census binder 4c7c38d5dec073bc98e7a63c98f7435a150a9702; five Python regression methods 148d5f82dfdf723c9df785f4de8c3183ad79b376; Windows parser step 2ad689bc3aedf6746fe65e9e04ee04c2910468fa. These are committed candidates, not verified passing tests. No actual Access data, database schema increment or release authorization.

## Integration audit and privacy hardening

- Inventory revision 554a32156ca82ae9c374227e4afdd8c9807730c0 requires private fixed-drive paths outside the checkout, checks ancestor reparse points, rejects alternate data streams and requests exclusive read-only DAO opening. Extraction revision 147a87cf4f6747ae8a386140c7c0c10fe14d5331 also rejects alternate data streams and projects a shared contact primary/foreign key only once, while preserving both identity roles. This supports a declared one-to-one source relationship without manufacturing IDs.
- Regression revision 0ec3f18beed80532e6df76418693f23c2af14946 includes the shared-key metadata and census case.
- Local Python verification: five migration modules and four test files were transcribed into an isolated directory and checked against their exact Git blob SHA-1 values before execution. Mapping 7/7, row conversion 8/8, extraction-plan/census binding 6/6, identity reconciliation 8/8: **29/29 passed** using Python unittest discovery. These were exact source-level tests on synthetic metadata and rows, not DAO tests.
- The new PowerShell path controls and shared-key recordset execution still require Windows qualification. Add controlled cases for checkout output, ancestor junctions, alternate data streams, locked sources, and a shared-key synthetic Access relationship. Path checks are defense in depth, not a sandbox or proof against concurrent replacement or filesystem aliases. Keep inputs and outputs in a separately access-controlled local directory.
- No Windows PowerShell, DAO or .NET runtime was available for this local check. No combined build, actual source inventory or live database import is certified by this result; schema and release hold unchanged.
