# SQLite connection boundary hardening

The main LocalPractice connection factory sets trusted_schema=OFF and verifies that setting and foreign_keys=ON before returning each connection. Errors dispose the connection and propagate; no fallback to less restrictive settings. Tests inspect two actual service connections. Existing schema, migrations and recovery behavior require full regression. This is a defense-in-depth configuration, not validation of arbitrary SQLite databases or protection against direct file access. Other specialized connections require separate review. No production startup, encryption or ACL qualification is implied.

Prior credential revision 8f61c9111ce2a02e1c2c76d24afded3e454e053f, run 46095210494: 2326 native checks and 114 Python tests passed, zero compiler warnings/errors. New changes pending CI. Independent security hold remains.
