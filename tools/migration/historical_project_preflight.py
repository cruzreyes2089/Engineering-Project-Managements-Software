"""Offline historical project archive preflight, not operational project import."""
import argparse
from collections import Counter
import json
from pathlib import Path
import re
import sys
import unicodedata
import validate_crm_mapping as mapping
import reconcile_crm_transfer as reconciliation

ROOT = Path(__file__).resolve().parents[2]
HASHES = {"sourceSha256", "crmTransferSha256", "projectTransferSha256"}
IDENTITIES = {"legacyId", "legacyClientId", "legacyProjectNumber"}
TEXT_LIMITS = {"legacyProjectNumber": 100, "title": 200, "site": 300,
               "legacyStatus": 100, "legacyAuthorizationStatus": 100,
               "authorizationEvidenceRef": 200}
REQUIRED = {"legacyProjectNumber", "title", "legacyStatus", "legacyAuthorizationStatus"}

def historical_text(value, key):
    mapping.require(isinstance(value, str) and len(value) <= TEXT_LIMITS[key],
                    "Invalid historical text")
    mapping.require(not any(unicodedata.category(c) in ("Cc", "Cs") for c in value),
                    "Unsupported historical text character")
    mapping.require(key not in REQUIRED or bool(value.strip()), "Required historical text missing")
    # Retain source spelling exactly; no inferred lifecycle or authorization.
    return value

def identity(row):
    return (reconciliation.ident(row["legacyId"]), reconciliation.ident(row["legacyClientId"]),
            historical_text(row["legacyProjectNumber"], "legacyProjectNumber"))

def validate(crm, projects, census, hashes):
    mapping.exact(hashes, HASHES)
    mapping.require(all(isinstance(v, str) and re.fullmatch(r"[0-9a-f]{64}", v)
                        for v in hashes.values()), "Invalid file identities")
    mapping.exact(crm, {"format", "clients", "contacts"})
    mapping.require(crm["format"] == "CRC-Legacy-CRM-v1" and isinstance(crm["clients"], list)
                    and 1 <= len(crm["clients"]) <= 5000, "Invalid CRM reference")
    client_status = {}
    for row in crm["clients"]:
        mapping.exact(row, mapping.CLIENT)
        mapping.require(all(isinstance(v, str) for v in row.values()), "Invalid CRM client values")
        key = reconciliation.ident(row["legacyId"])
        mapping.require(key.lower() not in client_status and row["status"] in ("Active", "Inactive"),
                        "Duplicate or invalid CRM client")
        client_status[key.lower()] = row["status"]
    bindings = HASHES - {"projectTransferSha256"}
    mapping.exact(projects, {"format", "projects"} | bindings)
    mapping.exact(census, {"format", "projects"} | HASHES)
    mapping.require(projects["format"] == "CRC-Legacy-Projects-v1" and
                    census["format"] == "CRC-Legacy-Project-Census-v1" and
                    all(projects[k] == hashes[k] for k in bindings) and
                    all(census[k] == hashes[k] for k in HASHES), "Project binding mismatch")
    def collect(rows, full):
        mapping.require(isinstance(rows, list) and len(rows) <= 10000, "Unsupported project count")
        result = {}
        seen = set()
        numbers = Counter()
        inactive = references = 0
        for row in rows:
            mapping.exact(row, IDENTITIES | (set(TEXT_LIMITS) if full else set()))
            ident, parent, number = identity(row)
            mapping.require(ident.lower() not in seen, "Duplicate project identity")
            mapping.require(parent.lower() in client_status, "Orphan project")
            seen.add(ident.lower())
            result[ident] = (parent, number)
            numbers[number.strip().casefold()] += 1
            inactive += client_status[parent.lower()] == "Inactive"
            if full:
                for key in TEXT_LIMITS:
                    historical_text(row[key], key)
                references += bool(row["authorizationEvidenceRef"].strip())
        return result, numbers, inactive, references
    actual, numbers, inactive, refs = collect(projects["projects"], True)
    original, _, _, _ = collect(census["projects"], False)
    mapping.require(actual == original, "Project identities, relationships or numbers differ from census")
    return {"format": "CRC-Historical-Project-Preflight-v1", "projectCount": len(actual),
            "inactiveClientProjectCount": inactive, "duplicateNumberGroups": sum(n > 1 for n in numbers.values()),
            "historicalEvidenceReferenceCount": refs, "identityMatch": True,
            "sourceAuthenticityVerified": False, "historicalAuthorizationVerified": False,
            "nativeProjectCreationAuthorized": False, "workAuthorized": False,
            "financialMigrationAuthorized": False}

def main():
    p = argparse.ArgumentParser(description=__doc__)
    for name in ("crm_transfer", "project_transfer", "project_census"):
        p.add_argument(name, type=Path)
    p.add_argument("--expected-source-sha256", required=True)
    args = p.parse_args()
    try:
        paths = [getattr(args, n).resolve() for n in ("crm_transfer", "project_transfer", "project_census")]
        mapping.require(len(set(paths)) == 3 and all(not p.is_relative_to(ROOT.resolve()) for p in paths),
                        "Use distinct private files outside checkout")
        (crm, ch), (projects, ph), (census, _) = [mapping.load(p) for p in paths]
        result = validate(crm, projects, census, {"sourceSha256": args.expected_source_sha256,
                          "crmTransferSha256": ch, "projectTransferSha256": ph})
    except (OSError, ValueError, KeyError, TypeError, RecursionError):
        print("Historical project preflight rejected; inspect private inputs locally.", file=sys.stderr)
        return 2
    print(json.dumps(result, sort_keys=True))
    return 0

if __name__ == "__main__":
    sys.exit(main())
