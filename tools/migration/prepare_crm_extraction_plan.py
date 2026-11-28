"""Prepare a private, metadata-only projection plan for the DAO extraction candidate."""
import argparse
import hashlib
import json
from pathlib import Path
import re
import sys
import validate_crm_mapping as mapping

ROOT = Path(__file__).resolve().parents[2]

def safe_name(name):
    mapping.require(isinstance(name, str) and 0 < len(name) <= 64 and
                    not any(c in name for c in "[]") and
                    not any(ord(c) < 32 or ord(c) == 127 for c in name),
                    "Unsupported DAO identifier")
    return name

def prepare(inventory, inventory_hash, rules, mapping_hash):
    for digest in (inventory_hash, mapping_hash):
        mapping.require(isinstance(digest, str) and re.fullmatch(r"[0-9a-f]{64}", digest),
                        "Invalid input digest")
    mapping.validate(inventory, inventory_hash, rules)
    tables = mapping.named(inventory["localTables"])
    result = {"format": "CRC-Access-CRM-Extraction-Plan-v1",
              "sourceSha256": inventory["sourceSha256"].lower(),
              "inventorySha256": inventory_hash, "mappingSha256": mapping_hash}
    for entity in ("clients", "contacts"):
        choice = rules[entity]
        name = safe_name(choice["table"])
        mapping.require(not name.lower().startswith("msys"), "System table denied")
        columns = mapping.named(tables[name]["fields"])
        selected = sorted({safe_name(r["column"]) for r in choice["fields"].values()
                           if r["transform"] != "blank"})
        identities = {k: choice["fields"][k]["column"] for k in sorted(mapping.IDS & choice["fields"].keys())}
        result[entity] = {"table": name, "columns": [{"name": c, "daoType": columns[c]["daoType"]}
                                                       for c in selected], "identities": identities}
    return result

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("inventory", "mapping", "output"):
        parser.add_argument(name, type=Path)
    args = parser.parse_args()
    try:
        paths = [getattr(args, n).resolve() for n in ("inventory", "mapping", "output")]
        mapping.require(len(set(paths)) == 3 and all(not p.is_relative_to(ROOT.resolve()) for p in paths),
                        "Use distinct private paths outside checkout")
        inv, ih = mapping.load(paths[0])
        rules, mh = mapping.load(paths[1])
        plan = prepare(inv, ih, rules, mh)
        raw = (json.dumps(plan, ensure_ascii=True, sort_keys=True, indent=2) + "\n").encode("utf-8")
        mapping.require(len(raw) <= mapping.MAX_BYTES, "Plan too large")
        with paths[2].open("xb") as output:
            output.write(raw)
        print(json.dumps({"planSha256": hashlib.sha256(raw).hexdigest(),
                          "sourceRead": False, "importAuthorized": False}))
    except (OSError, ValueError, TypeError, KeyError, RecursionError):
        print("Extraction plan rejected; review private metadata locally.", file=sys.stderr)
        return 2
    return 0

if __name__ == "__main__":
    sys.exit(main())
