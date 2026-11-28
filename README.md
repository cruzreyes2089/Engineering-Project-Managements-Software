# Engineering Project Management Software

CRC Practice Manager is a developing open-source Windows desktop application for engineering, architecture, and other professional services firms.

## Project goals

- Client and project management, proposals, contracts, tasks and documents.
- Time tracking, approvals, invoicing, project budgets and profitability.
- Role-based authorization, audit history and conflict-of-interest controls.
- Provider-independent accounting integrations, with Manager.io as the first candidate.

## Development status

Development source; not production-ready. The native C#/.NET, WPF and SQLite application now includes CRM, proposals, project handoff, staff, scheduling, work submissions, approvals, document evidence, QA, additional-scope controls, internal invoice drafts, reports and recovery copies. Manager.io read-only integration and synthetic posting rehearsals are experimental; no production posting, duplicate AR ledger, or automatic replacement of a live database is enabled. Recovery copies can be verified and restored to a separate file; collections review is bounded to a synthetic fixture. See the stacked draft pull requests and exact-commit Windows CI for verification. Desktop acceptance, release security review and live provider qualification remain required.

## Roadmap

1. [Stage 1: internal operational application](docs/stage-1-release-scope.md) — current owner-approved priority; qualification pending.
2. Stage 2: qualified legacy migration and Manager integration.
3. Stage 3: network operation, commercial licensing and distribution.

See the [Stage 1 operating guide](docs/stage-1-operating-guide.md), [installation contract](docs/stage-1-installation-contract.md), and [acceptance plan](docs/desktop-release-acceptance-plan.md). All are development and qualification references, not authorization to execute or distribute a held build.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md), [SECURITY.md](SECURITY.md), and [the import checklist](docs/source-import-checklist.md). Use synthetic data only. Do not upload business databases, private documents or credentials.

## License

Apache License 2.0; see [LICENSE](LICENSE). Third-party software remains subject to its own license. Manager.io is an independent product; this project is not affiliated with its vendor.
