# Milestone 04B — baseline import evidence

- Original v0.4.0 ZIP: all 24 recorded SHA-256 checksums matched.
- Python SQLite schema test suite: 9/9 passed on 2026-11-25.
- Source-only inspection: no production databases, actual customer records, credentials or signing keys observed. Synthetic password fixtures are not production credentials.
- Public import preserves original C#, XAML, SQL, project files and tests byte-for-byte. Legacy requirements and acceptance scenarios are retained; internal Access form, procedure and table mappings and internal planning notes remain in the original archive. The original checksum manifest applies to that archive, not this import.
- Native .NET build, C# smoke checks and Windows launch are pending CI and interactive validation. The Linux inspection environment has no .NET SDK. Dependency license and vulnerability review remains pending.
- Development only. No live data. Do not treat a successful compile or automated test as production certification.
