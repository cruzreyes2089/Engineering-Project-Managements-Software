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
