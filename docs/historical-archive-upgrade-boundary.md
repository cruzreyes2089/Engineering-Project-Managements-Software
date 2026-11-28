# Schema 46-to-47 boundary and branch reconciliation

## Reconciliation

Two-parent candidate merge 6ac1b104f536157eebd85110720c9315d3442b28 combines candidate 208aae6f1c3691903d2bb80f8c2412ba47976968 and active schema-46 head df2cdf61c0ab02b200e34cdfdead304e96e74b2c. Compared both against ffdefe2c8c48d03bad33eae96f05ef841ba8f958. Eleven active-branch changes were imported by exact Git blob identity, including the two overlapping qualification files: the active harness retains the identical strict comparator and the active test file contains the complete candidate test file plus additional cases. Verified all eleven imported blob hashes and active ancestry before fast-forwarding only the candidate branch. Main and active schema-46 branch were not updated. The additional source-only archive service prototype is retained outside application compilation for historical verification; the actual schema-47 service remains authoritative on the candidate.

## Rehearsal and acceptance

884d32f75ccfab5ab52ac9cef9d7533964326f0a adds a separate local hash witness for the 46-to-47 boundary: all client and contact fields, CRM batch and mapping rows, sequences, roles, and existing permissions/grants other than the two new archive codes. No private values or witness are returned. Older schemas retain their stable baseline identity check; CrmAndExistingGrantsPreserved is not asserted for those older sources. This does not establish preservation of every application table or unchanged owner-approval state.

58a43d423ceea8fdf81e14eb14084ba6a60640b8 and 61e0533e7a54937a53d5f6be6913345a41fb64a4 add and wire native tests on a populated disposable constructed boundary fixture. It is created with the candidate, imports fixed synthetic CRM, then removes only the additive v47 objects and grants and resets user_version to 46. This is NOT a source produced by the exact prior v46 binary. Tests cover preserved CRM mappings and review, unchanged source digest, administrator-only grants, existing employee grants, and injected late grant insertion failure with schema, permission and version rollback. Separate actual prior-build fixture qualification remains mandatory.

## Offline SQL verification

dcf711a1ba40e326965e9a466f6d5e570e4e5459 adds three SQLite tests, executed locally against the migration's actual extracted SQL with a minimal synthetic parent schema. Passed 3/3: existing values and grants preserved, preexisting explicit denial not overwritten, and a late permission insert failure rolls back all new DDL, grants and schema version. Full local archive SQL group passed 20/20 (six prototype, six staged service, five audit query, three upgrade). The new test and full reconstructed migration file match exact Git blobs 8d6864b6b01c8e04a927acacc989a5735f5466ea and b71fb911018c7c2d3777735eee29976927f6320b; all four test file blobs and the prototype SQL blob also verified. The earlier audit/service SQL query excerpts remain excerpts, not full executed native service. Python explicitly manages the transaction; this does not verify Microsoft.Data.Sqlite transaction semantics, the real prior database, or native compilation.

## Remaining gates

Run exact combined Windows build/smoke, generate a populated fixture using the actual reviewed schema-46 binary, run native rehearsal against that closed fixture, verify full application data and approval-state invariants, qualify real synthetic DAO/desktop/recovery workflows, and obtain independent release/security approval. No actual CRC data, executable distribution or production migration. Release hold unchanged.
