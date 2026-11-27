"""Opt-in Windows DAO qualification of synthetic historical project extraction only."""
import argparse
import copy
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import qualify_synthetic_crm as common
import validate_crm_mapping as mapping
import prepare_historical_project_plan as planner
import convert_historical_project_rows as converter

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]


def qualify(folder, powershell):
    checks = []
    def check(condition, label):
        common.require(condition, label)
        checks.append(label)
    def ps(script, args, success=True):
        result = subprocess.run([str(powershell), "-NoProfile", "-NonInteractive", "-File",
                                 str(HERE / script), *map(str, args)],
                                stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=120)
        check((result.returncode == 0) == success, "Expected result: " + script)
    source = folder / "SyntheticCRM.accdb"
    ps("New-SyntheticLegacyCrmFixture.ps1", ["-OutputDirectory", folder,
       "-IncludeHistoricalProjects", "-ConfirmSyntheticFixture"])
    before = common.digest(source)
    inventory = folder / "inventory.json"
    ps("Inspect-LegacyAccessSchema.ps1", ["-DatabaseCopy", source, "-OutputFile",
       inventory, "-ConfirmTrustedCopy"])
    inv, ih = mapping.load(inventory)
    check(inv["sourceSha256"].lower() == before == common.digest(source), "Source unchanged after inventory")
    rules = common.fixture_mapping(inv, ih)
    crm_map = folder / "crm-map.json"
    common.write(crm_map, rules)
    mh = common.digest(crm_map)
    fields = {"legacyId":("Id","integer-id"), "legacyClientId":("Parent","text-id"),
              "legacyProjectNumber":("Number","text"), "title":("Title","text"),
              "site":("Site","text"), "legacyStatus":("Status","text"),
              "legacyAuthorizationStatus":("Authorization","text"),
              "authorizationEvidenceRef":("Evidence","text")}
    project_rules = {"format":"CRC-Access-Historical-Project-Mapping-v1",
       "sourceSha256":before,"inventorySha256":ih,"crmMappingSha256":mh,
       "projects":{"table":"SyntheticProjects","fields":{
           k:{"column":v[0],"transform":v[1]} for k,v in fields.items()}}}
    project_map = folder / "project-map.json"
    common.write(project_map,project_rules)
    ph = common.digest(project_map)
    plan = planner.prepare(inv,ih,rules,mh,project_rules)
    plan["projectMappingSha256"] = ph
    plan_path = folder / "plan.json"
    common.write(plan_path,plan)
    plan_hash = common.digest(plan_path)
    def export(mode, out, expected=plan_hash, chosen=plan_path, success=True):
        ps("Export-HistoricalProjectSnapshot.ps1", ["-DatabaseCopy",source,
           "-PlanFile",chosen,"-ExpectedPlanSha256",expected,"-Mode",mode,
           "-OutputFile",out,"-ConfirmTrustedSyntheticCopy"], success)
    identities = folder / "identities.json"
    export("Identity",identities)
    identity_hash = common.digest(identities)
    rows = folder / "rows.json"
    export("Rows",rows)
    raw,rh = mapping.load(rows)
    census,ch = mapping.load(identities)
    check(ch == identity_hash and common.digest(source)==before,"Separate projections preserve source and census")
    check(len(raw["rows"])==len(census["rows"])==2,"Exact projection counts")
    check(all(set(r)=={v[0] for v in fields.values()} for r in raw["rows"]),"Only mapped columns exported")
    check(all(set(r)=={"Id","Parent","Number"} for r in census["rows"]),"Only identity columns exported")
    transfer=common.expected_transfer(False)
    # Synthetic reference values are known; actual CRM extraction is a separate qualification.
    crm_transfer=folder/"crm-transfer.json"
    common.write(crm_transfer,transfer)
    th=common.digest(crm_transfer)
    project_bytes,census_bytes,result=converter.convert(inv,ih,rules,mh,project_rules,ph,
        plan,plan_hash,raw,rh,census,ch,transfer,th,
        {"planSha256":plan_hash,"projectionSha256":rh,"identityCensusSha256":ch,"crmTransferSha256":th})
    actual=json.loads(project_bytes)["projects"]
    expected=[
       {"legacyId":"101","legacyClientId":"001","legacyProjectNumber":"OLD-01","title":"Synthetic Alpha Project",
        "site":"Synthetic site A","legacyStatus":"Active","legacyAuthorizationStatus":"Approved","authorizationEvidenceRef":"REF-01"},
       {"legacyId":"102","legacyClientId":"002","legacyProjectNumber":"old-01","title":"Synthetic Beta Project",
        "site":"Synthetic site B","legacyStatus":"Closed","legacyAuthorizationStatus":"Unknown","authorizationEvidenceRef":"REF-02"}]
    check(sorted(actual,key=lambda r:r["legacyId"])==expected,"All normalized historical values match fixed fixture")
    check(result["duplicateNumberGroups"]==1 and result["inactiveClientProjectCount"]==1
          and not result["workAuthorized"] and not result["importAuthorized"],"Historical warnings and no authority")
    check(len(json.loads(census_bytes)["projects"])==2,"Bound normalized census")
    export("Rows",rows,success=False)
    check(common.digest(rows)==rh,"Overwrite denied")
    stale=folder/"stale.json"
    export("Rows",stale,"0"*64,success=False)
    check(not stale.exists(),"Stale plan denied without output")
    wrong=copy.deepcopy(plan);wrong["sourceSha256"]="0"*64
    wrong_path=folder/"wrong-plan.json";common.write(wrong_path,wrong)
    export("Rows",stale,common.digest(wrong_path),wrong_path,False)
    check(not stale.exists(),"Stale source denied")
    lock=source.with_suffix(".laccdb")
    with lock.open("xb") as f: f.write(b"synthetic lock sentinel")
    try:
        export("Rows",stale,success=False)
        check(not stale.exists(),"Lock sentinel denied")
    finally: lock.unlink()
    check(common.digest(source)==before,"Final source bytes unchanged")
    return checks


def main():
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument("--confirm-synthetic",action="store_true")
    p.add_argument("--dao-bitness",choices=("32","64"),required=True)
    args=p.parse_args()
    if os.name!="nt" or not args.confirm_synthetic:
        print("Blocked: Windows and explicit synthetic confirmation required.",file=sys.stderr)
        return 2
    try:
        system=Path(os.environ["SystemRoot"])
        is64=(system/"SysWOW64").is_dir()
        common.require(args.dao_bitness!="64" or is64,"64-bit Windows required")
        directory=("SysWOW64" if is64 and args.dao_bitness=="32" else
                   "Sysnative" if is64 and sys.maxsize<=2**32 else "System32")
        powershell=system/directory/"WindowsPowerShell/v1.0/powershell.exe"
        common.require(powershell.is_file(),"Matching Windows PowerShell required")
        with tempfile.TemporaryDirectory(prefix="CRC-synthetic-project-") as td:
            folder=Path(td).resolve()
            common.require(not folder.is_relative_to(ROOT),"Private temporary directory required")
            checks=qualify(folder,powershell)
        print(json.dumps({"checks":checks,"daoFixtureChecksPassed":True,
                          "nativeImportExecuted":False,"releaseAuthorized":False},sort_keys=True))
        return 0
    except (OSError,ValueError,KeyError,TypeError,subprocess.TimeoutExpired):
        print("Synthetic historical DAO qualification did not complete; review local prerequisites.",file=sys.stderr)
        return 2


if __name__=="__main__":
    sys.exit(main())
