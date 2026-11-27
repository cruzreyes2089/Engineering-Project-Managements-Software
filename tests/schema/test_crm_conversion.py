import copy
import importlib.util
from pathlib import Path
import tempfile
import unittest
import test_crm_mapping as fixture

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("convert_crm", ROOT / "tools/migration/convert_crm_rows.py")
m = importlib.util.module_from_spec(spec)
spec.loader.exec_module(m)

class ConversionTests(unittest.TestCase):
    def setUp(self):
        f = fixture.CrmMappingTests()
        f.setUp()
        self.inv, self.rules = f.inv, f.mapping
        self.hashes = {"sourceSha256": "b"*64, "inventorySha256": f.digest,
                       "mappingSha256": "c"*64, "rowsSha256": "d"*64}
        self.raw = {"format": "CRC-Access-CRM-Rows-v1", "sourceSha256": "b"*64,
                    "inventorySha256": f.digest, "mappingSha256": "c"*64,
                    "clients": [{"Id": 1, "Name": "Synthetic", "Kind": "Company", "Status": "Open"}],
                    "contacts": [{"Id": 2, "Parent": 1, "Name": "Synthetic contact", "Primary": True}]}
        self.enums = {"format": "CRC-Access-CRM-Enum-Review-v1", **self.hashes,
                      "type": [{"source": "Company", "target": "Business / Company"}],
                      "status": [{"source": "Open", "target": "Active"}]}

    def convert(self):
        return m.convert(self.inv, self.rules, self.raw, self.enums, self.hashes)

    def test_valid_conversion_and_explicit_blank(self):
        out = self.convert()
        self.assertEqual(out["format"], "CRC-Legacy-CRM-v1")
        self.assertEqual(out["clients"][0]["legacyId"], "1")
        self.assertEqual(out["clients"][0]["type"], "Business / Company")
        self.assertEqual(out["clients"][0]["email"], "")
        self.assertIs(out["contacts"][0]["primary"], True)

    def test_no_unknown_enum_or_invented_target(self):
        for value in ("Unmapped", "company", None):
            self.raw["clients"][0]["Kind"] = value
            with self.subTest(value=value), self.assertRaises(ValueError):
                self.convert()
        self.raw["clients"][0]["Kind"] = "Company"
        self.enums["type"][0]["target"] = "Individual"
        with self.assertRaises(ValueError):
            self.convert()

    def test_reject_wrong_scalar_types(self):
        for field, value in (("Id", True), ("Id", "1"), ("Id", 2147483648), ("Name", None)):
            with self.subTest(field=field, value=value):
                original = self.raw["clients"][0][field]
                self.raw["clients"][0][field] = value
                with self.assertRaises(ValueError):
                    self.convert()
                self.raw["clients"][0][field] = original
        self.raw["contacts"][0]["Primary"] = -1
        with self.assertRaises(ValueError):
            self.convert()

    def test_duplicate_enum_and_wrong_hash(self):
        self.enums["type"].append(copy.deepcopy(self.enums["type"][0]))
        with self.assertRaises(ValueError):
            self.convert()
        self.enums["type"].pop()
        self.enums["rowsSha256"] = "f"*64
        with self.assertRaises(ValueError):
            self.convert()

    def test_extra_missing_and_orphan_rows(self):
        for change in ("extra", "missing", "orphan", "duplicate"):
            saved = copy.deepcopy(self.raw)
            if change == "extra": self.raw["clients"][0]["Unmapped"] = "private"
            elif change == "missing": del self.raw["clients"][0]["Name"]
            elif change == "orphan": self.raw["contacts"][0]["Parent"] = 5
            else: self.raw["clients"].append(copy.deepcopy(self.raw["clients"][0]))
            with self.subTest(change=change), self.assertRaises(ValueError):
                self.convert()
            self.raw = saved

    def test_preserve_text_identifier_and_leading_zeros(self):
        for table in self.inv["localTables"]:
            for column in table["fields"]:
                if column["name"] in ("Id", "Parent"): column["daoType"] = 10
        for entity in ("clients", "contacts"):
            for rule in self.rules[entity]["fields"].values():
                if rule["transform"] == "integer-id": rule["transform"] = "text-id"
        self.raw["clients"][0]["Id"] = "001"
        self.raw["contacts"][0].update(Id="C-002", Parent="001")
        out = self.convert()
        self.assertEqual(out["clients"][0]["legacyId"], "001")
        self.assertEqual(out["contacts"][0]["legacyClientId"], "001")

    def test_exclusive_write(self):
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / "transfer.json"
            m.write_new(path, self.convert())
            before = path.read_bytes()
            with self.assertRaises(FileExistsError):
                m.write_new(path, self.convert())
            self.assertEqual(path.read_bytes(), before)
