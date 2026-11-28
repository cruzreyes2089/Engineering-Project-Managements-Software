# Historical project migration — offline preflight

The existing native project table is not a historical archive. It requires an engagement derived from an accepted proposal; its handoff checks a frozen proposal snapshot, signed agreement evidence, active client and current conflict clearance before explicit start authorization. Never fabricate a proposal, zero fee, acceptance, signed PDF, start date, audit actor or clearance to satisfy these constraints. Never treat a legacy Active or Closed label as a native lifecycle transition.

The first candidate is a private offline preflight, not a native project importer or archive database. It preserves the distinction between source-reported history and currently verified operational rights. No new schema or UI is introduced.

## Private inputs

All files remain outside the repository. Use only synthetic fixtures until actual source extraction and mapping are separately qualified.

- Reviewed normalized CRM transfer (CRC-Legacy-CRM-v1). Independently qualify the CRM migration and bind its identity census before a future native archive import. This preflight checks client identities and statuses, not all CRM fields or contacts.
- Historical project transfer: exact root fields format (CRC-Legacy-Projects-v1), sourceSha256, crmTransferSha256, projects. Each project has exactly legacyId, legacyClientId, legacyProjectNumber, title, site, legacyStatus, legacyAuthorizationStatus, authorizationEvidenceRef. All are strings. Original ID and parent ID use the CRM ASCII identifier rules. Number, title and both source status labels are required; site and evidence reference may be empty. Historical labels are not mapped to current native state. Evidence reference is an opaque historical identifier, not a validated agreement, document path, signature or permission. Preserve exact spelling and do not invent missing labels; unresolved source semantics require explicit review.
- Independently collected project identity census: exact root fields format (CRC-Legacy-Project-Census-v1), sourceSha256, crmTransferSha256, projectTransferSha256, projects. Every census row contains only legacyId, legacyClientId and legacyProjectNumber. Collect before constructing the transfer; retain and bind exact source evidence privately. Do not regenerate the list from the converted transfer.

Run:

    python tools/migration/historical_project_preflight.py CRM_TRANSFER PROJECT_TRANSFER PROJECT_CENSUS --expected-source-sha256 EXACT_SOURCE_DIGEST

The source digest must be independently retained from the trusted closed source. The CLI calculates the exact CRM and project transfer byte digests and checks the census binding. It rejects duplicate case-insensitive IDs, orphan clients, altered historical numbers or relationships, unexpected fields and malformed labels. It reports duplicate number groups rather than merging records. Projects associated with inactive clients remain historical. No row values are emitted; result flags for historical authorization verification, native project creation, financial migration, work authorization and source authenticity are always false.

The project census is an identity comparison, not verification of every historical field. Source hash assertions are not authenticated by this tool. Actual project metadata inventory, DAO table selection, source field mapping, extraction and field-value review remain outstanding. No signed-document extraction, financial amounts, staffing, time, expenses, proposals or invoice imports are supported.

## Next native design

Use a separately permissioned, nonoperational archive with explicit legacy batch and client-map provenance. Preserve historical project numbers independently of native project numbering. Future linking to an actual native project requires reviewed reconciliation and the ordinary proposal, agreement, conflict and start controls. Never insert imported archive rows directly into the native projects table. Require transaction rollback, replay safety, restrictive read access, immutable historical evidence and actual source reconciliation. Do not introduce schema changes until exact combined source regression evidence is available.

## Verification

Tests c4b1fd8723e3720f24b0132e83ce523fd39297e0; implementation 62648a4553d762ec73745e1c6aeff5be7be61934. Eight Python regression methods are committed. No test execution, actual DAO extraction, or exact combined Windows CI result has been verified in this continuation. The available workflow lookup did not return results for the preceding branch head. No main merge, live data use, release approval or security-hold change.
