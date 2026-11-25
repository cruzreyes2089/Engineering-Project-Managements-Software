# ADR: external accounting provider

Status: Proposed, pending owner approval and verification against application source.
Date: 2026-11-25

CRC owns operational clients, projects, contracts, time approvals, rates, bill preparation, project expenses and operational job costing. Manager.io is the initial candidate for authoritative posted accounting transactions, payments, general ledger, chart of accounts, AR/AP, reconciliation and financial statements. Payroll is out of scope.

Use a provider-neutral application boundary. Implement adapters independently through documented APIs or supported extensions; do not copy or bundle Manager.io's implementation. Review licensing for the exact selected version and dependencies.

## Feasibility after native build verification

- Select an edition/version and isolated synthetic test company; verify API availability, supported operations, authentication, permissions, external IDs and costs.
- Start read-only; validate customer, account and project mappings. Test approval-gated invoice and payment posting only in the sandbox.
- Prevent duplicate posting with stable identifiers, mapping records, reconciliation before retry, and auditable states for timeouts, concurrent attempts and partial failure. Do not assume provider-side idempotency.
- Store tokens outside source control and logs; enforce finance authorization and least privilege. Avoid unnecessary disclosure of cost rates and protected data.
- Use decimal money amounts, explicit currencies and rounding, accounting dates and audit trails. Reconcile balances, tax, credits, payments and corrections. Never directly edit the provider database.
- Provide manual export/reconciliation during outages. Avoid a second independent general ledger.
- Do not post to real books before accounting review and acceptance tests.

Exit criteria: documented API and license review, reproducible sandbox tests, reconciled control totals, error/retry tests with no duplicates, and accounting review. No adapter is implemented yet.

References: https://www.manager.io/ and https://github.com/managerhq/Manager
