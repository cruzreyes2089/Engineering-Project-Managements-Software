# Historical project association — isolated SQL prototype

Status: prototype only; not a native migration or a released feature.

## Design

The historical archive and operational project remain distinct records. A link refers to an existing sealed historical project and an existing native project belonging to the same client. Original historical identifiers are compared exactly, and the stored archive and accepted proposal hashes must match. A historical identifier or native project may participate in at most one active association. Reversal is a separate immutable record; it does not delete the original link. A new association may be reviewed following reversal. The active-link projection explicitly reports work_authorized = 0.

The prototype blocks ordinary updates, deletions and INSERT OR REPLACE operations against association/reversal history. Its foreign keys and insertion guards reject missing parents, incorrect evidence, duplicate active associations and cross-client links. These are integrity protections, not protection against a privileged administrator who can alter the database schema.

## Local evidence

Six Python unittest cases passed using isolated in-memory SQLite with minimal synthetic parent fixtures: valid link without work authority; one-to-one enforcement; exact identity, evidence and client validation; reversal/reassociation; update/delete/replace rejection; rollback.

Verified repository blobs match the locally executed files:
- SQL: c9d1349fa456a404ff9a018c17d073536c5f97ab
- Test: 0e807633932cc688dd949ea6ad45f6397885d0d0

Reproduce from the repository root:

    python -m unittest discover -s tests/schema -p test_historical_association_sql.py -v

No native compilation, WPF exercise, full-schema migration or Windows acceptance was performed.

## Integration gates

Do not amend a previously numbered schema migration in place. Keep schema 46 baseline and schema 47 candidate unchanged until their combined verification is established. Introduce the association migration separately after that gate.

The native commit service must enforce import-administrator and project-view grants, explicit review confirmation, substantive reason, exact preview evidence, current accepted proposal snapshot byte verification, and atomic association/audit writes. Reversal must verify the expected active association and record its reason and actor atomically. Concurrency and stale-preview cases must be tested against the actual native schema. The prototype itself cannot verify user permissions or the cryptographic integrity of proposal snapshot bytes.

Extend the existing archive fingerprint to cover association and reversal records deterministically and exercise populated recovery/restore and migration fixtures. That extension has not yet been implemented. No association may satisfy agreement, conflict-clearance, project-start, or financial authorization requirements. No production data or main-branch merge is permitted under the current release hold.
