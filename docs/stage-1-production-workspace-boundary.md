# Stage 1 production workspace boundary

Decision proposed for later owner acceptance: a dedicated per-user local production data root, distinct from installed binaries, development and synthetic acceptance roots. One firm per database; no silent adoption or import of earlier stores. No production startup mode or arbitrary database argument is enabled in this revision. Production location, filesystem protections, backup policy, and exact installer identity require approval and qualified implementation before live use.

Existing development and acceptance resolution now also rejects directory collisions and detectable file reparse points at SQLite WAL, SHM and rollback-journal sidecar paths. Ordinary existing sidecars are retained without mutation. These checks address accidental redirection/collision on trusted local storage, not hostile concurrent replacement, hard links, dangling links or effective Windows ACL validation. No filesystem security qualification is inferred.

Verified historical source baseline 8e3850fae5aca9655bca538d20d698ac04d1ac35 / run 46091975912: 2310 native, 113 Python, zero warnings/errors; inventory structure passed for 409 files. New regression pending. Security HOLD, no live data, no distribution.
