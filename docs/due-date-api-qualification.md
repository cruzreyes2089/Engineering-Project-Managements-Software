# Due-date API qualification (manual fixture, no write adapter)

Manager's published user guide describes immediate/default, Net days, and fixed By date payment terms. The API list and form routes are documented but installed field names, enumerations, missing/default semantics and due-date register representation remain unqualified. Sources: https://www2.manager.io/guides/7178 and https://manager.readme.io/reference/getsalesinvoiceform and https://manager.readme.io/reference/getsalesinvoices .

Use a trusted Windows test environment only. Do not change any existing accepted test business. In a NEW Manager business named CRC DUE DATE TEST, use USD, add a text custom field named CRC Due Date Test Identity and one synthetic customer. Enable sales invoices and use one ordinary synthetic service-income account. Manually create exactly three unpaid synthetic sales invoices, each issued 2026-11-01, USD 100 total, no taxes/discounts/late fees/receipts/payments. Set the exact references and due-date modes:

| Reference | Due-date selection | Expected effective date |
| --- | --- | --- |
| CRC-DUE-IMMEDIATE | Immediate/default | 2026-11-01 |
| CRC-DUE-NET30 | Net 30 days | 2026-12-01 |
| CRC-DUE-FIXED | By December 15, 2026 | 2026-12-15 |

Compare the Manager UI dates first. The script prompts for loopback port and secure token; no token, raw response, customer identity, GUID, local port or file path is printed. GET-only fixed routes, no redirects/proxy/cookies, bounded response, exact business and marker, three exact invoice references, one customer, issue date, USD-only attestation and amount/balance checks; two matching observations of selected due-date fields and identities and marker rechecked. The field extraction is diagnostic only, not a qualified parser; the JSON conversion is not an independent schema attestation. Script syntax checked by CI; actual execution on installed Manager remains pending. Preserve this fresh fixture after acceptance; do not run the script on production or alter earlier fixtures. The output and UI dates determine the next implementation of overdue aging and reminders. No live posting, no release distribution, no reliance on draft due dates as actual accounting due dates.

Offline CI self-tests exercise the diagnostic mechanics without a token or network connection using explicitly invented due-field names. Stable observations, wrong business, duplicate references, unexpected balances, mixed customers, changed due fields, redaction and disallowed routes are checked. These fixtures do not qualify actual API date fields.

Revision 2 provides stage-specific safe validation messages and HTTP status on failure; never prints the exception message, request URL or raw response. It preserves all identity, amount and repeated-observation checks. This is a diagnostic enhancement, not a relaxation of fixture requirements.
