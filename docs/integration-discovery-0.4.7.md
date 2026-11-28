# Milestone 04C — v0.4.7 integration discovery

Owner accepted v0.4.6 (10/10 interactive checks, no defects reported). Keep original integration roadmap through reconciliation, consolidate related releases.

## Identity decision and limitations

API2 live customer envelope exposed only business.name. A business name and a saved connection revision do not uniquely identify a ledger. Vendor forum discussion dated April 4, 2026 raises the possibility that backup/import copies access tokens. Treat this as a credible risk, not proven behavior of this installed version. Current vendor API4 specification documents a business-details record key/id, but API4 uses name-based business routing or extension context; external API2 token compatibility and clone stability are not established. Do not upgrade API protocol, read other businesses, or assume an immutable ledger ID without evidence.

Candidate operational identity: a unique, nonfinancial, dedicated custom-field definition/marker, freshly issued business-specific token, explicit synthetic enrollment, and a fail-closed identity comparison before any future write. Marker/key alone cannot distinguish an exact clone, so a clone/restore protocol must rotate tokens and markers before enabling writes; independently test mismatches and revocation. Do not add fictitious financial accounts or use customer records as identity markers. No enrollment or write capability in this release. Existing profile-scoped provisional links remain provisional and never silently transfer. A later migration must require explicit manual confirmation.

## Consolidated discovery

Five fixed GET routes reuse the existing loopback client, saved encrypted profile, 15-second per-request deadline and 1 MB response limit:
- text-custom-fields (two-record sample)
- chart-of-accounts (request Key, Code, Name; provider may return full list, only a two-row structural sample displayed)
- sales-invoices, receipts, payments (two-record sample each)

Only sanitized structural field names/types and HTTP outcomes shown. No customer, account, invoice, receipt or payment values are printed or imported. User must run on a synthetic business. An empty list does not establish record-level schema. Failed requests are reported independently. Report discarded on profile revision mismatch. No database migration, no write routes, no durable identity certified. Actual Manager version response schema remains to be verified.

References:
https://forum.manager.io/t/api-distinction-between-live-and-imported-sandbox-entities/67555
https://forum.manager.io/t/business-details-uuid/66411
https://www.manager.io/openapi/get-business-details.json
https://manager.readme.io/reference/gettextcustomfields
https://manager.readme.io/reference/getchartofaccounts
https://manager.readme.io/reference/getsalesinvoices
https://manager.readme.io/reference/getreceipts
https://manager.readme.io/reference/getpayments
