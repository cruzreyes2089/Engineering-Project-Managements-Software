# Native historical archive qualification (staged)

This package is qualification code only. The production persistence project, schema 46, permission catalog, desktop application, recovery format and accounting holds are unchanged.

Run on Windows with the repository's pinned SDK:
```powershell
./tools/migration/Test-HistoricalArchiveCandidate.ps1
```

The runner archives the exact committed HEAD into a new temporary directory, adds the candidate to that copy of the persistence project, and builds/runs a separate console harness against a newly generated synthetic SQLite database. It does not accept an existing database, invoke Access or Manager, publish a desktop application, or alter the working tree. It deletes its owned temporary paths. The test-only grants and prototype tables are installed only on that synthetic database.

The harness covers authentication, CRM prerequisite, explicit review, digest mismatch, individual grant revocations, forced password change, faults at batch/second row/seal/audit, operational table snapshot equality, historical status and native mapping, exact replay, changed-package denial and missing audit evidence. No results are claimed until native execution completes. The audit table and session/permission tables are excluded from the operational snapshot; the archive audit marker is checked separately.

Source review at df2cdf61c0ab02b200e34cdfdead304e96e74b2c found compatible native Command/Exec/Require/RequireAdministration/LegacyHistory contracts. Recovery currently explicitly accepts schema 46 and must be separately revised and qualified before any archive migration. Test-only grant provisioning is not a migration or authorization policy. Native execution, schema upgrade/bootstrap parity, recovery qualification, full source-value reconciliation, desktop register activation and production release remain gated.

## Combined batch: schema, permission and recovery rehearsal

The first Windows run at 99961801e228cb7a56eab26504328cdea7961f8c passed 1,702 existing application checks and 83 Python schema tests with zero build warnings/errors, but failed the candidate harness because the native CRM importer expects its uppercase digest. The archive parser intentionally uses lowercase SHA-256. The fixture now uses the native uppercase contract for CRM import and retains lowercase for the archive package. No parser validation is relaxed.

The same runner now qualifies both schema 46 with isolated prototype provisioning and a schema 47 candidate in a second compilation. Exact-anchor transformations affect only the owned temporary Git archive, never the checked-out application. The candidate adds an embedded schema resource, atomic startup upgrade, administrator-only initial grants, bootstrap parity, upgrade rehearsal, and read-only recovery validation of archive evidence. Synthetic tests cover initialized 46-to-47 upgrade without altering the original file, fresh bootstrap, explicit import after upgrade, recovery copy/readback, exact replay after restore, overwrite rejection, and incomplete archive backup rejection. These are pending native results. Schema 46 remains the committed application's startup schema. No real CRM or project data, external accounting service, executable distribution or live recovery is authorized by this batch.
