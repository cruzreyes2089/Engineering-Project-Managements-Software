"""Offline metadata-only preflight. No Access engine, rows, extraction or imports."""
import argparse
import hashlib
import json
import re
from pathlib import Path

MAX_BYTES = 10 * 1024 * 1024
CLIENT = set("legacyId legacyNumber type company first last email phone address1 address2 city state zip notes status".split())
CONTACT = set("legacyId legacyClientId first last title email phone primary".split())
TEXT = {10, 12}  # DAO dbText/dbMemo
INTEGER = {2, 3, 4, 16}  # DAO byte/integer/long/bigint
IDS = {"legacyId", "legacyClientId"}
REQUIRED = IDS | {"type", "status", "primary"}

def require(condition, message):
    if not condition:
        raise ValueError(message)

def exact(obj, fields):
    require(isinstance(obj, dict) and set(obj) == set(fields), "Unexpected or missing metadata fields")

def pairs(items):
    result = {}
    for key, value in items:
        require(key not in result, "Duplicate JSON key")
        result[key] = value
    return result

def load(path):
    with Path(path).open("rb") as stream:
        data = stream.read(MAX_BYTES + 1)
    require(0 < len(data) <= MAX_BYTES, "Invalid metadata file size")
    return json.loads(data.decode("utf-8"), object_pairs_hook=pairs), hashlib.sha256(data).hexdigest()

def named(items):
    require(isinstance(items, list), "Expected metadata array")
    result = {}
    seen = set()
    for item in items:
        require(isinstance(item, dict) and isinstance(item.get("name"), str) and item["name"].strip(), "Invalid object name")
        name = item["name"]
        require(name.casefold() not in seen, "Ambiguous object names")
        seen.add(name.casefold())
        result[name] = item
    return result

def validate(inventory, inventory_hash, mapping):
    exact(inventory, ["format","sourceSha256","sourceExtension","localTables","localRelations","skippedLinkedTableCount","skippedSystemOrHiddenTableCount","rowDataIncluded","linkedSourcesOpened"])
    require(inventory["format"] == "CRC-Access-Schema-Inventory-v1" and inventory["rowDataIncluded"] is False and inventory["linkedSourcesOpened"] is False, "Metadata-only inventory required")
    require(inventory["sourceExtension"] in (".mdb", ".accdb") and isinstance(inventory["sourceSha256"], str) and re.fullmatch(r"[0-9a-fA-F]{64}", inventory["sourceSha256"]), "Invalid source identity")
    exact(mapping, ["format","inventorySha256","sourceSha256","clients","contacts"])
    require(mapping["format"] == "CRC-Access-CRM-Mapping-v1" and mapping["inventorySha256"] == inventory_hash and mapping["sourceSha256"] == inventory["sourceSha256"], "Mapping is not bound to exact inventory and source")
    tables = named(inventory["localTables"])
    selected = {}
    for entity, expected in (("clients", CLIENT), ("contacts", CONTACT)):
        choice = mapping[entity]
        exact(choice, ["table","fields"])
        require(isinstance(choice["table"], str) and choice["table"] in tables, "Selected table not in local inventory")
        table = tables[choice["table"]]
        columns = named(table["fields"])
        exact(choice["fields"], expected)
        for target, rule in choice["fields"].items():
            exact(rule, ["column","transform"])
            column, transform = rule["column"], rule["transform"]
            if transform == "blank":
                require(column is None and target not in REQUIRED, "Blank rule is not permitted for required target")
                continue
            require(isinstance(column, str) and column in columns, "Mapped column not in selected table")
            dao = columns[column]["daoType"]
            require(type(dao) is int, "Invalid DAO field type")
            if target in IDS:
                require((transform == "text-id" and dao in TEXT) or (transform == "integer-id" and dao in INTEGER), "ID conversion must preserve identifier identity")
            elif target == "primary":
                require(transform == "boolean" and dao == 1, "Primary contact requires explicit DAO Yes/No field")
            elif target in {"type","status"}:
                require(transform == "reviewed-enum" and dao in TEXT | INTEGER | {1}, "Enumeration requires explicit reviewed translation")
            else:
                require(transform == "text" and dao in TEXT, "Text target requires text field; numeric formatting needs separately reviewed mapping")
        key = choice["fields"]["legacyId"]["column"]
        indexes = named(table["indexes"])
        require(any(i.get("unique") is True and i.get("fields") == [key] for i in indexes.values()), "Legacy ID must have a single-column unique index")
        require(columns[key].get("required") is True or any(i.get("primary") is True and i.get("fields") == [key] for i in indexes.values()), "Legacy ID must be nonnullable")
        selected[entity] = (choice["table"], choice["fields"])
    client_table, client_fields = selected["clients"]
    contact_table, contact_fields = selected["contacts"]
    require(client_table != contact_table, "Separate local client and contact tables required")
    require(any(client_fields[x]["transform"] != "blank" for x in ("company","first","last")) and any(contact_fields[x]["transform"] != "blank" for x in ("first","last")), "Name fields must be mapped")
    parent = client_fields["legacyId"]
    child = contact_fields["legacyClientId"]
    require(parent["transform"] == child["transform"], "Parent and child identifiers require matching conversion")
    relations = named(inventory["localRelations"])
    require(any(r.get("primaryTable") == client_table and r.get("foreignTable") == contact_table and r.get("fields") == [{"primaryField":parent["column"],"foreignField":child["column"]}] for r in relations.values()), "Exact declared client-contact relationship required; unverified relationships need separate review")
    return {"status":"MetadataMappingAcceptedForFurtherReview","mappedTargets":len(CLIENT)+len(CONTACT),"rowDataRead":False,"importAuthorized":False}

def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("inventory")
    p.add_argument("mapping")
    args = p.parse_args()
    try:
        inventory, digest = load(args.inventory)
        mapping, _ = load(args.mapping)
        print(json.dumps(validate(inventory, digest, mapping)))
    except (OSError, ValueError, TypeError, KeyError, RecursionError) as exc:
        # Never echo paths, column names, raw input or other private metadata.
        print("Mapping preflight rejected. Review inventory format, exact hashes, field rules, keys and relationship.")
        return 1
    return 0

if __name__ == "__main__":
    raise SystemExit(main())
