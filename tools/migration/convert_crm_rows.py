"""Convert reviewed private CRM row snapshots; no DAO access or import authorization."""
import argparse
import importlib.util
import json
from pathlib import Path
import re
import sys

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("crm_reconcile", Path(__file__).with_name("reconcile_crm_transfer.py"))
reconciliation = importlib.util.module_from_spec(spec)
spec.loader.exec_module(reconciliation)
mapping = reconciliation.mapping
require = mapping.require
HASHES = {"sourceSha256", "inventorySha256", "mappingSha256", "rowsSha256"}
TARGETS = {
    "type": {"Individual / Homeowner", "Business / Company", "Contractor / Developer",
             "Architect / Designer", "Property Manager / HOA", "Government / Public Agency", "Other"},
    "status": {"Active", "Inactive"},
}
INTEGER_BOUNDS = {2: (0, 255), 3: (-32768, 32767), 4: (-2147483648, 2147483647),
                  16: (-9223372036854775808, 9223372036854775807)}

def typed(value, dao):
    if dao in INTEGER_BOUNDS:
        low, high = INTEGER_BOUNDS[dao]
        require(type(value) is int and low <= value <= high, "Invalid integer")
    elif dao in mapping.TEXT:
        require(isinstance(value, str), "Expected non-null text")
    elif dao == 1:
        require(type(value) is bool, "Expected explicit boolean")
    else:
        raise ValueError("Unsupported source scalar type")
    return value

def enum_lookup(entries, dao, target):
    require(isinstance(entries, list) and 1 <= len(entries) <= 1000, "Invalid enum review")
    result = {}
    for entry in entries:
        mapping.exact(entry, {"source", "target"})
        value = typed(entry["source"], dao)
        require(isinstance(entry["target"], str) and entry["target"] in TARGETS[target],
                "Unsupported native enumeration")
        require(value not in result, "Duplicate enum review value")
        result[value] = entry["target"]
    return result

def convert(inventory, rules, rows, enums, hashes):
    mapping.exact(hashes, HASHES)
    require(all(isinstance(x, str) and re.fullmatch(r"[0-9a-f]{64}", x) for x in hashes.values()),
            "Invalid expected file hashes")
    mapping.validate(inventory, hashes["inventorySha256"], rules)
    require(inventory["sourceSha256"].lower() == hashes["sourceSha256"], "Source hash mismatch")
    binding = HASHES - {"rowsSha256"}
    mapping.exact(rows, {"format", "clients", "contacts"} | binding)
    mapping.exact(enums, {"format", "type", "status"} | HASHES)
    require(rows["format"] == "CRC-Access-CRM-Rows-v1" and
            enums["format"] == "CRC-Access-CRM-Enum-Review-v1" and
            all(rows[k] == hashes[k] for k in binding) and
            all(enums[k] == hashes[k] for k in HASHES), "Snapshot or review binding mismatch")
    require(isinstance(rows["clients"], list) and 1 <= len(rows["clients"]) <= 5000 and
            isinstance(rows["contacts"], list) and len(rows["contacts"]) <= 10000,
            "Snapshot row counts outside supported limits")
    tables = mapping.named(inventory["localTables"])
    output = {"format": "CRC-Legacy-CRM-v1"}
    for entity in ("clients", "contacts"):
        fields = rules[entity]["fields"]
        columns = mapping.named(tables[rules[entity]["table"]]["fields"])
        selected = {r["column"] for r in fields.values() if r["transform"] != "blank"}
        lookups = {k: enum_lookup(enums[k], columns[fields[k]["column"]]["daoType"], k)
                   for k in ("type", "status") if k in fields}
        converted = []
        for ordinal, row in enumerate(rows[entity], 1):
            try:
                mapping.exact(row, selected)
                result = {}
                for target, rule in sorted(fields.items()):
                    transform = rule["transform"]
                    if transform == "blank":
                        result[target] = ""
                        continue
                    column = rule["column"]
                    value = typed(row[column], columns[column]["daoType"])
                    if transform in ("integer-id", "text-id"):
                        value = reconciliation.ident(str(value) if transform == "integer-id" else value)
                    elif transform == "reviewed-enum":
                        require(value in lookups[target], "Unmapped enum")
                        value = lookups[target][value]
                    result[target] = value
                converted.append(result)
            except (ValueError, TypeError, KeyError):
                raise ValueError(f"{entity} row {ordinal}: conversion rejected") from None
        output[entity] = converted
    clients = output["clients"]
    contacts = output["contacts"]
    reconciliation.collect([r["legacyId"] for r in clients],
                           [{k: r[k] for k in ("legacyId", "legacyClientId")} for r in contacts])
    primary = set()
    for row in contacts:
        if row["primary"]:
            key = row["legacyClientId"].lower()
            require(key not in primary, "Multiple primary contacts")
            primary.add(key)
    return output

def write_new(path, transfer):
    raw = (json.dumps(transfer, ensure_ascii=True, separators=(",", ":")) + "\n").encode("utf-8")
    require(len(raw) <= mapping.MAX_BYTES, "Transfer exceeds size limit")
    with Path(path).open("xb") as stream:
        stream.write(raw)

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("inventory", "mapping", "rows", "enum_review", "output"):
        parser.add_argument(name, type=Path)
    args = parser.parse_args()
    try:
        paths = [getattr(args, key).resolve() for key in ("inventory", "mapping", "rows", "enum_review", "output")]
        require(len(set(paths)) == 5 and all(not p.is_relative_to(ROOT.resolve()) for p in paths),
                "Use distinct private paths outside checkout")
        (inv, inv_hash), (rules, rules_hash), (rows, rows_hash), (enums, _) = [
            mapping.load(path) for path in paths[:4]]
        hashes = {"sourceSha256": inv["sourceSha256"].lower(), "inventorySha256": inv_hash,
                  "mappingSha256": rules_hash, "rowsSha256": rows_hash}
        transfer = convert(inv, rules, rows, enums, hashes)
        write_new(paths[4], transfer)
    except (OSError, ValueError, TypeError, KeyError, RecursionError):
        print("CRM conversion rejected; review private inputs locally.", file=sys.stderr)
        return 2
    print(json.dumps({"clientCount": len(transfer["clients"]), "contactCount": len(transfer["contacts"]),
                      "nativeValidationPerformed": False, "sourceAuthenticityVerified": False,
                      "importAuthorized": False}, sort_keys=True))
    return 0

if __name__ == "__main__":
    sys.exit(main())
