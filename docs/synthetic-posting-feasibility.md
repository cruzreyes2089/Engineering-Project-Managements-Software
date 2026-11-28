# Milestone 04C — synthetic posting feasibility evidence

Status: operator-assisted API feasibility passed; native posting NOT implemented or authorized. Security distribution hold unchanged.

Two distinct synthetic businesses were used. The existing native acquisition fixture remains CRC ACCOUNTING TEST (invoice 100, receipt 40, expense 10). The separate CRC POSTING SANDBOX was used for explicitly authorized local PowerShell API2 writes and must not replace the original fixture or be modified during regression testing.

## Observed results (owner supplied console output and Manager Summary)

| Transaction | Synthetic reference | Amount |
|---|---|---:|
| Sales invoice | CRC-SB-INV-0001 | 125.00 |
| Customer receipt | CRC-SB-RCT-0001 | 50.00 |
| Direct expense payment | CRC-SB-PMT-0001 | 20.00 |

Observed ending balances: receivables 75, cash 30, assets 105, liabilities 0, income 125, expenses 20, profit and equity 105. One record per register. Currency context was displayed as dollars; this is not proof of API currency metadata.

Receipt form: PaidBy numeric 1, Customer, ReceivedIn, Lines.Account, Lines.AccountsReceivableSalesInvoice, Lines.Amount. Do not require AccountsReceivableCustomer in an invoice-allocated receipt. Direct expense form: Contact is free-text vendor, PaidFrom, Lines.Account, Lines.Amount. Both write tests returned HTTP 201 and verified detailed record identity and allocation. Payment register omitted reference although payment-form detail contained it. Resolve by verified GUID and detail, never solely by register reference. Actual field sets, currency, tax and project assignments need strict version-specific review; do not widen the existing native fixture parser without evidence.

Persistent per-workstation guard files and empty-register checks were exercised as read-only duplicate simulations. These are not provider idempotency, atomic remote locks, or multi-user protection. Do not repeat any POST to the verified sandbox; a timeout after commit is unresolved until independently reconciled. The final acceptance script calculated expected financial balances and compared the invoice balance; Manager Summary supplied independent operator evidence for the remaining totals. No full ledger API or atomic snapshot verified.

## Source follow-up

Added normalized reconciliation regressions for the separate posting fixture without changing native acquisition scope, roles, database schema, network routes or posting authorization. All IDs in tests are generated synthetic values. CI must pass on the exact new revision; no new binary artifact may be distributed. Existing offline posting journal supports invoice and receipt rehearsals only; direct expense payment rehearsal requires its own validated intent and schema migration. Before any native write: implement source-level correlation and durable attempt/recovery evidence surviving restore, approval and permission enforcement, negative-path transport fixtures (timeout after remote commit, unexpected response and duplicate/collision), and separate disposable sandbox enrollment with explicit authorization. Preserve both current test businesses and prior local guards.

No supplier invoices, payable settlements, credits, reversals, concurrent writes or production authorization verified. No tokens, business GUIDs, private logs or binaries included.
