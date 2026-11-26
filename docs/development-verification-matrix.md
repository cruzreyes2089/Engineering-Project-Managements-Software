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
