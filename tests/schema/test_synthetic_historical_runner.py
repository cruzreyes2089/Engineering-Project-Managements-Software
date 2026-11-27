"""Offline qualification of the historical runner's complete orchestration contract.

Only the external PowerShell/DAO boundary is substituted. All actual Python mapping,
projection validation, conversion, digest and census reconciliation routines run.
No real DAO or native executable is launched by these tests.
"""
import copy
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "tools/migration"))
import qualify_synthetic_historical_projects as runner
import qualify_synthetic_crm as common


class SyntheticHistoricalRunnerTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.folder = Path(self.temp.name)
        self.source = self.folder / "SyntheticCRM.accdb"
        self.mutation = None
        self.calls = []

    def inventory(self):
        def table(name, columns):
            return {"name":name,"fields":[{"name":n,"daoType":t,"required":True} for n,t in columns],
                    "indexes":[{"name":"pk","primary":True,"unique":True,"fields":["Id"]}]}
        def relation(name, table):
            return {"name":name,"primaryTable":"SyntheticClients","foreignTable":table,
                    "fields":[{"primaryField":"Id","foreignField":"Parent"}]}
        return {"format":"CRC-Access-Schema-Inventory-v1","sourceSha256":common.digest(self.source).upper(),
                "sourceExtension":".accdb","localTables":[
                    table("SyntheticClients",[("Id",10),("Name",10),("Kind",4),("Status",1),("Postal",10),("Ignored",10)]),
                    table("SyntheticContacts",[("Id",4),("Parent",10),("Name",10),("PrimaryFlag",1),("Ignored",10)]),
                    table("SyntheticProjects",[("Id",4),("Parent",10),("Number",10),("Title",10),("Site",10),
                        ("Status",10),("Authorization",10),("Evidence",10),("Ignored",10)])],
                "localRelations":[relation("FKParent","SyntheticContacts"),relation("FKProjectParent","SyntheticProjects")],
                "skippedLinkedTableCount":0,"skippedSystemOrHiddenTableCount":0,
                "rowDataIncluded":False,"linkedSourcesOpened":False}

    def execute(self, args, **kwargs):
        script = Path(args[4]).name
        self.calls.append(script)
        def value(flag):
            return args[args.index(flag)+1]
        def done(code=0):
            return subprocess.CompletedProcess(args,code,b"",b"")
        if script=="New-SyntheticLegacyCrmFixture.ps1":
            self.assertIn("-IncludeHistoricalProjects",args)
            self.source.write_bytes(b"synthetic source placeholder; not an Access database")
            return done()
        if script=="Inspect-LegacyAccessSchema.ps1":
            common.write(Path(value("-OutputFile")),self.inventory())
            return done()
        self.assertIn(script,{"Export-HistoricalProjectSnapshot.ps1","Export-LegacyCrmSnapshot.ps1"})
        source=Path(value("-DatabaseCopy"))
        plan_path=Path(value("-PlanFile"))
        expected=value("-ExpectedPlanSha256")
        target=Path(value("-OutputFile"))
        plan=json.loads(plan_path.read_bytes())
        if (target.exists() or expected!=common.digest(plan_path) or plan["sourceSha256"]!=common.digest(source)
                or source.with_suffix(".laccdb").exists()):
            return done(2)
        identity=value("-Mode")=="Identity"
        if script=="Export-HistoricalProjectSnapshot.ps1":
            self.assertEqual(plan["table"],"SyntheticProjects")
            rows=[
                {"Id":101,"Parent":"001","Number":"OLD-01","Title":"Synthetic Alpha Project",
                 "Site":"Synthetic site A","Status":"Active","Authorization":"Approved","Evidence":"REF-01"},
                {"Id":102,"Parent":"002","Number":"old-01","Title":"Synthetic Beta Project",
                 "Site":"Synthetic site B","Status":"Closed","Authorization":"Unknown","Evidence":"REF-02"}]
            if identity:
                rows=[{k:row[k] for k in ("Id","Parent","Number")} for row in reversed(rows)]
                if self.mutation=="census": rows[0]["Parent"]="001"
            elif self.mutation=="evidence": rows[0]["Evidence"]="Wrong evidence"
            elif self.mutation=="extra": rows[0]["Ignored"]="DO NOT EXPORT"
            payload={"format":"CRC-Access-Historical-Project-"+("Identity-Census-v1" if identity else "Projection-v1"),
                     "sourceSha256":common.digest(source),"planSha256":expected,"rows":rows}
        else:
            clients=[{"Id":"001","Name":"Synthetic Alpha","Kind":7,"Status":True,"Postal":"00501"},
                     {"Id":"002","Name":"Synthetic Beta","Kind":9,"Status":False,"Postal":"00999"}]
            contacts=[{"Id":11,"Parent":"001","Name":"Synthetic First","PrimaryFlag":True},
                      {"Id":12,"Parent":"002","Name":"Synthetic Second","PrimaryFlag":False}]
            if identity:
                clients=[{"legacyId":r["Id"]} for r in reversed(clients)]
                contacts=[{"legacyId":r["Id"],"legacyClientId":r["Parent"]} for r in reversed(contacts)]
                if self.mutation=="crm": contacts[0]["legacyClientId"]="001"
            payload={"format":"CRC-Access-CRM-"+("Raw-Identity-v1" if identity else "Rows-v1"),
                     **{k:plan[k] for k in ("sourceSha256","inventorySha256","mappingSha256")},
                     "clients":clients,"contacts":contacts}
        common.write(target,payload)
        return done()

    def run_offline(self):
        with patch.object(runner.subprocess,"run",side_effect=self.execute):
            return runner.qualify(self.folder,"synthetic-powershell",native_smoke=False)

    def test_complete_runner_contract(self):
        checks=self.run_offline()
        self.assertIn("All normalized historical values match fixed fixture",checks)
        self.assertIn("Same-source CRM full values and independent identities reconciled",checks)
        self.assertIn("Final source bytes unchanged",checks)
        self.assertFalse(self.source.with_suffix(".laccdb").exists())
        self.assertEqual(self.calls.count("Export-HistoricalProjectSnapshot.ps1"),6)
        self.assertEqual(self.calls.count("Export-LegacyCrmSnapshot.ps1"),2)

    def test_independent_project_census_substitution(self):
        self.mutation="census"
        with self.assertRaises(ValueError): self.run_offline()

    def test_nonidentity_evidence_substitution(self):
        self.mutation="evidence"
        with self.assertRaises(ValueError): self.run_offline()

    def test_unmapped_projection_data(self):
        self.mutation="extra"
        with self.assertRaises(ValueError): self.run_offline()

    def test_independent_crm_census_substitution(self):
        self.mutation="crm"
        with self.assertRaises(ValueError): self.run_offline()
