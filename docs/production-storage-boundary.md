# Proposed production storage boundary — review required

No production mode or installer activation. No actual firm data, credentials, or deployment paths in this public repository. Scope is a single-user Windows installation, not a shared network database.

## Keep the daily workflow simple

The user signs into the application and uses ordinary project folders. The application should manage its own private database location without asking the operator to browse for database files. A single administrative choice of a dedicated projects root is sufficient for ordinary project-folder use. Folder templates are independent of optional formal document registration. Backup uses an explicit protected destination and separately retained verification evidence.

## Proposed boundaries

| Area | Proposed placement and constraints |
|---|---|
| Program | Versioned signed program files; separate from data; installer never opens SQLite |
| Practice data | Dedicated private local fixed-drive production directory with exact identity marker; never alias DEV or acceptance roots; no silent discovery or creation on reopen |
| Project files | Separate dedicated local root selected by administrator; stable project number; validated ordinary paths; no broad recursive access or deletion; preserve existing contents |
| Recovery | Separate protected backup location, preferably separate physical medium with approved encryption and retained recovery keys; no automatic replacement of live database |
| Development/acceptance | Existing distinct synthetic paths; no production credentials or data; candidate schemas 48/49 remain isolated |

## Confidentiality design decision

Existing SQLite is plaintext; DPAPI protects supported accounting secrets only. Restricted Windows DACLs are necessary but do not protect against same-user processes, administrators, theft of an unlocked device, or all path races. Recommend full-volume encryption on the approved production device and separately encrypted backup media, with recovery keys held outside the encrypted device by the owner using an independently reviewed custody procedure. Do not assume encryption is enabled, implement an unreviewed cryptographic format, or store recovery keys alongside backups. Independent reviewer must establish whether volume encryption suffices for the intended threat model or a reviewed database encryption approach is required. Project folders and exported confidential reports require the same custody review. No assertion of compliance or cryptographic qualification.

## Implementation and acceptance sequence

1. Review exact storage locations, identity marker and path separation, file and directory effective access, local account threat model, encryption status and recovery key custody. Owner and independent reviewer approve scope before production activation.
2. Implement explicit first-time production creation, then existing-only reopening; no fallback to DEV, automatic upgrade or default replacement. Verify directory/file/sidecar permissions and path identity; document residual time-of-check risks. Revalidate on sign-in renewal. Do not silently alter preexisting ACLs.
3. Separately qualify real folder setup with ordinary fixture data, collision, stale references, partial failure, and preservation of existing files. Do not enable candidate migration solely for convenience.
4. Verify encrypted-device and separate-backup recovery on a clean isolated Windows installation under exact-artifact approval. Retain trusted verification hash; verify actual restore, recovery-key access and source preservation. Never use a checksum as encryption or authenticity proof.
5. Preserve approved installer, signing, independent security and owner scope gates. No production use before explicit release authorization.

## Current evidence

PR #45 passed Windows run 46108011570 (2394 native checks, 151 Python tests, zero compiler warnings/errors) and prior-source upgrade 46108011612. PR #46 Windows run 46108173495 failed after nine new directory-refusal assertions passed: synthetic ACL reset did not persist the original access section, so subsequent session reopen was refused. Prior-version upgrade 46108173479 passed. Corrected fixture explicitly marks the original access section for persistence and checks restored layout; new reauthentication path revalidates the existing workspace. Exact new candidate CI pending. No security gate relaxed, no production activation. Independent security, original warning disposition, physical signed installer and 56-case desktop acceptance remain outstanding.
