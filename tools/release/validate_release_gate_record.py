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
    "G7": "Mandatory Microsoft Defender and exact-candidate security qualification",
    "G8": "Explicit final release decision",
}
ARTIFACTS = {"packageSha256", "coordinatorSha256", "uninstallerSha256", "desktopSha256", "inventorySha256"}

ACCESS_DEFERRED = frozenset(("MIG-01", "MIG-02", "MIG-03"))

def scope_for(approval_ref):
    acceptance.require(acceptance.nonempty(approval_ref), "Independent owner scope reference required")
    return {"policy": "Fresh-firm-no-Access-migration", "approvalRef": approval_ref,
            "deferredIds": sorted(ACCESS_DEFERRED)}

def validate_defender(evidence, commit, artifacts):
    """Structural evidence only; does not run Defender or authenticate a report."""
    require = acceptance.require
    flags = {"antivirusEnabled", "serviceEnabled", "realTimeProtectionEnabled",
             "scanCompleted", "bytesUnchanged", "securityControlsUnchanged"}
    fields = flags | {"sourceCommit", "artifacts", "engineVersion", "definitionVersion",
                      "definitionsAgeHours", "detections", "remediations", "evidenceRef"}
    require(isinstance(evidence, dict) and set(evidence) == fields,
            "Mandatory exact-candidate Defender evidence missing or malformed")
    require(evidence["sourceCommit"] == commit and evidence["artifacts"] == artifacts,
            "Defender evidence is not bound to the exact release candidate")
    require(all(evidence[key] is True for key in flags), "Defender protections or scan not verified")
    require(all(type(evidence[key]) is int and evidence[key] == 0
                for key in ("detections", "remediations")), "Defender finding requires resolution")
    age = evidence["definitionsAgeHours"]
    require(type(age) is int and 0 <= age <= 48, "Defender definitions not current")
    require(all(acceptance.nonempty(evidence[key]) for key in
                ("engineVersion", "definitionVersion", "evidenceRef")),
            "Defender scan details required")

def validate(record, scenarios, commit, artifacts, approval_ref=None):
    require = acceptance.require
    require(acceptance.hex_id(commit, 40) and isinstance(artifacts, dict)
            and set(artifacts) == ARTIFACTS
            and all(acceptance.hex_id(v, 64) for v in artifacts.values()), "Invalid exact candidate")
    scoped = approval_ref is not None
    fields = {"format", "commit", "artifacts", "gates"} | ({"scope"} if scoped else set())
    require(isinstance(record, dict) and set(record) == fields, "Invalid gate record")
    require(record["format"] == ("CRC-Release-Gate-Record-v2" if scoped else "CRC-Release-Gate-Record-v1")
            and record["commit"] == commit and record["artifacts"] == artifacts, "Candidate mismatch")
    if scoped:
        require(record["scope"] == scope_for(approval_ref), "Unapproved or expanded scope deferral")
    deferred = ACCESS_DEFERRED if scoped else frozenset()
    result = acceptance.validate(scenarios, acceptance.V2_IDS, commit, artifacts["desktopSha256"])
    rows = record["gates"]
    require(isinstance(rows, list) and len(rows) == len(GATES), "Incomplete gate record")
    statuses = {}
    for row in rows:
        require(isinstance(row, dict) and {"id", "status"} <= set(row), "Invalid gate")
        ident, status = row["id"], row["status"]
        require(isinstance(ident, str) and ident in GATES and ident not in statuses
                and isinstance(status, str) and (status in acceptance.STATUSES or
                    (scoped and ident == "G3" and status == "Deferred")), "Unknown gate or status")
        required = {"id", "status"}
        if status == "Blocked":
            required.add("reason")
        elif status == "Deferred":
            # A reviewer must record evidence that the unqualified production
            # import is unavailable; the owner decision alone is insufficient.
            required |= {"reviewer", "date", "evidenceRef", "importDisabledEvidenceRef"}
        elif status in ("Pass", "Fail"):
            required |= {"reviewer", "date", "evidenceRef"}
            if status == "Fail":
                required.add("defectId")
            if ident == "G7" and status == "Pass":
                required.add("defender")
        require(set(row) == required and all(acceptance.nonempty(row[k]) for k in required - {"defender"}),
                "Gate evidence fields incomplete")
        if ident == "G7" and status == "Pass":
            validate_defender(row["defender"], commit, artifacts)
        if status in ("Pass", "Fail", "Deferred"):
            require(date.fromisoformat(row["date"]).isoformat() == row["date"], "Invalid review date")
        statuses[ident] = status
    require(set(statuses) == set(GATES), "Missing gate")
    required_rows = [row for row in scenarios["results"] if row["testId"] not in deferred]
    required_pass = all(row["status"] == "Pass" for row in required_rows)
    if scoped:
        require(statuses["G3"] != "Pass", "Deferred migration is not qualified")
    if statuses["G5"] == "Pass":
        require(required_pass, "Desktop gate conflicts with incomplete in-scope acceptance scenarios")
    gates_satisfied = all(statuses[k] == ("Deferred" if scoped and k == "G3" else "Pass") for k in GATES)
    if statuses["G8"] == "Pass":
        require(gates_satisfied, "Final decision conflicts with outstanding gates")
    report = {"format": "CRC-Release-Gate-Structure-Report-v2" if scoped else "CRC-Release-Gate-Structure-Report-v1",
              "gateStatuses": statuses, "scenarioCounts": result["counts"],
              "allRecordedPassed": all(v == "Pass" for v in statuses.values()),
              "evidenceAuthenticityVerified": False, "releaseAuthorized": False}
    if scoped:
        report.update(deferredIds=sorted(deferred), requiredScenarioCount=len(required_rows),
                      requiredScenariosPassed=sum(row["status"] == "Pass" for row in required_rows),
                      deferredScenarioStatuses={row["testId"]: row["status"] for row in scenarios["results"] if row["testId"] in deferred},
                      scopedGateRecordsComplete=gates_satisfied)
    return report

def initialize(path, commit, artifacts, approval_ref=None):
    record = {"format": "CRC-Release-Gate-Record-v2" if approval_ref is not None else "CRC-Release-Gate-Record-v1",
              "commit": commit, "artifacts": artifacts,
              "gates": [{"id": i, "status": "Not run"} for i in GATES]}
    if approval_ref is not None:
        record["scope"] = scope_for(approval_ref)
    scenarios = {"format": "CRC-Acceptance-Evidence-v2", "commit": commit,
                 "artifactSha256": artifacts.get("desktopSha256"), "syntheticOnly": True,
                 "results": [{"testId": i, "status": "Not run"} for i in sorted(acceptance.V2_IDS)]}
    validate(record, scenarios, commit, artifacts, approval_ref)
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
    parser.add_argument("--access-deferral-approval-ref", help="Independently retained owner scope reference; not authentication")
    args = parser.parse_args()
    try:
        record, scenarios = args.record.resolve(), args.scenarios.resolve()
        acceptance.require(record != scenarios and all(not p.is_relative_to(acceptance.ROOT.resolve())
                           for p in (record, scenarios)), "Private separate records required")
        artifacts = {key: getattr(args, key) for key in ARTIFACTS}
        data = acceptance.load(scenarios)
        acceptance.validate(data, acceptance.V2_IDS, args.commit, artifacts["desktopSha256"])
        if args.initialize:
            initialize(record, args.commit, artifacts, args.access_deferral_approval_ref)
        print(json.dumps(validate(acceptance.load(record), data, args.commit, artifacts, args.access_deferral_approval_ref), sort_keys=True))
    except (ValueError, OSError, UnicodeError, RecursionError, TypeError):
        print("Release gate record rejected; inspect private input locally.", file=sys.stderr)
        return 2
    return 0

if __name__ == "__main__":
    sys.exit(main())
