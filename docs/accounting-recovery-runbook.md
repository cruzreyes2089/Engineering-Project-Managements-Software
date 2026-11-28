# Accounting recovery and posting readiness — operator runbook
Status: development / no provider posting. Applies to the synthetic test environment only.

## Before clone, restore, or replacement
1. Stop new accounting work. Preserve the current CRC and Manager business backups separately, with UTC timestamps; never overwrite the only known-good backup. Close CRC before copying its entire local database directory. Use Manager's supported Backup function, not direct edits to its database. Maintain a recovery record outside both databases.
2. If CRC already has the lifecycle safeguards, declare the applicable hold in Accounting connection > Posting safeguards (offline). After restoring an older CRC database, declare the hold again before any integration activity; a hold stored inside an older backup may be rolled back. The screen is not a production authorization control.
3. Isolate the clone from the original. Never allow two writable copies of the same business to share an accounting integration. In a separate test clone, revoke any copied tokens and replace the test marker with a fresh definition; confirm only the intended clone changed. Do not modify the original's credentials or marker. A name, path, port, marker UUID, and matching totals are not sufficient to prove identity of an exact copy.
4. Use supported Manager History and independently retained accounting/export evidence to reconcile all affected invoices, receipts, payments, balances and unresolved attempts. Preserve original source operation keys and fingerprints; do not regenerate them or replay uncertain requests. A missing row in a restored copy is not proof of non-posting in the authoritative books.
5. Re-enrollment and release require an independently verified recovery protocol and explicit authorized review. No release/reset operation exists in this development version. A declared hold blocks simulated reservation/begin/finish/cancel, but read-only diagnosis is still possible. Do not attempt to remove the hold by SQL, database replacement, or token rotation.

## Before implementing any actual sandbox writes
The public Manager API2 documentation lists create routes for sales invoices and receipts (HTTP 201), but does not specify required writable form fields, response identity, idempotency, or atomic duplicate prevention. GET form shapes are not a write contract. Do not assume POST replay is safe or use PUT as an undocumented create-by-ID mechanism. Need a separate isolated synthetic business (not the verified baseline), exact-version contract validation, explicit external operation correlation, durable reconciliation evidence independent of restored local DB, error cases including timeout after remote commit, duplicate rejection, confirmation/readback and correction procedures. Manager Desktop is free but single-user; do not assume shared remote writes or permissions. No financial-write endpoint in CRC until this gate is met.

Vendor references reviewed 2026-11-25:
- https://manager.readme.io/reference/postsalesinvoiceform
- https://manager.readme.io/reference/postreceiptform
- https://www.manager.io/guides/backup/
- https://www.manager.io/guides/import-business/
- https://www.manager.io/guides/history/
- https://www.manager.io/download/

Security distribution hold remains separate. Do not run the flagged archive or treat antivirus non-detections as vendor clearance. No new binary uploads.