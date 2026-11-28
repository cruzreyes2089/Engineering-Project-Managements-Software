import copy
import hashlib
import json
import sys
from pathlib import Path
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "tools/migration"))
import prepare_historical_project_plan as planner
import convert_historical_project_rows as convert
import test_crm_mapping
import qualify_synthetic_crm as fixture


class HistoricalProjectMappingTests(unittest.TestCase):
    def setUp(self):
        crm = test_crm_mapping.CrmMappingTests()
        crm.setUp()
        self.inv, self.ih, self.rules = crm.inv, crm.digest, crm.mapping
        self.mh = "c"*64
        types = {"Id":4, "Parent":4, "Number":10, "Name":10, "Status":10, "Authorization":10}
        self.inv["localTables"].append({"name":"SyntheticProjects",
            "fields":[{"name":n,"daoType":t,"required":True} for n,t in types.items()],
            "indexes":[{"name":"pk","primary":True,"unique":True,"fields":["Id"]}]})
        self.inv["localRelations"].append({"name":"ProjectParent","primaryTable":"SyntheticClients",
            "foreignTable":"SyntheticProjects","fields":[{"primaryField":"Id","foreignField":"Parent"}]})
        mapped = {"legacyId":("Id","integer-id"),"legacyClientId":("Parent","integer-id"),
                  "legacyProjectNumber":("Number","text"),"title":("Name","text"),
                  "legacyStatus":("Status","text"),"legacyAuthorizationStatus":("Authorization","text")}
        self.project = {"format":"CRC-Access-Historical-Project-Mapping-v1","sourceSha256":"b"*64,
                        "inventorySha256":self.ih,"crmMappingSha256":self.mh,
                        "projects":{"table":"SyntheticProjects","fields":{
                            k:{"column":mapped[k][0],"transform":mapped[k][1]} if k in mapped else
                              {"column":None,"transform":"blank"} for k in planner.FIELDS}}}
        self.ph = "d"*64
        self.plan = self.prepare()
        self.plan["projectMappingSha256"] = self.ph
        self.plan_hash = "e"*64
        self.raw_hash, self.census_hash, self.ch = "1"*64, "2"*64, "3"*64
        self.raw = {"format":"CRC-Access-Historical-Project-Projection-v1","sourceSha256":"b"*64,
                    "planSha256":self.plan_hash,"rows":[
                    {"Id":1,"Parent":1,"Number":"OLD-1","Name":" Historical title ","Status":"Active","Authorization":"Approved"},
                    {"Id":2,"Parent":2,"Number":"old-1","Name":"Other","Status":"Closed","Authorization":"Unknown"}]}
        self.census = {"format":"CRC-Access-Historical-Project-Identity-Census-v1",
                       "sourceSha256":"b"*64,"planSha256":self.plan_hash,
                       "rows":[{"Id":2,"Parent":2,"Number":"old-1"},{"Id":1,"Parent":1,"Number":"OLD-1"}]}
        self.transfer = fixture.expected_transfer(True)
        # This fixture represents integer IDs rather than zero-padded text IDs.
        self.transfer["clients"] = [dict(x, legacyId=str(i)) for i,x in enumerate(self.transfer["clients"],1)]
        self.expected = {"planSha256":self.plan_hash,"projectionSha256":self.raw_hash,
                         "identityCensusSha256":self.census_hash,"crmTransferSha256":self.ch}

    def prepare(self):
        return planner.prepare(self.inv,self.ih,self.rules,self.mh,self.project)

    def convert(self):
        return convert.convert(self.inv,self.ih,self.rules,self.mh,self.project,self.ph,
             self.plan,self.plan_hash,self.raw,self.raw_hash,self.census,self.census_hash,
             self.transfer,self.ch,self.expected)

    def test_metadata_plan_preserves_identity_and_no_authorization(self):
        self.assertEqual(self.plan["identityColumns"],{"legacyId":"Id","legacyClientId":"Parent","legacyProjectNumber":"Number"})
        self.assertFalse(self.plan["sourceRead"])
        self.assertFalse(self.plan["importAuthorized"])

    def test_required_text_and_unknown_fields(self):
        for target in ("legacyAuthorizationStatus","title","legacyProjectNumber"):
            with self.subTest(target=target):
                original=copy.deepcopy(self.project)
                self.project["projects"]["fields"][target]={"column":None,"transform":"blank"}
                with self.assertRaises(ValueError): self.prepare()
                self.project=original

    def test_key_relation_and_source_binding(self):
        for kind in ("key","relation","binding"):
            with self.subTest(kind=kind):
                inv, project=copy.deepcopy(self.inv),copy.deepcopy(self.project)
                if kind=="key": self.inv["localTables"][-1]["indexes"]=[]
                elif kind=="relation": self.inv["localRelations"].pop()
                else: self.project["crmMappingSha256"]="0"*64
                with self.assertRaises(ValueError): self.prepare()
                self.inv,self.project=inv,project

    def test_conversion_exact_text_and_independent_census_order(self):
        p,c,result=self.convert()
        rows=json.loads(p)["projects"]
        self.assertEqual(rows[0]["title"]," Historical title ")
        self.assertEqual(rows[0]["legacyAuthorizationStatus"],"Approved")
        self.assertEqual(rows[0]["legacyId"],"1")
        self.assertEqual(result["duplicateNumberGroups"],1)
        self.assertEqual(result["projectCount"],2)
        self.assertEqual(json.loads(c)["projectTransferSha256"],hashlib.sha256(p).hexdigest())
        for key in ("importAuthorized","workAuthorized","independentCollectionVerified","sourceAuthenticityVerified"):
            self.assertFalse(result[key])

    def test_census_substitution_or_missing_record(self):
        for kind in ("parent","number","missing"):
            with self.subTest(kind=kind):
                old=copy.deepcopy(self.census)
                if kind=="missing": self.census["rows"].pop()
                elif kind=="parent": self.census["rows"][0]["Parent"]=1
                else: self.census["rows"][0]["Number"]="Changed"
                with self.assertRaises(ValueError): self.convert()
                self.census=old

    def test_projection_rejects_extra_financial_columns_or_invalid_identity(self):
        for key,value in (("Fee",1000),("Id",True),("Id",1.0),("Id",2147483648),("Authorization",None)):
            with self.subTest(key=key,value=value):
                old=copy.deepcopy(self.raw)
                self.raw["rows"][0][key]=value
                with self.assertRaises(ValueError): self.convert()
                self.raw=old

    def test_plan_and_independent_digest_changes_denied(self):
        self.expected["identityCensusSha256"]="0"*64
        with self.assertRaises(ValueError): self.convert()
        self.expected["identityCensusSha256"]=self.census_hash
        self.plan["rules"]["title"]["column"]="Status"
        with self.assertRaises(ValueError): self.convert()

    def test_empty_or_duplicate_projection_denied(self):
        original=copy.deepcopy(self.raw)
        self.raw["rows"]=[]
        with self.assertRaises(ValueError): self.convert()
        self.raw=original
        self.raw["rows"][1]["Id"]=1
        with self.assertRaises(ValueError): self.convert()
