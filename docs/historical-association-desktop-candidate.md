# Historical association desktop candidate

Staged under tools/migration/prototypes, outside application and smoke-test compilation. No navigation wiring, database migration, or release promotion. Existing schema 46 baseline and schema 47 candidate remain unchanged.

## Review workflow

Select a historical record from the restricted register (future navigation integration), then explicitly search for an exact native project number. The service checks import-administrator, historical view, client view and project view grants and restricts candidates to the mapped native client and an accepted proposal snapshot. No fuzzy or automatic association. The history projection returns up to 1000 associations for the exact historical identity, including active/reversed status and reasons; larger histories fail closed instead of silently truncating. The window is restricted to the synthetic acceptance workspace and rechecks permission during refresh, interaction, activation and periodic polling. These are UI safeguards; the transactional service is the authority.

Review displays title and site comparisons, explicitly distinguishes unavailable evidence, and shows archive and accepted proposal hashes. Matching text does not establish project identity. Input or selection changes clear confirmation and review evidence. Both association and reversal require a fresh review, nonblank reason and explicit confirmation; the commit service independently revalidates the review and permissions. Reversal preserves the original association and never changes operational authorization. The window closes and clears displayed records on denied access; no export functionality is added.

## Staged native acceptance

HistoricalAssociationNativeTests.cs is a disposable-fixture test specification for exact same-client lookup, denied cross-client lookup, altered reviewer, revoked permission, explicit confirmation, review reason, transactional audit failure and rollback for both operations, duplicate/replayed operations, stale preview after a link/reversal cycle, immutable history and unchanged operational authorization. It is not registered in Program.cs and requires the existing historical fixture's native project ID to be passed to it.

## Remaining gates

The proposed service and WPF source have not been compiled or executed. The GitHub combined status lookup for the staged UI commit was empty; this is not a CI pass. Require exact-revision combined baseline/candidate build evidence, separately numbered association migration, integration of service and tests, recovery and audit pairing validation, concurrent native sessions, actual nonadministrator role tests, and independent review of query completeness and UI behavior. Interactive acceptance must cover modal confirmation with changing permissions, stale preview, selection clearing, history limits, closed window cleanup, missing/mismatched site, empty search, wrong-client search and repeated reversal. Navigation must be enabled only after these gates and only in the synthetic acceptance workspace. Do not distribute executables, modify real records, promote schema 47, or lift the release hold.
