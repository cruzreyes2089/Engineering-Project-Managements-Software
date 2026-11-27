"""Convert reviewed local project projections and an independently collected identity census.

This offline tool reads private JSON only. It never opens Access, imports data or authorizes work.
"""
import argparse
import hashlib
import json
from pathlib import Path
import sys
import validate_crm_mapping as crm
import historical_project_preflight as preflight
import prepare_historical_project_plan as plan_tools
import reconcile_crm_transfer as reconciliation
from convert_crm_rows import typed

ROOT = Path(__file__).resolve().parents[2]


def raw_json(value):
    return (json.dumps(value, ensure_ascii=True, sort_keys=True, indent=2) + "\n").encode("utf-8")


def encode(value):
    data = raw_json(value)
    crm.require(0 < len(data) <= crm.MAX_BYTES, "Output exceeds bounds")
    return data


def convert(inventory, inventory_hash, crm_rules, crm_mapping_hash, project_rules,
            project_mapping_hash, plan, plan_hash, raw, raw_hash, independent, census_hash,
            crm_transfer, crm_hash, expected):
    crm.exact(expected, {"planSha256", "projectionSha256", "identityCensusSha256", "crmTransferSha256"})
    for key, actual in (("planSha256", plan_hash), ("projectionSha256", raw_hash),
                        ("identityCensusSha256", census_hash), ("crmTransferSha256", crm_hash)):
        crm.require(plan_tools.digest(expected[key]) == actual, "Independent file digest mismatch")
    verified_plan = plan_tools.prepare(inventory, inventory_hash, crm_rules, crm_mapping_hash, project_rules)
    verified_plan["projectMappingSha256"] = plan_tools.digest(project_mapping_hash)
    crm.require(plan == verified_plan, "Extraction plan differs from reviewed metadata")
    bindings = {"sourceSha256": plan["sourceSha256"], "planSha256": plan_hash}
    for item, fmt in ((raw, "CRC-Access-Historical-Project-Projection-v1"),
                      (independent, "CRC-Access-Historical-Project-Identity-Census-v1")):
        crm.exact(item, {"format", "sourceSha256", "planSha256", "rows"})
        crm.require(item["format"] == fmt and all(item[k] == v for k, v in bindings.items()),
                    "Projection or census source binding mismatch")
        crm.require(isinstance(item["rows"], list) and 1 <= len(item["rows"]) <= 10000,
                    "Nonempty bounded historical rows required")

    types = {column["name"]: column["daoType"] for column in plan["columns"]}

    def normalize(row, identities_only):
        targets = preflight.IDENTITIES if identities_only else plan_tools.FIELDS
        rules = {key: plan["rules"][key] for key in targets}
        crm.exact(row, {rule["column"] for rule in rules.values() if rule["transform"] != "blank"})
        result = {}
        for target, rule in rules.items():
            transform = rule["transform"]
            value = "" if transform == "blank" else typed(row[rule["column"]], types[rule["column"]])
            if target in plan_tools.IDS:
                if transform == "integer-id":
                    crm.require(type(value) is int and -(2**63) <= value < 2**63,
                                "Integer identity must be exact")
                    value = str(value)
                result[target] = reconciliation.ident(value)
            else:
                # Null optional text is not silently converted: the reviewer must explicitly
                # represent an absent optional value as empty text or an approved blank mapping.
                result[target] = preflight.historical_text(value, target)
        return result

    full = [normalize(row, False) for row in raw["rows"]]
    ids = [normalize(row, True) for row in independent["rows"]]
    base = {"sourceSha256": plan["sourceSha256"], "crmTransferSha256": crm_hash}
    project = {"format": "CRC-Legacy-Projects-v1", **base, "projects": full}
    project_bytes = encode(project)
    project_hash = hashlib.sha256(project_bytes).hexdigest()
    census = {"format": "CRC-Legacy-Project-Census-v1", **base,
              "projectTransferSha256": project_hash, "projects": ids}
    census_bytes = encode(census)
    result = preflight.validate(crm_transfer, project, census,
                                {**base, "projectTransferSha256": project_hash})
    result.update({"projectTransferSha256": project_hash,
                   "censusSha256": hashlib.sha256(census_bytes).hexdigest(),
                   "sourceAuthenticityVerified": False, "independentCollectionVerified": False,
                   "importAuthorized": False})
    return project_bytes, census_bytes, result


def main():
    p = argparse.ArgumentParser(description=__doc__)
    names = ("inventory", "crm_mapping", "project_mapping", "plan", "projection",
             "independent_census", "crm_transfer", "project_output", "census_output")
    for name in names:
        p.add_argument(name, type=Path)
    for name in ("plan", "projection", "identity-census", "crm-transfer"):
        p.add_argument("--expected-" + name + "-sha256", required=True)
    args = p.parse_args()
    created = []
    try:
        paths = [getattr(args, name).resolve() for name in names]
        crm.require(len(set(paths)) == len(paths) and all(not path.is_relative_to(ROOT) for path in paths),
                    "Use distinct private files outside checkout")
        crm.require(all(not path.exists() for path in paths[-2:]), "Never overwrite output")
        loaded = [crm.load(path) for path in paths[:7]]
        (inv, ih), (rules, mh), (pr, ph), (plan, plan_hash), (raw, raw_hash), (ids, ids_hash), (transfer, ch) = loaded
        project_bytes, census_bytes, result = convert(
            inv, ih, rules, mh, pr, ph, plan, plan_hash, raw, raw_hash, ids, ids_hash, transfer, ch,
            {"planSha256": args.expected_plan_sha256, "projectionSha256": args.expected_projection_sha256,
             "identityCensusSha256": args.expected_identity_census_sha256,
             "crmTransferSha256": args.expected_crm_transfer_sha256})
        for path, data in zip(paths[-2:], (project_bytes, census_bytes)):
            with path.open("xb") as output:
                created.append(path)
                output.write(data)
        print(json.dumps(result, sort_keys=True))
    except (OSError, ValueError, TypeError, KeyError, RecursionError):
        for path in created:
            try:
                path.unlink()
            except OSError:
                pass
        print("Historical conversion rejected; review private inputs and output paths locally.", file=sys.stderr)
        return 2
    return 0


if __name__ == "__main__":
    sys.exit(main())
