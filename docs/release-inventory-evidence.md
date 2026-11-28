# Published inventory evidence

Verified baseline fc0ad3c21b9f81d4bd690fb5d27c9bbf39b86708, Windows run 46089068861: 2310 native checks, 104 Python tests, zero compilation warnings/errors, static Windows deployment passed. DAO absent; Windows rename exclusion unqualified.

CI now independently parses and checks the exact compact inventory bytes against the computed SHA-256, required component names, file count, byte sum, individual digest syntax, unique case-insensitive paths and obvious database/credential extensions. The parser does not rehash actual files independently, authenticate the build or authorize execution or distribution. The existing inventory producer hashes each actual file. Preserve the exact compact JSON bytes and independently retained SHA-256 outside the checkout for later review.

Use the complete inventory digest as the candidate artifactSha256 in private acceptance evidence, not the executable-only digest; retain executable SHA-256 separately as a component of the inventory. A matching acceptance record remains structure-only. Independent security assessment must verify provenance, actual artifact bytes, all dependencies and signing before any execution. No release, upload, installation or hold bypass. New CI pending.
