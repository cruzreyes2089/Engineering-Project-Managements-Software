"""Reconcile private release gate records and all 56 acceptance cases; never authorize release."""
import argparse
from datetime import date
import json
from pathlib import Path
import sys

import validate_acceptance_evidence as acceptance

GATES = {
    "G1": "Exact revision regression and workflow freeze",
    "G2": "Accounting scope and reconciliation",
    "G3": "Legacy data migration or explicit owner scope decision",
    "G4": "Legal and document acceptance",
    "G5": "Clean-machine desktop acceptance",
    "G6": "Recovery and physical installer qualification",
    "G7": "Independent release security and original warning resolution",
    "G8": "Explicit final release decision",
}
ARTIFACTS = {"packageSha256", "coordinatorSha256", "uninstallerSha256", "desktopSha256", "inventorySha256"}

def validate(record, scenarios, commit, artifacts):
    require = acceptance.require
    require(acceptance.hex_id(commit, 40) and isinstance(artifacts, dict)
            and set(artifacts) == ARTIFACTS
            and all(acceptance.hex_id(v, 64) for v in artifacts.values()), "Invalid exact candidate")
    require(isinstance(record, dict) and set(record) == {"format", "commit", "artifacts", "gates"},
            "Invalid gate record")
    require(record["format"] == "CRC-Release-Gate-Record-v1" and record["commit"] == commit
            and record["artifacts"] == artifacts, "Candidate mismatch")
    result = acceptance.validate(scenarios, acceptance.V2_IDS, commit, artifacts["desktopSha256"])
    rows = record["gates"]
    require(isinstance(rows, list) and len(rows) == len(GATES), "Incomplete gate record")
    statuses = {}
    for row in rows:
        require(isinstance(row, dict) and {"id", "status"} <= set(row), "Invalid gate")
        ident, status = row["id"], row["status"]
        require(isinstance(ident, str) and ident in GATES and ident not in statuses
                and isinstance(status, str) and status in acceptance.STATUSES, "Unknown gate or status")
        required = {"id", "status"}
        if status == "Blocked":
            required.add("reason")
        elif status in ("Pass", "Fail"):
            required |= {"reviewer", "date", "evidenceRef"}
            if status == "Fail":
                required.add("defectId")
        require(set(row) == required and all(acceptance.nonempty(row[k]) for k in required),
                "Gate evidence fields incomplete")
        if status in ("Pass", "Fail"):
            require(date.fromisoformat(row["date"]).isoformat() == row["date"], "Invalid review date")
        statuses[ident] = status
    require(set(statuses) == set(GATES), "Missing gate")
    if statuses["G5"] == "Pass":
        require(result["counts"]["Pass"] == len(acceptance.V2_IDS),
                "Desktop gate conflicts with incomplete acceptance scenarios")
    if statuses["G8"] == "Pass":
        require(all(statuses[k] == "Pass" for k in GATES), "Final decision conflicts with outstanding gates")
    return {"format": "CRC-Release-Gate-Structure-Report-v1", "gateStatuses": statuses,
            "scenarioCounts": result["counts"], "allRecordedPassed": all(v == "Pass" for v in statuses.values()),
            "evidenceAuthenticityVerified": False, "releaseAuthorized": False}

def initialize(path, commit, artifacts):
    record = {"format": "CRC-Release-Gate-Record-v1", "commit": commit, "artifacts": artifacts,
              "gates": [{"id": i, "status": "Not run"} for i in GATES]}
    scenarios = {"format": "CRC-Acceptance-Evidence-v2", "commit": commit,
                 "artifactSha256": artifacts.get("desktopSha256"), "syntheticOnly": True,
                 "results": [{"testId": i, "status": "Not run"} for i in sorted(acceptance.V2_IDS)]}
    validate(record, scenarios, commit, artifacts)
    with Path(path).open("x", encoding="utf-8", newline="\n") as stream:
        json.dump(record, stream, indent=2)
        stream.write("\n")
    return record

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("record", type=Path)
    parser.add_argument("scenarios", type=Path)
    parser.add_argument("--commit", required=True)
    for key in sorted(ARTIFACTS):
        parser.add_argument("--" + key, required=True)
    parser.add_argument("--initialize", action="store_true")
    args = parser.parse_args()
    try:
        record, scenarios = args.record.resolve(), args.scenarios.resolve()
        acceptance.require(record != scenarios and all(not p.is_relative_to(acceptance.ROOT.resolve())
                           for p in (record, scenarios)), "Private separate records required")
        artifacts = {key: getattr(args, key) for key in ARTIFACTS}
        data = acceptance.load(scenarios)
        acceptance.validate(data, acceptance.V2_IDS, args.commit, artifacts["desktopSha256"])
        if args.initialize:
            initialize(record, args.commit, artifacts)
        print(json.dumps(validate(acceptance.load(record), data, args.commit, artifacts), sort_keys=True))
    except (ValueError, OSError, UnicodeError, RecursionError, TypeError):
        print("Release gate record rejected; inspect private input locally.", file=sys.stderr)
        return 2
    return 0

if __name__ == "__main__":
    sys.exit(main())
