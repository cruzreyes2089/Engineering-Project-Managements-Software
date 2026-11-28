# Stage 1 installation and data-preservation contract

Status: design only; no installer, packaging, executable launch, distribution or security clearance authorized.

## Boundaries
The existing development app resolves normal data under the trusted local profile in CRC Practice Manager DEV/practice-dev.db. Acceptance sessions use CRC Practice Manager ACCEPTANCE/<session>/practice-acceptance.db with a matching marker. Neither is a production store. Do not rename, move or silently adopt these stores to create a production release. Future production startup needs a separately reviewed data-location decision and explicit upgrade/import consent.

The installation directory must contain only verified application components. It must not contain a database, recovery copy, user documents, credentials, or acceptance evidence. No database path may lie inside the install tree. No installer may recursively delete a profile or a directory containing user-owned data. No shared/network installation or elevated service is required for the Stage 1 local application.

## Installation transaction design
After independent security review authorizes the exact signed candidate and installer:
- Establish supported Windows x64 and an isolated clean test profile; verify the complete deployment inventory and signature against independently retained evidence.
- Use a dedicated application directory with an explicit installation identity; reject preexisting unrelated directories and redirected paths. Do not adopt existing content.
- Stage a verified complete version separately. Verify every staged component before activation; preserve the previous installed version if activation fails. No unverified partial executable may become the launch target.
- Never start the application automatically during install or update. A database schema upgrade is a separate controlled operation; require a verified protected recovery copy and a rehearsed supported upgrade before permitting it.
- Uninstall only installer-owned files identified by the exact installed manifest. Preserve data, backups and user-created files. Unknown or modified files require review, never broad deletion. Preserve prior evidence. No automatic data purge option.
- No rollback that points older incompatible software at a newer database. Recover only through verified isolated restore and explicit owner decision.

## Required controlled acceptance
Record exact candidate inventory, installer digest/signature, environment, reviewer, fixture and results privately. Verify fresh install without Office/Access, interrupted staging, mismatched inventory, existing unrelated path, redirected path, failed activation, upgrade and interrupted upgrade, prior-version retention, uninstall with sentinel user data, reinstall with intact data, isolated recovery, and ordinary account launch. Each failure must be observable and leave the original data unchanged. Actual install/launch remains blocked pending independent security clearance. A static source or plan check is not an installed-product test.

## Scope
This contract does not select a packaging technology, signing certificate or production data root without qualified evidence. It does not change startup behavior, permit real accounting, or waive REL-01/REL-02. Stage 2 and 3 remain deferred.
