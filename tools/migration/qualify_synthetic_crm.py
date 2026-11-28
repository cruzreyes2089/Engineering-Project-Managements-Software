"""Opt-in Windows DAO qualification with optional isolated native synthetic import."""
import argparse
import copy
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import uuid

import validate_crm_mapping as mapping
import prepare_crm_extraction_plan as plans
import convert_crm_rows as conversion
import bind_crm_source_census as binder

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]

def require(condition, label):
    if not condition:
        raise ValueError(label)

def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()

def write(path, value):
    with path.open("x", encoding="utf-8", newline="\n") as f:
        json.dump(value, f, ensure_ascii=True, sort_keys=True)
        f.write("\n")

def fixture_mapping(inv, ih, shared=False):
    def fields(targets, selected):
        return {k: {"column": selected[k][0], "transform": selected[k][1]}
                if k in selected else {"column": None, "transform": "blank"} for k in targets}
    return {"format": "CRC-Access-CRM-Mapping-v1", "inventorySha256": ih,
            "sourceSha256": inv["sourceSha256"],
            "clients": {"table": "SyntheticClients", "fields": fields(mapping.CLIENT, {
                "legacyId": ("Id", "text-id"), "company": ("Name", "text"),
                "type": ("Kind", "reviewed-enum"), "status": ("Status", "reviewed-enum"),
                "zip": ("Postal", "text")})},
            "contacts": {"table": "SyntheticContacts", "fields": fields(mapping.CONTACT, {
                "legacyId": ("Id", "text-id" if shared else "integer-id"),
                "legacyClientId": ("Id" if shared else "Parent", "text-id"),
                "first": ("Name", "text"), "primary": ("PrimaryFlag", "boolean")})}}

def expected_transfer(empty, shared=False):
    def client(ident, name, kind, status, postal):
        row = dict.fromkeys(mapping.CLIENT, "")
        row.update(legacyId=ident, company=name, type=kind, status=status, zip=postal)
        return row
    def contact(ident, parent, name, primary):
        row = dict.fromkeys(mapping.CONTACT, "")
        row.update(legacyId=ident, legacyClientId=parent, first=name, primary=primary)
        return row
    return {"format": "CRC-Legacy-CRM-v1", "clients": [
        client("001", "Synthetic Alpha", "Business / Company", "Active", "00501"),
        client("002", "Synthetic Beta", "Architect / Designer", "Inactive", "00999")],
        "contacts": [] if empty else [contact("001" if shared else "11", "001", "Synthetic First", True),
                                      contact("002" if shared else "12", "002", "Synthetic Second", False)]}

def compare_values(actual, expected):
    mapping.exact(actual, {"format", "clients", "contacts"})
    require(actual["format"] == expected["format"], "Transfer format")
    for entity, fields in (("clients", mapping.CLIENT), ("contacts", mapping.CONTACT)):
        rows = actual[entity]
        wanted = expected[entity]
        require(isinstance(rows, list) and len(rows) == len(wanted),
                "Full normalized fixture row count: " + entity)
        # JSON booleans are not integers, despite Python True == 1.
        for row in rows:
            mapping.exact(row, fields)
            require(all(type(value) is (bool if key == "primary" else str)
                        for key, value in row.items()), "Fixture scalar type: " + entity)
        require(sorted(rows, key=lambda r: r["legacyId"]) ==
                sorted(wanted, key=lambda r: r["legacyId"]),
                "Full normalized fixture field comparison: " + entity)

def native_mode(empty, shared):
    require(not (empty and shared), "Distinct synthetic fixture modes required")
    return "empty" if empty else "shared" if shared else "populated"

def qualify(folder, powershell, empty, native_smoke=False, shared=False):
    require(not (empty and shared), "Distinct synthetic fixture modes required")
    checks = []
    def check(condition, label):
        require(condition, label)
        checks.append(label)
    def ps(script, args, success=True):
        result = subprocess.run([str(powershell), "-NoProfile", "-NonInteractive",
                                 "-File", str(HERE / script), *map(str, args)],
                                stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=120)
        require((result.returncode == 0) == success, "Unexpected execution result: " + script)
    source = folder / "SyntheticCRM.accdb"
    fixture_args = ["-OutputDirectory", folder, "-ConfirmSyntheticFixture"]
    if empty:
        fixture_args += ["-EmptyContacts"]
    if shared:
        fixture_args += ["-SharedContactKey"]
    ps("New-SyntheticLegacyCrmFixture.ps1", fixture_args)
    before = digest(source)
    ps("New-SyntheticLegacyCrmFixture.ps1", fixture_args, False)
    check(digest(source) == before, "Fixture overwrite denied")
    ip = folder / "inventory.json"
    ps("Inspect-LegacyAccessSchema.ps1", ["-DatabaseCopy", source, "-OutputFile", ip,
                                         "-ConfirmTrustedCopy"])
    inv, ih = mapping.load(ip)
    check(inv["sourceSha256"].lower() == before == digest(source), "Inventory source unchanged")
    rules = fixture_mapping(inv, ih, shared)
    rp = folder / "mapping.json"
    write(rp, rules)
    mh = digest(rp)
    plan = plans.prepare(inv, ih, rules, mh)
    pp = folder / "plan.json"
    write(pp, plan)
    ph = digest(pp)
    args = ["-DatabaseCopy", source, "-PlanFile", pp, "-ConfirmTrustedSyntheticCopy"]
    def export(mode, output, expected=ph, success=True):
        ps("Export-LegacyCrmSnapshot.ps1",
           args + ["-ExpectedPlanSha256", expected, "-Mode", mode, "-OutputFile", output], success)
    identity_path = folder / "identities.json"
    export("Identity", identity_path)
    identity_hash = digest(identity_path)
    rows_path = folder / "rows.json"
    export("Rows", rows_path)
    check(digest(source) == before, "Separate read-only projections preserve source")
    rows, rh = mapping.load(rows_path)
    raw, _ = mapping.load(identity_path)
    check(len(rows["clients"]) == 2 and len(rows["contacts"]) == (0 if empty else 2),
          "Actual extracted row counts")
    for entity in ("clients", "contacts"):
        allowed = {x["name"] for x in plan[entity]["columns"]}
        check(all(set(r) == allowed for r in rows[entity]), "Only mapped columns: " + entity)
    hashes = {"sourceSha256": before, "inventorySha256": ih, "mappingSha256": mh,
              "rowsSha256": rh}
    enums = {"format": "CRC-Access-CRM-Enum-Review-v1", **hashes,
             "type": [{"source": 7, "target": "Business / Company"},
                      {"source": 9, "target": "Architect / Designer"}],
             "status": [{"source": True, "target": "Active"},
                        {"source": False, "target": "Inactive"}]}
    transfer = conversion.convert(inv, rules, rows, enums, hashes)
    compare_values(transfer, expected_transfer(empty, shared))
    checks.append("All normalized fields match fixed expected fixture")
    tp = folder / "transfer.json"
    conversion.write_new(tp, transfer)
    th = digest(tp)
    census = binder.bind(inv, rules, ih, mh, raw, transfer, th)
    result = conversion.reconciliation.reconcile(census, transfer,
        {k: census[k] for k in conversion.reconciliation.HASH_KEYS})
    check(result["identityMatch"] and not result["importAuthorized"], "Independent identity reconciliation")
    check(digest(identity_path) == identity_hash, "Identity export unchanged")
    if shared:
        check(plan["contacts"]["identities"] == {"legacyId": "Id", "legacyClientId": "Id"} and
              all(type(r["legacyId"]) is str and r["legacyId"] == r["legacyClientId"]
                  for r in raw["contacts"]), "Shared contact key retained in both identity roles")
    old_rows = digest(rows_path)
    export("Rows", rows_path, success=False)
    check(digest(rows_path) == old_rows, "Existing snapshot protected")
    stale = folder / "stale.json"
    export("Rows", stale, "0"*64, False)
    check(not stale.exists(), "Stale plan digest rejected without output")
    wrong_plan = copy.deepcopy(plan)
    wrong_plan["sourceSha256"] = "0"*64
    wrong_plan_path = folder / "wrong-source-plan.json"
    write(wrong_plan_path, wrong_plan)
    wrong_output = folder / "wrong-source.json"
    ps("Export-LegacyCrmSnapshot.ps1",
       ["-DatabaseCopy", source, "-PlanFile", wrong_plan_path,
        "-ExpectedPlanSha256", digest(wrong_plan_path), "-Mode", "Rows",
        "-OutputFile", wrong_output, "-ConfirmTrustedSyntheticCopy"], False)
    check(not wrong_output.exists(), "Stale source digest rejected without output")
    # Only synthetic files created by this run are used. A marker exercises
    # lock-file refusal, not genuine competing DAO session behavior.
    def inspect(output, success=False):
        ps("Inspect-LegacyAccessSchema.ps1", ["-DatabaseCopy", source, "-OutputFile", output,
                                             "-ConfirmTrustedCopy"], success)
    for ext in (".ldb", ".laccdb"):
        lock = source.with_suffix(ext)
        with lock.open("xb") as stream:
            stream.write(b"CRC synthetic lock sentinel")
        try:
            blocked_inventory = folder / ("locked-inventory" + ext + ".json")
            blocked_rows = folder / ("locked-rows" + ext + ".json")
            inspect(blocked_inventory)
            export("Rows", blocked_rows, success=False)
            check(not blocked_inventory.exists() and not blocked_rows.exists(),
                  "Both scripts reject lock sentinel " + ext)
        finally:
            lock.unlink()
    # Use a random new checkout filename. If a regression creates it, remove
    # only that synthetic output; never overwrite an existing checkout file.
    for operation in ("inventory", "rows"):
        checkout_output = ROOT / ("crc-synthetic-rejection-" + uuid.uuid4().hex + ".json")
        require(not checkout_output.exists(), "Unexpected checkout test path collision")
        try:
            if operation == "inventory":
                inspect(checkout_output)
            else:
                export("Rows", checkout_output, success=False)
            check(not checkout_output.exists(), "Checkout output denied: " + operation)
        finally:
            if checkout_output.is_file():
                checkout_output.unlink()
    # The base is an existing synthetic file; the proposed alternate stream
    # does not exist. Check by opening the stream, not Path.exists alone.
    base_file = folder / "stream-base.txt"
    with base_file.open("xb") as stream:
        stream.write(b"CRC synthetic stream base")
    base_hash = digest(base_file)
    for operation in ("inventory", "rows"):
        ads = str(base_file) + ":crc-" + operation + ".json"
        if operation == "inventory":
            inspect(ads)
        else:
            export("Rows", ads, success=False)
        try:
            with open(ads, "rb"):
                raise ValueError("Unexpected alternate stream created")
        except FileNotFoundError:
            pass
        check(digest(base_file) == base_hash, "Alternate stream denied: " + operation)
    modified = copy.deepcopy(transfer)
    modified["clients"][0]["legacyId"] = "SUBSTITUTED"
    try:
        binder.bind(inv, rules, ih, mh, raw, modified, "a"*64)
    except ValueError:
        checks.append("Substituted identity rejected")
    else:
        raise ValueError("Substituted identity accepted")
    if native_smoke:
        mode = native_mode(empty, shared)
        run = subprocess.run(["dotnet", "run", "--project",
                              str(ROOT / "tests/PracticeManager.SmokeTests/PracticeManager.SmokeTests.csproj"),
                              "-c", "Release", "--no-build", "--no-restore", "--",
                              "--synthetic-crm-transfer", mode, str(tp), th],
                             stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=180)
        # Require both successful exit and digest-bound native completion marker.
        marker = ("; transfer SHA-256: " + th).encode("ascii")
        check(run.returncode == 0 and any(line.startswith(b"Synthetic CRM native checks passed: ") and
              line.endswith(marker) for line in run.stdout.splitlines()),
              "Native fixed-fixture import and rollback checks")
        check(digest(tp) == th, "Native run preserved exact transfer bytes")
    check(digest(source) == before, "Final source hash unchanged")
    return checks

def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--confirm-synthetic", action="store_true")
    p.add_argument("--dao-bitness", choices=("32", "64"), required=True)
    p.add_argument("--native-smoke", action="store_true", help="Run separately reviewed, already built native smoke harness")
    args = p.parse_args()
    if os.name != "nt" or not args.confirm_synthetic:
        print("Blocked: Windows and explicit synthetic qualification confirmation required.", file=sys.stderr)
        return 2
    try:
        system = Path(os.environ["SystemRoot"])
        is64 = (system / "SysWOW64").is_dir()
        require(args.dao_bitness != "64" or is64, "64-bit Windows required")
        # Sysnative exposes native System32 to a 32-bit Python process on 64-bit Windows.
        directory = ("SysWOW64" if is64 and args.dao_bitness == "32"
                     else "Sysnative" if is64 and sys.maxsize <= 2**32 else "System32")
        powershell = system / directory / "WindowsPowerShell/v1.0/powershell.exe"
        require(powershell.is_file(), "Matching Windows PowerShell not available")
        with tempfile.TemporaryDirectory(prefix="CRC-synthetic-CRM-") as td:
            base = Path(td).resolve()
            require(not base.is_relative_to(ROOT.resolve()), "Private temp directory required")
            runs = {}
            for label, empty, shared in (("populated", False, False),
                                         ("emptyContacts", True, False),
                                         ("sharedContactKey", False, True)):
                folder = base / label
                folder.mkdir()
                runs[label] = qualify(folder, powershell, empty, args.native_smoke, shared)
        print(json.dumps({"checks": runs, "daoFixtureChecksPassed": True,
                          "nativeImportExecuted": args.native_smoke,
                          "nativeCases": ["populated", "emptyContacts", "sharedContactKey"] if args.native_smoke else [],
                          "sharedKeyNativeQualified": args.native_smoke, "releaseAuthorized": False}, sort_keys=True))
        return 0
    except (OSError, ValueError, KeyError, TypeError, subprocess.TimeoutExpired):
        print("Synthetic DAO qualification did not complete successfully; inspect local prerequisites.", file=sys.stderr)
        return 2

if __name__ == "__main__":
    sys.exit(main())
