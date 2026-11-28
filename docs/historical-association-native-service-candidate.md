# Native historical association service candidate

The source file tools/migration/prototypes/HistoricalAssociationService.cs is staged outside the compiled application project. It is not a released, callable application feature. No existing schema migration was modified.

## Intended service contract

- Prepare and commit require the existing historical-import administration, historical view, client view and project view checks within the transaction. The same authenticated reviewer ID is part of the preview comparison.
- The commit revalidates the sealed archive, exact historical identity, mapped client, accepted proposal snapshot through ProjectRow, existing active association conflicts and combined integrity witness. The current comparison must match the expected review, including title/site comparison and reviewer identity.
- Association and reversal require explicit confirmation and the existing Validation.Text review-note validation. The actor comes from RequireHistoricalImport; the timestamp comes from UtcNow. Neither is accepted as a caller-supplied write parameter.
- Both operations use an immediate transaction and the existing Audit helper, with event insertion and audit before commit. Reversal preserves the prior association and requires an exact active record and fresh integrity witness. No operational project or authorization write is present.
- The native integrity witness incorporates the existing archive fingerprint plus ordered association/reversal rows, with a versioned domain and a per-table 100,000-row maximum. The native format is intentionally separate from the Python reference format. It does not yet include the audit table or verify all historical links against current parent evidence; it is not a complete recovery acceptance check.

## Verification

The existing six SQL integrity and six Python transaction tests were rerun locally: 12 passed. Their four local Git blobs match the previously verified committed versions c9d1349fa456a404ff9a018c17d073536c5f97ab, 5f8d6083c56ed1c9c4b73adc70dc59e094bc441f, 0e807633932cc688dd949ea6ad45f6397885d0d0, and 65a55fdc396e29503af3e479311e40e4e8e8cb00. They do not exercise the staged C# source. No dotnet or csc executable was available locally.

## Promotion gates

After exact-revision schema 46 and 47 combined Windows verification, create a separately numbered migration and integrate the staged source into the actual persistence project. Review helper signatures and actual schema compatibility, compile, and test authorized versus denied users, changed reviewer and revoked permission, valid and mismatched accepted snapshots, stale preview including link/reverse/relink, competing distinct associations, audit failure rollback for both actions, duplicate active link and repeat reversal, and unchanged authorization/operational state. Verify real audit pairing and counts, foreign-key integrity, full recovery and restore fingerprint equality, bounded input and scan behavior, and the absence of unauthorized exports. Add independently reviewed reconciliation UI only after those service checks pass.

A preview record is a concurrency comparison, not a secret token or proof of manual review; the service must still authenticate and authorize the transaction. The proposed conservative scan limit requires operational review. No application schema promotion, production data, main merge or release authorization.
