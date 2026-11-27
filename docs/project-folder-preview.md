# Project folder template — development preview

The current WPF Projects > Project documents workspace includes a Project folders preview action. This is a read-only view of a versioned relative template, not a registration, provisioning, filesystem validation or folder access grant. The preview identifies the selected native project using existing accepted-project and document view checks; it does not require an agreement or project start authorization because it writes nothing. Historical archive rows are not native project IDs and have no folder action.

Production template CRC-Production-1:
- Architectural
- Calculations
- Construction Administration
- Plancheck Comment Responses
- QA-QC
- Reference Documents / MEP / Mechanical, Electrical, Plumbing
- Send Outs
- Site Pictures
- Structural DWGs

Correspondence is optional and off by default. MEP remains under Reference Documents. Existing Conditions and general Administration are not default production folders. Restricted administration is separate: Proposals, Agreements and Contracts, Change Orders and Additional Services, Billing Administration. The restricted branch is omitted from the service result when Documents.Restricted is absent; the UI is not the access-control boundary. No paths or folder names are supplied by user input.

The specific MEP child names and restricted labels are design defaults; the preview currently uses a fixed template revision and optional correspondence, not persisted customizable templates or per-project selections. Later provisioning must use explicit approved distinct roots, reparse-point checks, OS-level restricted ACL qualification, preview/confirmation, immutable template revision and audited outcomes, idempotent creation/repair and no move/delete/overwrite of existing files. Do not change the project setup completion or start status based on preview. No external directory creation, registration, actual file migration, security-hold change or release approval in this batch. Source tests cover approved labels, optional selection, permission revocation, separate project context and no document record creation. Exact-revision compilation and interactive WPF acceptance pending.
