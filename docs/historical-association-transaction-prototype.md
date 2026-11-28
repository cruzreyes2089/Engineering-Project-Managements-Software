# Historical association transaction prototype

Isolated Python/SQLite reference contract only. No native service, application schema migration, permission integration or WPF change.

A read transaction captures exact historical and native identities, archive transfer hash, accepted proposal snapshot hash (verified against UTF-8 snapshot bytes), and a deterministic SHA-256 fingerprint covering every association and reversal field. The fingerprint uses versioned domain separation, ordered queries and JSON arrays with newline framing. It is not an authentication token or signed integrity certificate.

An immediate write transaction repeats the comparison before insertion and requires explicit confirmation and a nonblank reason of at most 1000 characters. A separate reversal checks the expected history fingerprint and active association. Both write their event and a synthetic audit record atomically. Any unrelated association history change invalidates an outstanding review conservatively, including a link/reversal cycle. The SQL guards remain in force.

Local verification: 12 distinct tests passed (six prior SQL tests and six new reference contract tests). The new tests cover fingerprint equality across SQLite backup, reversal sensitivity, stale review after reversal, changed snapshot bytes, audit failure rollback for both operations, two concurrent submissions with exactly one successful commit, and confirmation/reason validation. The tests use synthetic parent fixtures and an intentionally synthetic audit table. No operational data was used.

Verified local file blobs:
- association_review.py: 5f8d6083c56ed1c9c4b73adc70dc59e094bc441f
- test_association_review.py: 65a55fdc396e29503af3e479311e40e4e8e8cb00

Reproduce from repository root with python -m unittest discover -s tests/schema -p 'test_association_review.py' -v.

Native integration still requires transaction-scoped authorization, trusted authenticated actor and UTC timestamp, existing production audit API, full archive provenance validation, actual schema constraints, bounded recovery scanning and combined archive/association fingerprints, stale reversal and competing distinct-review tests, native UI and Windows acceptance. A caller-supplied actor is not authorization. The prototype fingerprint does not cover the audit table, archive content or native projects, and is not wired into native recovery. Do not modify previously numbered migrations or release the candidate until baseline verification is established.
