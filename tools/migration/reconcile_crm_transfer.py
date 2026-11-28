"""Offline CRM identity reconciliation; no DAO access, database mutation or import approval."""
import argparse
import importlib.util
import json
from pathlib import Path
import re
import sys

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("crm_mapping", Path(__file__).with_name("validate_crm_mapping.py"))
mapping = importlib.util.module_from_spec(spec)
spec.loader.exec_module(mapping)
HASH_KEYS = {"sourceSha256", "inventorySha256", "mappingSha256", "transferSha256"}

def require(condition, message):
    if not condition:
        raise ValueError(message)

def ident(value):
    require(isinstance(value, str) and re.fullmatch(r"[A-Za-z0-9._-]{1,100}", value) is not None,
            "Invalid source identifier")
    return value

def collect(clients, contacts):
    require(isinstance(clients, list) and 1 <= len(clients) <= 5000 and
            isinstance(contacts, list) and len(contacts) <= 10000, "Unsupported row counts")
    client_ids = set()
    client_keys = set()
    contact_map = {}
    contact_keys = set()
    for value in clients:
        key = ident(value)
        require(key.lower() not in client_keys, "Duplicate client identifier")
        client_keys.add(key.lower())
        client_ids.add(key)
    for row in contacts:
        mapping.exact(row, {"legacyId", "legacyClientId"})
        key, parent = ident(row["legacyId"]), ident(row["legacyClientId"])
        require(key.lower() not in contact_keys, "Duplicate contact identifier")
        require(parent.lower() in client_keys, "Orphan contact")
        contact_keys.add(key.lower())
        contact_map[key] = parent
    return client_ids, contact_map

def reconcile(census, transfer, expected_hashes):
    mapping.exact(expected_hashes, HASH_KEYS)
    require(all(isinstance(x, str) and re.fullmatch(r"[0-9a-f]{64}", x) is not None
                for x in expected_hashes.values()), "Invalid expected file identities")
    mapping.exact(census, {"format", "clients", "contacts"} | HASH_KEYS)
    require(census["format"] == "CRC-Access-CRM-Census-v1" and
            all(census[key] == expected_hashes[key] for key in HASH_KEYS),
            "Census does not bind exact source, inventory, mapping and transfer")
    source_ids, source_parents = collect(census["clients"], census["contacts"])
    mapping.exact(transfer, {"format", "clients", "contacts"})
    require(transfer["format"] == "CRC-Legacy-CRM-v1", "Invalid transfer format")
    clients, contacts = transfer["clients"], transfer["contacts"]
    require(isinstance(clients, list) and 1 <= len(clients) <= 5000 and
            isinstance(contacts, list) and len(contacts) <= 10000, "Unsupported transfer counts")
    transfer_ids, transfer_parents = [], []
    primary_parents = set()
    for row in clients:
        mapping.exact(row, mapping.CLIENT)
        require(all(isinstance(value, str) for value in row.values()), "Client values must be text")
        transfer_ids.append(row["legacyId"])
    for row in contacts:
        mapping.exact(row, mapping.CONTACT)
        require(type(row["primary"]) is bool and
                all(isinstance(value, str) for key, value in row.items() if key != "primary"),
                "Contact values must be text and explicit boolean")
        parent = ident(row["legacyClientId"])
        if row["primary"]:
            require(parent.lower() not in primary_parents, "Multiple primary contacts")
            primary_parents.add(parent.lower())
        transfer_parents.append({"legacyId": row["legacyId"], "legacyClientId": parent})
    ids, parents = collect(transfer_ids, transfer_parents)
    require(ids == source_ids and parents == source_parents, "Source and transfer identity mismatch")
    return {"format": "CRC-CRM-Identity-Reconciliation-v1", "clientCount": len(ids),
            "contactCount": len(parents), "identityMatch": True,
            "sourceAuthenticityVerified": False, "fieldValuesReconciled": False,
            "nativeValidationPerformed": False, "importAuthorized": False}

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("inventory", "mapping", "census", "transfer"):
        parser.add_argument(name, type=Path)
    args = parser.parse_args()
    try:
        paths = [getattr(args, name).resolve() for name in ("inventory", "mapping", "census", "transfer")]
        require(len(set(paths)) == 4 and all(not p.is_relative_to(ROOT.resolve()) for p in paths),
                "Use four distinct private files outside the source checkout")
        (inv, inv_hash), (rules, rules_hash), (census, _), (transfer, transfer_hash) = [
            mapping.load(path) for path in paths]
        mapping.validate(inv, inv_hash, rules)
        hashes = {"sourceSha256": inv["sourceSha256"].lower(), "inventorySha256": inv_hash,
                  "mappingSha256": rules_hash, "transferSha256": transfer_hash}
        result = reconcile(census, transfer, hashes)
    except (ValueError, OSError, TypeError, KeyError, RecursionError):
        print("CRM reconciliation rejected; inspect private inputs locally.", file=sys.stderr)
        return 2
    print(json.dumps(result, sort_keys=True))
    return 0

if __name__ == "__main__":
    sys.exit(main())
