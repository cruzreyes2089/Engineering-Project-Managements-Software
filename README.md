# Engineering Project Management Software

CRC Practice Manager is a developing open-source Windows desktop application for engineering, architecture, and other professional services firms.

## Project goals

- Client and project management, proposals, contracts, tasks and documents.
- Time tracking, approvals, invoicing, project budgets and profitability.
- Role-based authorization, audit history and conflict-of-interest controls.
- Provider-independent accounting integrations, with Manager.io as the first candidate.

## Development status

Early development; not production-ready. The native foundation uses C#/.NET, WPF and SQLite. Source and Windows build instructions are available; [native foundation notes](docs/native-foundation-0.4.0.md) describe the original baseline. Version 0.4.1 adds phone display and entry formatting; see the pull request and exact-commit CI results for verification. Version 0.4.2 adds an experimental read-only Manager.io Desktop API2 connectivity probe; see [accounting connection notes](docs/accounting-connection-0.4.2.md). No synchronization, ledger or accounting posting is implemented.

## Roadmap

- 04A: establish repository governance and a safe source import process.
- 04B: import the reviewed native baseline, verify Windows compilation, tests and startup.
- 04C: assess accounting-provider feasibility using a synthetic sandbox.
- Resume feature development after foundation acceptance.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md), [SECURITY.md](SECURITY.md), and [the import checklist](docs/source-import-checklist.md). Use synthetic data only. Do not upload business databases, private documents or credentials.

## License

Apache License 2.0; see [LICENSE](LICENSE). Third-party software remains subject to its own license. Manager.io is an independent product; this project is not affiliated with its vendor.
