"""Bind a private independent identity export to a transfer; no import authorization."""
import argparse
import json
from pathlib import Path
import sys
import validate_crm_mapping as mapping
import prepare_crm_extraction_plan as plans
import convert_crm_rows as conversion

ROOT = Path(__file__).resolve().parents[2]

def bind(inventory, rules, inventory_hash, mapping_hash, raw, transfer, transfer_hash):
    plan = plans.prepare(inventory, inventory_hash, rules, mapping_hash)
    hashes = {key: plan[key] for key in ("sourceSha256", "inventorySha256", "mappingSha256")}
    mapping.exact(raw, {"format", "clients", "contacts"} | set(hashes))
    mapping.require(raw["format"] == "CRC-Access-CRM-Raw-Identity-v1" and
                    all(raw[k] == v for k, v in hashes.items()), "Identity source mismatch")
    result = {"format": "CRC-Access-CRM-Census-v1", **hashes,
              "transferSha256": transfer_hash}
    for entity, limit in (("clients", 5000), ("contacts", 10000)):
        choice = plan[entity]
        types = {x["name"]: x["daoType"] for x in choice["columns"]}
        rows = raw[entity]
        mapping.require(isinstance(rows, list) and len(rows) <= limit, "Invalid identity count")
        converted = []
        for row in rows:
            mapping.exact(row, choice["identities"].keys())
            identities = {}
            for key, column in choice["identities"].items():
                value = conversion.typed(row[key], types[column])
                identities[key] = conversion.reconciliation.ident(str(value))
            converted.append(identities["legacyId"] if entity == "clients" else identities)
        result[entity] = converted
    conversion.reconciliation.reconcile(result, transfer, {**hashes, "transferSha256": transfer_hash})
    return result

def main():
    p = argparse.ArgumentParser(description=__doc__)
    for name in ("inventory", "mapping", "identity_export", "transfer", "output"):
        p.add_argument(name, type=Path)
    args = p.parse_args()
    try:
        paths = [getattr(args, n).resolve() for n in ("inventory", "mapping", "identity_export", "transfer", "output")]
        mapping.require(len(set(paths)) == 5 and all(not p.is_relative_to(ROOT.resolve()) for p in paths),
                        "Use distinct private paths outside checkout")
        (inv, ih), (rules, mh), (raw, _), (transfer, th) = [mapping.load(p) for p in paths[:4]]
        census = bind(inv, rules, ih, mh, raw, transfer, th)
        conversion.write_new(paths[4], census)
    except (OSError, ValueError, TypeError, KeyError, RecursionError):
        print("Identity binding rejected; review private inputs locally.", file=sys.stderr)
        return 2
    print(json.dumps({"clientCount": len(census["clients"]), "contactCount": len(census["contacts"]),
                      "sourceAuthenticityVerified": False, "importAuthorized": False}))
    return 0

if __name__ == "__main__":
    sys.exit(main())
