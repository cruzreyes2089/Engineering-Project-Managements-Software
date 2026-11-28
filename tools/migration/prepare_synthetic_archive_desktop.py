"""Generate fixed synthetic desktop test inputs, never actual legacy data or release evidence."""
import argparse
import hashlib
import json
from pathlib import Path
import sys

import historical_project_preflight as preflight
from qualify_synthetic_crm import expected_transfer

ROOT = Path(__file__).resolve().parents[2]

def encode(value):
    return (json.dumps(value, sort_keys=True, ensure_ascii=True, indent=2) + "\n").encode("utf-8")

def sha(data):
    return hashlib.sha256(data).hexdigest()

def package():
    # This is an explicitly synthetic provenance token, not an Access file digest.
    source = b"CRC synthetic archive desktop fixture v1; not an Access database\n"
    crm = expected_transfer(False)
    crm_bytes = encode(crm)
    bindings = {"sourceSha256": sha(source), "crmTransferSha256": sha(crm_bytes)}
    projects = {"format": "CRC-Legacy-Projects-v1", **bindings, "projects": [
        {"legacyId": "P01", "legacyClientId": "001", "legacyProjectNumber": "OLD-001",
         "title": "Synthetic project", "site": "", "legacyStatus": "Active",
         "legacyAuthorizationStatus": "Approved", "authorizationEvidenceRef": "Ref-01"},
        {"legacyId": "P02", "legacyClientId": "002", "legacyProjectNumber": "old-001",
         "title": " Historical title ", "site": "Synthetic site", "legacyStatus": "Closed",
         "legacyAuthorizationStatus": "Unknown", "authorizationEvidenceRef": ""}]}
    project_bytes = encode(projects)
    hashes = {**bindings, "projectTransferSha256": sha(project_bytes)}
    # Fixed separately declared expected identities, not projected from transfer.
    census = {"format": "CRC-Legacy-Project-Census-v1", **hashes, "projects": [
        {"legacyId": "P02", "legacyClientId": "002", "legacyProjectNumber": "old-001"},
        {"legacyId": "P01", "legacyClientId": "001", "legacyProjectNumber": "OLD-001"}]}
    census_bytes = encode(census)
    report = preflight.validate(crm, projects, census, hashes)
    # Independently hash-bound but wrong parent; tests identity reconciliation rather than a stale file hash.
    wrong_census = {**census, "projects": [
        dict(census["projects"][0]), {**census["projects"][1], "legacyClientId": "002"}]}
    try:
        preflight.validate(crm, projects, wrong_census, hashes)
    except ValueError:
        pass
    else:
        raise ValueError("Negative fixture was unexpectedly accepted")
    wrong_bytes = encode(wrong_census)
    data = {"synthetic-source-marker.txt": source, "crm.json": crm_bytes,
            "projects.json": project_bytes, "census.json": census_bytes,
            "wrong-parent-census.json": wrong_bytes}
    manifest = {"format": "CRC-Synthetic-Archive-Desktop-v1", "syntheticOnly": True,
                "sourceIsAccessDatabase": False, "sourceAuthenticityVerified": False,
                "desktopExecuted": False, "workAuthorized": False, "releaseAuthorized": False,
                "hashes": {**hashes, "censusSha256": sha(census_bytes)},
                "negativeCensusSha256": sha(wrong_bytes),
                "fileSha256": {name: sha(value) for name, value in data.items()},
                "expected": report}
    data["fixture-manifest.json"] = encode(manifest)
    return data

def generate(destination):
    path = Path(destination)
    # Resolve the parent, require a new directory and keep fixture outside checkout.
    parent = path.parent.resolve(strict=True)
    target = parent / path.name
    if not path.name or path.name in (".", "..") or not parent.is_dir() or target.resolve().is_relative_to(ROOT.resolve()):
        raise ValueError("New private directory outside checkout required")
    data = package()
    target.mkdir(exist_ok=False)
    for name, value in data.items():
        with (target / name).open("xb") as f:
            f.write(value)
    return target

def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("destination", type=Path)
    p.add_argument("--confirm-synthetic", action="store_true")
    args = p.parse_args()
    if not args.confirm_synthetic:
        print("Explicit synthetic fixture confirmation required.", file=sys.stderr)
        return 2
    try:
        generate(args.destination)
    except (OSError, ValueError, RuntimeError):
        print("Synthetic desktop fixture preparation failed; inspect the private directory locally.", file=sys.stderr)
        return 2
    print("Fixed synthetic desktop fixture prepared; no Access source or desktop execution verified.")
    return 0

if __name__ == "__main__":
    sys.exit(main())
