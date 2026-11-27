import copy
import sys
from pathlib import Path
import unittest
import test_crm_conversion as fixtures

TOOLS = Path(__file__).resolve().parents[2] / "tools/migration"
sys.path.insert(0, str(TOOLS))
import prepare_crm_extraction_plan as plans
import bind_crm_source_census as binder
import convert_crm_rows as conversion

class ExtractionPlanTests(unittest.TestCase):
    def setUp(self):
        f = fixtures.ConversionTests()
        f.setUp()
        self.inv, self.rules, self.hashes = f.inv, f.rules, f.hashes
        self.transfer = conversion.convert(f.inv, f.rules, f.raw, f.enums, f.hashes)
        self.raw = {"format": "CRC-Access-CRM-Raw-Identity-v1",
                    **{k: f.hashes[k] for k in ("sourceSha256", "inventorySha256", "mappingSha256")},
                    "clients": [{"legacyId": 1}],
                    "contacts": [{"legacyId": 2, "legacyClientId": 1}]}

    def plan(self):
        return plans.prepare(self.inv, self.hashes["inventorySha256"],
                             self.rules, self.hashes["mappingSha256"])

    def bind(self):
        return binder.bind(self.inv, self.rules, self.hashes["inventorySha256"],
                           self.hashes["mappingSha256"], self.raw, self.transfer, "e"*64)

    def test_projection_only_selected_columns(self):
        plan = self.plan()
        self.assertEqual({x["name"] for x in plan["clients"]["columns"]}, {"Id", "Name", "Kind", "Status"})
        self.assertEqual(plan["contacts"]["identities"], {"legacyId": "Id", "legacyClientId": "Parent"})

    def test_unsafe_identifier(self):
        for value in ("A]B", "A[B", "A\nB", "", "x"*65):
            with self.subTest(value=value), self.assertRaises(ValueError):
                plans.safe_name(value)

    def test_bind_preserves_independent_ids(self):
        result = self.bind()
        self.assertEqual(result["clients"], ["1"])
        self.assertEqual(result["contacts"], [{"legacyId": "2", "legacyClientId": "1"}])
        self.assertEqual(result["transferSha256"], "e"*64)

    def test_cannot_substitute_identities_or_parent(self):
        for key, value in (("legacyId", 9), ("legacyClientId", 9)):
            before = copy.deepcopy(self.raw)
            self.raw["contacts"][0][key] = value
            with self.subTest(key=key), self.assertRaises(ValueError):
                self.bind()
            self.raw = before

    def test_changed_hash_and_wrong_scalar_type(self):
        self.raw["sourceSha256"] = "f"*64
        with self.assertRaises(ValueError):
            self.bind()
        self.raw["sourceSha256"] = self.hashes["sourceSha256"]
        self.raw["clients"][0]["legacyId"] = "1"
        with self.assertRaises(ValueError):
            self.bind()
