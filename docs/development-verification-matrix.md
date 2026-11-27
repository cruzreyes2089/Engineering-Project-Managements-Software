# Development verification matrix

This is a source-development record, not an acceptance certificate. The original feature and acceptance registers define the full intended scope; source implementation does not imply legacy parity, desktop acceptance, legal compliance or production safety.

Verified milestones:
- Schema 25 jurisdiction rules: run 45912124870 (15 Python tests + 992 native checks; zero compiler warnings/errors).
- Conflict-status UI and policy audit evidence: run 45912791930 (15 + 994; zero warnings/errors).
- Schema 26 Word review documents, history and exact-byte retrieval: run 45912960689 (15 + 1014; zero warnings/errors).
- Schema 27 CRM transfer initial build 45913090212: compiler and Python tests passed, native suite failed on a test expecting a space in the phone format. Fixed to match native display/storage, with additional inactive-contact checks. Corrected revision 225c4d336130fc8fa9636f2044c5e49b5df49524 under CI at this record's preparation. Mapping-history extension requires its own regression. Access DAO script syntax qualification is separate from actual DAO execution.

Key outstanding requirements:
1. Source-specific legacy mapping: actual trusted Access schema and reconciliation counts, no private source data in this public repository.
2. Final proposal/agreement template and document delivery: owner-approved template language, signed evidence, no invented legal terms.
3. Actual Manager invoice/receipt/payment posting: dedicated fresh test fixture, verified native transport and durable attempt/recovery controls, correct due-date/line/account/tax semantics. Retainers and reversals not qualified. Manager remains authoritative; no duplicate GL.
4. Complete desktop acceptance, role and project isolation, operational corrections/closeout, full report parity, installer/upgrades, independent release-security qualification, and eventual network edition.
5. No automatic real-data migration, no production posting, no artifact distribution. No main-branch merge.

Reference exact source commits and CI logs rather than interpreting the test count as a percentage of completion.

## Verified continuation through schema 45

All entries below are source-level Windows regression results. Each includes 15 Python schema tests, zero compiler warnings/errors and the native check count shown. The active draft PR #6 was promoted to schema 45; no main merge or release occurred.

| Schema | Windows CI run | Native checks | Scope |
|---|---:|---:|---|
| 36 | 45942486512 | 1331 | Rejected overhead correction |
| 37 | 45943582353 | 1361 | Approved project work invalidation |
| 38 | 45947105673 | 1389 | Approved overhead correction |
| 39 | 45947483948 | 1417 | Attendance revision and missed clock-out recovery |
| 40 | 45947845323 | 1459 | Missing and duplicate attendance |
| 41 | 45948368742 | 1484 | Closed-project historical work |
| 42 | 45948966766 | 1515 | Closed-project linked corrections |
| 43 | 45949569120 | 1540 | Cancelled-only allocation reconciliation |
| 44 | 45949872265 | 1566 | Protective approved-invoice holds |
| 45 | 45950103973 | 1593 | Independent hold release and renewed holds |

Exact verified baseline: 9670918c763b1d0e6ed6bbe90246436170d156a3. Schema 46 is a candidate; its test-first and functional CI must qualify independently. Automated checks are not completed acceptance-register scenarios or a release percentage. The separate legacy acceptance register remains specified, not executed; do not mark scenarios passed without actual evidence. See [single-user release gate plan](single-user-release-gates.md).

## Verified schema 46 and release preparation

- Schema 46 corrected observation guard: run 45950890547, 15 Python + 1604 native checks, zero warnings/errors.
- Combined migration metadata preflight: run 45951402051, 22 Python + 1604 native checks, zero warnings/errors.
- Isolated desktop workspace: run 45951746285, 22 Python + 1618 native checks, zero warnings/errors. Promoted to active draft PR #6 at 25122753f95a91239778135375c9df6ce3810620. No WPF interactive scenario executed.
- Upgrade rehearsal initial red run 45951814689 stopped on pooled fixture connection before reaching stub; corrected test-first revision requires qualification. No new schema.

[Desktop acceptance execution plan](desktop-release-acceptance-plan.md) covers all original scenarios without marking any passed. No security hold change.

- Complete acceptance procedure documentation: run 45952257022, 22 Python + 1618 native checks, zero warnings/errors; integrated. No interactive acceptance executed.
- Isolated upgrade rehearsal: run 45952285436, 22 Python + 1630 native checks, zero warnings/errors; independent package passed. Combined integration pending.

## Verified historical archive and storage development through schema 54

These are completed source-regression runs, not field or interactive desktop acceptance. All rows below have zero compiler warnings/errors. The Windows directory metadata handle does not exclude rename; passing the test suite does not close that qualification gap.

| Schema | Exact source revision | Windows run | Python | Native | Scope |
|---|---|---:|---:|---:|---:|---|
| 47 | a9522cf3b265d3ab1cb10e22a314a4ab66925686 | 46058478446 | 83 | 1770 | Native archive and validation preview |
| 47 | 37167b28cc05888f68945c534ccc963467edd7df | 46059351444 | 91 | 1814 | Combined synthetic historical native archive |
| 48 | cf3b6c0f68165fc5302f48f83cf505a925cc33c3 | 46062057845 | 91 | 1979 | Business storage preferences |
| 49 | 177349364ff96a88bfc6de84e67c26bc743240d3 | 46062376730 | 91 | 2011 | Business production templates |
| 50 | 6d40f7710cf0f8622f4fcad04fee339562cddce3 | 46062835254 | 91 | 2032 | Named templates and project selections |
| 51 | b8295eec0704742e64ef0987dc865a9c298a2d19 | 46062978497 | 91 | 2044 | Project folder overrides |
| 52 | bc243627e8f99f75380e37f6968041f30b40c9b0 | 46063217619 | 91 | 2064 | Historical category recommendation implementation (retired) |
| 53 | 15ab42e966bfe3a51c24c8355a62ae6cb0f3a06a | 46064063172 | 91 | 2066 | Suggested preset and optional preference, retired categories |
| 54 | efa3256ae36543b330b6a7bdfdfa386ead6ab5ce | 46064325605 | 91 | 2089 | Inactive project storage bindings |
| 54 | 277fd84edacb8004ef93ace3d3f9c6aa4123258d | 46064575362 | 91 | 2103 | Nested synthetic storage and journal recovery |

The later read-only storage observation revision must be qualified separately. No live physical provisioning, DAO source migration, financial posting, final distribution or main merge is authorized.
