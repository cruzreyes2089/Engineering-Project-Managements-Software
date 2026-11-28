"""Metadata-only historical project mapping and private extraction plan.

No Access engine, source row access, native project creation or import authorization.
"""
import argparse
import hashlib
import json
from pathlib import Path
import re
import sys
import validate_crm_mapping as crm
from prepare_crm_extraction_plan import safe_name
from historical_project_preflight import IDENTITIES, TEXT_LIMITS, REQUIRED

ROOT = Path(__file__).resolve().parents[2]
FIELDS = IDENTITIES | set(TEXT_LIMITS)
OPTIONAL = {"site", "authorizationEvidenceRef"}
IDS = {"legacyId", "legacyClientId"}


def digest(value):
    crm.require(isinstance(value, str) and re.fullmatch(r"[0-9a-f]{64}", value),
                "Invalid independently recorded digest")
    return value


def prepare(inventory, inventory_hash, crm_rules, crm_mapping_hash, project_rules):
    digest(inventory_hash)
    digest(crm_mapping_hash)
    crm.validate(inventory, inventory_hash, crm_rules)
    crm.exact(project_rules, {"format", "sourceSha256", "inventorySha256", "crmMappingSha256",
                              "projects"})
    source = inventory["sourceSha256"].lower()
    crm.require(project_rules["format"] == "CRC-Access-Historical-Project-Mapping-v1" and
                project_rules["sourceSha256"] == source and
                project_rules["inventorySha256"] == inventory_hash and
                project_rules["crmMappingSha256"] == crm_mapping_hash, "Project mapping binding mismatch")
    tables = crm.named(inventory["localTables"])
    choice = project_rules["projects"]
    crm.exact(choice, {"table", "fields"})
    name = safe_name(choice["table"])
    crm.require(name in tables and not name.lower().startswith("msys") and
                name not in {crm_rules["clients"]["table"], crm_rules["contacts"]["table"]},
                "A separate local project table is required")
    table = tables[name]
    columns = crm.named(table["fields"])
    rules = choice["fields"]
    crm.exact(rules, FIELDS)
    selected = set()
    for target, rule in rules.items():
        crm.exact(rule, {"column", "transform"})
        column, transform = rule["column"], rule["transform"]
        if transform == "blank":
            crm.require(target in OPTIONAL and column is None, "Required historical target cannot be blank")
            continue
        crm.require(isinstance(column, str) and column in columns, "Unknown project column")
        safe_name(column)
        crm.require(column not in selected, "Distinct source column required per mapped project field")
        selected.add(column)
        dao = columns[column]["daoType"]
        crm.require(type(dao) is int, "Invalid DAO type")
        if target in IDS:
            crm.require((transform == "integer-id" and dao in crm.INTEGER) or
                        (transform == "text-id" and dao in crm.TEXT), "Historical ID conversion mismatch")
        else:
            # Preserve literal legacy status labels; numeric/Boolean codes need reviewed mapping,
            # not an inferred authorization or lifecycle.
            crm.require(transform == "text" and dao in crm.TEXT, "Historical text mapping required")
    key = rules["legacyId"]["column"]
    indexes = crm.named(table["indexes"])
    crm.require(any(i.get("unique") is True and i.get("fields") == [key]
                    for i in indexes.values()), "Historical project ID needs a unique single-column index")
    crm.require(columns[key].get("required") is True or
                any(i.get("primary") is True and i.get("fields") == [key]
                    for i in indexes.values()), "Historical project ID must be nonnullable")
    parent = crm_rules["clients"]["fields"]["legacyId"]
    child = rules["legacyClientId"]
    crm.require(child["transform"] == parent["transform"], "Project parent conversion differs from CRM")
    relations = crm.named(inventory["localRelations"])
    crm.require(any(row.get("primaryTable") == crm_rules["clients"]["table"] and
                    row.get("foreignTable") == name and
                    row.get("fields") == [{"primaryField": parent["column"], "foreignField": child["column"]}]
                    for row in relations.values()), "Exact declared client-project relationship required")
    return {"format": "CRC-Access-Historical-Project-Plan-v1", "sourceSha256": source,
            "inventorySha256": inventory_hash, "crmMappingSha256": crm_mapping_hash,
            "table": name, "columns": [{"name": c, "daoType": columns[c]["daoType"]}
                                       for c in sorted(selected)],
            "identityColumns": {k: rules[k]["column"] for k in sorted(IDENTITIES)},
            "rules": rules, "sourceRead": False, "importAuthorized": False}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("inventory", "crm_mapping", "project_mapping", "output"):
        parser.add_argument(name, type=Path)
    args = parser.parse_args()
    try:
        paths = [getattr(args, k).resolve() for k in ("inventory", "crm_mapping", "project_mapping", "output")]
        crm.require(len(set(paths)) == 4 and all(not p.is_relative_to(ROOT) for p in paths),
                    "Use distinct private paths outside checkout")
        (inv, ih), (rules, mh), (project, ph) = [crm.load(p) for p in paths[:3]]
        result = prepare(inv, ih, rules, mh, project)
        result["projectMappingSha256"] = ph
        data = (json.dumps(result, ensure_ascii=True, sort_keys=True, indent=2) + "\n").encode("utf-8")
        crm.require(len(data) <= crm.MAX_BYTES, "Plan exceeds bounds")
        with paths[3].open("xb") as output:
            output.write(data)
        print(json.dumps({"planSha256": hashlib.sha256(data).hexdigest(), "sourceRead": False,
                          "importAuthorized": False}))
    except (OSError, ValueError, TypeError, KeyError, RecursionError):
        print("Historical mapping rejected; inspect private metadata locally.", file=sys.stderr)
        return 2
    return 0


if __name__ == "__main__":
    sys.exit(main())
