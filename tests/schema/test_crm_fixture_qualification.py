import copy
from pathlib import Path
import sys
import unittest
import test_crm_mapping as fixtures

TOOLS = Path(__file__).resolve().parents[2] / "tools/migration"
sys.path.insert(0, str(TOOLS))
import qualify_synthetic_crm as q

class SyntheticQualificationTests(unittest.TestCase):
    def test_fixture_expected_values_and_empty_contacts(self):
        data = q.expected_transfer(False)
        self.assertEqual([r["legacyId"] for r in data["clients"]], ["001", "002"])
        self.assertEqual([r["zip"] for r in data["clients"]], ["00501", "00999"])
        self.assertEqual([r["status"] for r in data["clients"]], ["Active", "Inactive"])
        self.assertEqual(q.expected_transfer(True)["contacts"], [])
        self.assertEqual([r["primary"] for r in data["contacts"]], [True, False])

    def test_field_comparison_independent_of_row_order(self):
        expected = q.expected_transfer(False)
        actual = copy.deepcopy(expected)
        actual["clients"].reverse()
        actual["contacts"].reverse()
        q.compare_values(actual, expected)

    def test_field_comparison_rejects_nonidentity_changes(self):
        expected = q.expected_transfer(False)
        for key, value in (("zip", "501"), ("status", "Inactive"),
                           ("company", "Changed"), ("type", "Other")):
            with self.subTest(key=key):
                actual = copy.deepcopy(expected)
                actual["clients"][0][key] = value
                with self.assertRaises(ValueError):
                    q.compare_values(actual, expected)

    def test_unrelated_source_columns_not_selected(self):
        fixture = fixtures.CrmMappingTests()
        fixture.setUp()
        inv = fixture.inv
        tables = inv["localTables"]
        tables[0]["fields"] += [{"name": "Postal", "daoType": 10}, {"name": "Ignored", "daoType": 10}]
        tables[1]["fields"] += [{"name": "Ignored", "daoType": 10}]
        for f in tables[0]["fields"]:
            if f["name"] == "Id": f["daoType"] = 10
            if f["name"] == "Kind": f["daoType"] = 4
            if f["name"] == "Status": f["daoType"] = 1
        for f in tables[1]["fields"]:
            if f["name"] == "Parent": f["daoType"] = 10
            if f["name"] == "Primary": f["name"] = "PrimaryFlag"
        rules = q.fixture_mapping(inv, fixture.digest)
        plan = q.plans.prepare(inv, fixture.digest, rules, "c"*64)
        for entity in ("clients", "contacts"):
            self.assertNotIn("Ignored", {c["name"] for c in plan[entity]["columns"]})
