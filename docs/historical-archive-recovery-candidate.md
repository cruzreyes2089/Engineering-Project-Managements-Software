# Historical archive recovery and concurrent import — schema 47 candidate

Candidate branch only. The schema 46 baseline and draft PR #6 are not promoted. No production data or executable distribution.

## Recovery behavior

Historical archive reads now revalidate the sealed batch's imported CRM transfer hash and mapped parent identities using the existing CRM mapping history. Incomplete or inconsistent archive state fails closed. Recovery copy creation validates the copied archive before publishing; isolated restore validates the staged read-only archive before publishing the destination. Restore reports historical project count separately from operational projects. Existing exact-file hash, database integrity, foreign key, isolation and accounting-hold checks remain in force.

These checks establish structural consistency, not authenticity of the historical source or protection against arbitrary privileged database tampering. A rejected recovery request can still leave its request audit in the source database, consistent with the existing recovery design. The application does not repair or discard an incomplete archive automatically.

## Added regression scenarios

- Independent authenticated sessions simultaneously submit identical reviewed package bytes. Expect exactly one Imported result, one AlreadyImported result, and one import audit event; no duplicate records or native project number consumption.
- A valid census whose bytes change (even whitespace) yields DifferentPackageAlreadyImported; no merge or replacement.
- Create a recovery copy of a populated archive, restore to a distinct new file, reopen under authentication and compare all projected historical fields and native client mappings. Verify the source and original recovery copy remain intact.
- Remove the seal from a disposable recovery clone and recompute its file digest. Require read, backup and restore rejection; do not publish a new recovery or restore file. This negative fixture deliberately removes an immutability trigger on the disposable clone only.

Source commits: 8c6d9686fe63ba02b7708b611638a255c63ab763 (provenance read guard), 6eda97caecbb0e04cd884b99aaa5b325e86fa4bd (backup check), 03c49d79e641642d68510df6dbb2a11ed4f766d6 (restore check), b6faa3c5a47e37e82fd10a4584c7e38caaf294ac (restore count presentation), 0431a309e81bfde81d0370c960ae90ea2aa72c1c (concurrent and changed package checks), 48ddb3a0e8d56b6fd80b6ae218430ad3e3c29cb4 (recovery tests).

No .NET or PowerShell executable is available in the current local environment. These native regressions have not been compiled or executed. Combined baseline and candidate CI, populated archive upgrade rehearsal (including stable archive identity fingerprint), actual DAO fixture execution, interactive acceptance and security review remain outstanding. Do not treat this source implementation as release authorization.
