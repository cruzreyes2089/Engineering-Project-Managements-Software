import copy
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

spec = importlib.util.spec_from_file_location("crm_mapping", Path(__file__).resolve().parents[2] / "tools/migration/validate_crm_mapping.py")
m = importlib.util.module_from_spec(spec)
spec.loader.exec_module(m)

class CrmMappingTests(unittest.TestCase):
    def setUp(self):
        self.digest = "a"*64
        def table(name, columns):
            return {"name":name,"fields":[{"name":n,"daoType":t,"required":True} for n,t in columns],"indexes":[{"name":"pk","primary":True,"unique":True,"fields":["Id"]}]}
        self.inv = {"format":"CRC-Access-Schema-Inventory-v1","sourceSha256":"B"*64,"sourceExtension":".accdb","localTables":[table("SyntheticClients",[("Id",4),("Name",10),("Kind",10),("Status",10)]),table("SyntheticContacts",[("Id",4),("Parent",4),("Name",10),("Primary",1)])],"localRelations":[{"name":"Parent","primaryTable":"SyntheticClients","foreignTable":"SyntheticContacts","fields":[{"primaryField":"Id","foreignField":"Parent"}]}],"skippedLinkedTableCount":0,"skippedSystemOrHiddenTableCount":0,"rowDataIncluded":False,"linkedSourcesOpened":False}
        def fields(targets, mapped):
            return {x:{"column":mapped[x][0],"transform":mapped[x][1]} if x in mapped else {"column":None,"transform":"blank"} for x in targets}
        self.mapping = {"format":"CRC-Access-CRM-Mapping-v1","inventorySha256":self.digest,"sourceSha256":"B"*64,"clients":{"table":"SyntheticClients","fields":fields(m.CLIENT,{"legacyId":("Id","integer-id"),"company":("Name","text"),"type":("Kind","reviewed-enum"),"status":("Status","reviewed-enum")})},"contacts":{"table":"SyntheticContacts","fields":fields(m.CONTACT,{"legacyId":("Id","integer-id"),"legacyClientId":("Parent","integer-id"),"first":("Name","text"),"primary":("Primary","boolean")})}}

    def test_accept_metadata_only(self):
        r = m.validate(self.inv,self.digest,self.mapping)
        self.assertEqual(r["mappedTargets"],23)
        self.assertFalse(r["importAuthorized"])
        self.assertFalse(r["rowDataRead"])

    def test_exact_hash(self):
        for key in ("inventorySha256","sourceSha256"):
            with self.subTest(key=key):
                bad=copy.deepcopy(self.mapping);bad[key]="c"*64
                with self.assertRaises(ValueError):
                    m.validate(self.inv,self.digest,bad)

    def test_missing_target_or_unknown_column(self):
        for mutation in ("missing","unknown"):
            with self.subTest(mutation=mutation):
                bad=copy.deepcopy(self.mapping)
                if mutation=="missing": del bad["clients"]["fields"]["status"]
                else: bad["clients"]["fields"]["company"]["column"]="Absent"
                with self.assertRaises(ValueError):
                    m.validate(self.inv,self.digest,bad)

    def test_identifier_and_boolean_type(self):
        for field, transform in (("legacyId","text-id"),("primary","text")):
            with self.subTest(field=field):
                bad=copy.deepcopy(self.mapping);bad["contacts"]["fields"][field]["transform"]=transform
                with self.assertRaises(ValueError):
                    m.validate(self.inv,self.digest,bad)

    def test_missing_relation_and_unique_index(self):
        for mutation in ("relation","index"):
            with self.subTest(mutation=mutation):
                inv=copy.deepcopy(self.inv)
                if mutation=="relation": inv["localRelations"]=[]
                else: inv["localTables"][0]["indexes"]=[]
                with self.assertRaises(ValueError):
                    m.validate(inv,self.digest,self.mapping)

    def test_private_data_and_duplicate_names_rejected(self):
        for mutation in ("rows","duplicate"):
            with self.subTest(mutation=mutation):
                inv=copy.deepcopy(self.inv)
                if mutation=="rows": inv["rowDataIncluded"]=True
                else: inv["localTables"].append(dict(inv["localTables"][0],name="syntheticclients"))
                with self.assertRaises(ValueError):
                    m.validate(inv,self.digest,self.mapping)

    def test_duplicate_json_keys_rejected(self):
        with tempfile.TemporaryDirectory() as folder:
            path=Path(folder)/"mapping.json"
            path.write_text('{"format":"one","format":"two"}',encoding="utf-8")
            with self.assertRaises(ValueError):
                m.load(path)
