"""Validate the structure of private synthetic acceptance records; never authorize release."""
import argparse
import csv
from datetime import date
import json
from pathlib import Path
import re
import sys

ROOT = Path(__file__).resolve().parents[2]
SUPPLEMENTAL = frozenset(("CRC-LC-01", "CRC-LC-02", "CRC-LC-03", "CRC-BILL-01",
                          "CRC-BILL-02", "CRC-ACCT-01", "CRC-REC-01"))
ORIGINAL_COUNTS = {"SEC": 5, "CRM": 1, "ENG": 6, "PRJ": 3, "DOC": 2,
                   "WORK": 2, "QA": 1, "FIN": 10, "REP": 1, "MIG": 3,
                   "REL": 3, "NET": 2, "HR": 1, "UX": 1, "COM": 1}
ORIGINAL = frozenset(f"{prefix}-{number:02d}"
                     for prefix, count in ORIGINAL_COUNTS.items()
                     for number in range(1, count + 1))
STATUSES = ("Pass", "Fail", "Blocked", "Not run")
FIELDS = frozenset(("reviewer", "date", "environment", "session", "fixture",
                    "observed", "evidenceRef"))
LIMIT = 2 * 1024 * 1024

def require(condition, message):
    if not condition:
        raise ValueError(message)

def hex_id(value, length):
    return isinstance(value, str) and re.fullmatch(r"[0-9a-f]{" + str(length) + "}", value) is not None

def nonempty(value):
    return isinstance(value, str) and bool(value.strip()) and len(value) <= 2048

def scenario_ids(path):
    with Path(path).open(encoding="utf-8-sig", newline="") as stream:
        reader = csv.DictReader(stream)
        require(reader.fieldnames == ["Test ID", "Requirement ID", "Fixture/scenario",
                                     "Expected invariant", "Status"], "Invalid scenario register header")
        rows = list(reader)
    ids = [row["Test ID"] for row in rows]
    require(len(ids) == len(ORIGINAL) and set(ids) == ORIGINAL,
            "Original scenario register is incomplete or malformed")
    require(all(None not in row and all(nonempty(value) for value in row.values()) for row in rows),
            "Malformed scenario register row")
    return frozenset(ids) | SUPPLEMENTAL

def validate(manifest, expected_ids, expected_commit, expected_digest):
    require(set(expected_ids) == ORIGINAL | SUPPLEMENTAL, "Invalid expected scenario scope")
    require(hex_id(expected_commit, 40) and hex_id(expected_digest, 64),
            "Expected candidate identifiers must be full lowercase hex")
    require(isinstance(manifest, dict) and set(manifest) ==
            {"format", "commit", "artifactSha256", "syntheticOnly", "results"},
            "Invalid evidence manifest fields")
    require(manifest["format"] == "CRC-Acceptance-Evidence-v1" and
            manifest["syntheticOnly"] is True, "Only synthetic acceptance evidence is supported")
    require(manifest["commit"] == expected_commit and manifest["artifactSha256"] == expected_digest,
            "Evidence candidate does not match the expected candidate")
    rows = manifest["results"]
    require(isinstance(rows, list) and len(rows) == len(expected_ids),
            "Every scenario must have exactly one current result")
    counts = dict.fromkeys(STATUSES, 0)
    seen = set()
    base = {"testId", "status"}
    for row in rows:
        require(isinstance(row, dict) and base <= set(row), "Malformed result")
        ident, status = row["testId"], row["status"]
        require(isinstance(ident, str) and ident in expected_ids and ident not in seen,
                "Unknown or duplicate scenario")
        require(isinstance(status, str) and status in STATUSES, "Invalid result status")
        seen.add(ident)
        counts[status] += 1
        if status in ("Pass", "Fail"):
            required = base | FIELDS | ({"defectId"} if status == "Fail" else set())
            require(set(row) == required, "Executed result lacks required evidence or has extra fields")
            require(all(nonempty(row[k]) for k in required - base), "Empty or malformed evidence field")
            require(hex_id(row["session"], 32), "Invalid acceptance session")
            require(re.fullmatch(r"[0-9]{4}-[0-9]{2}-[0-9]{2}", row["date"]) is not None,
                    "Invalid execution date")
            try:
                date.fromisoformat(row["date"])
            except ValueError:
                raise ValueError("Invalid execution date") from None
        elif status == "Blocked":
            require(set(row) == base | {"reason"} and nonempty(row["reason"]),
                    "Blocked result requires a reason")
        else:
            require(set(row) == base, "Not-run result must not carry execution evidence")
    require(seen == set(expected_ids), "Scenario coverage mismatch")
    return {"format": "CRC-Acceptance-Structure-Report-v1", "counts": counts,
            "structureValid": True, "evidenceAuthenticityVerified": False,
            "releaseAuthorized": False}

def initialize(path, expected_ids, expected_commit, expected_digest):
    manifest = {"format": "CRC-Acceptance-Evidence-v1", "commit": expected_commit,
                "artifactSha256": expected_digest, "syntheticOnly": True,
                "results": [{"testId": ident, "status": "Not run"} for ident in sorted(expected_ids)]}
    validate(manifest, expected_ids, expected_commit, expected_digest)
    # Exclusive creation preserves all previous acceptance evidence.
    with Path(path).open("x", encoding="utf-8", newline="\n") as stream:
        json.dump(manifest, stream, indent=2)
        stream.write("\n")
    return manifest

def unique_keys(pairs):
    result = {}
    for key, value in pairs:
        require(key not in result, "Duplicate JSON key")
        result[key] = value
    return result

def load(path):
    with Path(path).open("rb") as stream:
        raw = stream.read(LIMIT + 1)
    require(len(raw) <= LIMIT, "Evidence file exceeds size limit")
    return json.loads(raw.decode("utf-8"), object_pairs_hook=unique_keys)

def verify_candidate_inventory(path, expected_digest):
    # Import locally so existing structure-only consumers remain independent.
    from validate_build_inventory import read, validate as validate_inventory
    inventory_path = Path(path).resolve()
    require(not inventory_path.is_relative_to(ROOT.resolve()), "Inventory must remain outside the checkout")
    return validate_inventory(read(inventory_path), expected_digest)

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("manifest", type=Path)
    parser.add_argument("--commit", required=True)
    parser.add_argument("--artifact-sha256", required=True, help="Complete deployment inventory SHA-256")
    parser.add_argument("--inventory", required=True, type=Path, help="Private exact compact UTF-8 deployment inventory")
    parser.add_argument("--stage1", action="store_true", help="Summarize Stage 1 scope without granting release authority")
    parser.add_argument("--initialize", action="store_true", help="Create an empty private manifest; never overwrite")
    args = parser.parse_args()
    try:
        private_path = args.manifest.resolve()
        require(not private_path.is_relative_to(ROOT.resolve()), "Evidence must remain outside the checkout")
        ids = scenario_ids(ROOT / "docs/legacy-acceptance-register.csv")
        inventory = verify_candidate_inventory(args.inventory, args.artifact_sha256)
        require(private_path != args.inventory.resolve(), "Evidence and inventory must be separate files")
        if args.initialize:
            initialize(private_path, ids, args.commit, args.artifact_sha256)
        manifest = load(private_path)
        result = validate(manifest, ids, args.commit, args.artifact_sha256)
        if args.stage1:
            from stage1_acceptance_scope import summarize
            result = summarize(manifest, args.commit, args.artifact_sha256)
        result["inventoryStructureValid"] = inventory["structureValid"]
        result["actualFilesRehashed"] = False
        result["evidenceAuthenticityVerified"] = False
    except (ValueError, OSError, UnicodeError, RecursionError, csv.Error):
        print("Acceptance evidence rejected; inspect private input locally.", file=sys.stderr)
        return 2
    print(json.dumps(result, sort_keys=True))
    return 0

if __name__ == "__main__":
    sys.exit(main())
