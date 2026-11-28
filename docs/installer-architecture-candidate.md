# Single-user installer architecture — source-only candidate

## Verified baseline

Windows run 46037732194 (job 140637298425) on candidate 35a56a22a58cec995fa78dfade07063b92fd4e48 / merge fb5796e passed 108 Python tests and 1844 native checks with zero compiler warnings/errors. Prior-source upgrade run 46037732290 passed; DAO run 46037732177 blocked at provider activation (no actual DAO extraction). The inventory contains 409 files, 150538037 bytes, self-contained win-x64 runtime, WPF and SQLite. No installer has been produced or qualified.

## Proposed installation boundary (requires owner/security approval)

Prefer an eventual per-user, signed, versioned installer for single-user operation, without admin privileges or machine-level services unless a reviewed requirement changes. Keep installed code in a dedicated local application directory, completely distinct from the production database, development store and synthetic acceptance workspaces. Production data path is intentionally not implemented yet. The current application remains development/acceptance only; never alias these paths or automatically discover/migrate real data. Require trusted local fixed filesystem and access-controlled profile; local machine compromise is outside this model.

Installer shall not open the database, upgrade the schema, copy actual records into program files, overwrite an existing data directory, or remove data on uninstall. No Access/Office dependency for normal core operation; optional migration tool qualification is separate. No automatic remote download, accounting authorization, secrets, or sample database in payload.

Future upgrade sequence: close application; verify signed exact package and independently retained hash; create independently retained recovery copy and rehearse schema upgrade using an isolated copy; stage new program version and validate full file set; activate only after staging succeeds. Do not roll a new-schema live database back by launching an older binary. Recovery requires verified isolated restore and explicit reviewed promotion, never a silent replacement. Preserve previous application and recovery evidence until acceptance. Interrupted installation and rollback, disk full, denied permissions, existing directory, concurrent instance, uninstall/reinstall and clean VM without Access/Office must be exercised before release. No executable distribution or signing attempt while security hold applies.

## Static payload preflight

tools/release/validate_install_payload.py verifies exact inventory JSON bytes against the provided SHA-256, rejects duplicate keys, unsafe Windows-relative paths, reserved names, case aliases, file-directory conflicts, inconsistent counts and unsupported payload names/extensions. Requires application assemblies, WPF and self-contained runtime/SQLite components. Exact executable and JSON filenames permitted; PDBs limited to current application symbols for this development inventory. DLLs are not trusted simply because their names end in DLL. This is a deliberately narrow current layout contract, not a general-purpose installer policy. Future intentional layout changes require reviewed updates. The existing CI inventory script hashes actual published files; the new validator consumes only its JSON in a temporary local file and emits a redacted structural report. It does not independently rehash binary files, verify signatures or authenticity, verify absence of secrets inside binaries, prove clean-machine dependencies or authorize execution. No installation, copying of payload, publishing of artifacts, packaging changes or security bypass. New checks require exact-revision Windows CI verification.

## Gate status

G5 desktop acceptance: 56 procedures defined, none executed interactively. G6: synthetic prior-source upgrade and native recovery proven, installer not implemented or tested. G7: original browser dangerous-file hold remains; independent security/vendor review and exact signed release approval still required. Do not distribute, run the quarantined artifact, or change packaging to evade detection.


## Milestone 10 — actual published byte verification

The earlier layout-only report did not rehash the physical files. CI now passes the actual runner-local publish root and separately generated inventory digest to the read-only Python verifier. It validates the inventory, walks the bounded directory tree, rejects missing/extra files, unlisted directories, redirected entries and unsafe paths, rehashes every file in bounded chunks and checks observed file identity and size before/after reading. It repeats the tree census. A mismatch fails the build. No binary is copied, launched, signed, installed or uploaded. The reported payloadBytesMatchInventory flag is a same-run consistency check, not independent security evidence, a race-proof filesystem boundary or proof of binary authenticity. Independent artifact verification, physical installer, clean-machine qualification and release authorization remain outstanding. Regression tests use text fixtures, not executable binaries.
