import copy
import importlib.util
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("crm_reconcile", ROOT / "tools/migration/reconcile_crm_transfer.py")
m = importlib.util.module_from_spec(spec)
spec.loader.exec_module(m)

class CrmReconciliationTests(unittest.TestCase):
    def setUp(self):
        self.hashes = {"sourceSha256": "a"*64, "inventorySha256": "b"*64,
                       "mappingSha256": "c"*64, "transferSha256": "d"*64}
        self.census = {"format": "CRC-Access-CRM-Census-v1", **self.hashes,
                       "clients": ["001", "C-2"],
                       "contacts": [{"legacyId": "K-1", "legacyClientId": "001"},
                                    {"legacyId": "K-2", "legacyClientId": "C-2"}]}
        self.transfer = {"format": "CRC-Legacy-CRM-v1", "clients": [], "contacts": []}
        for ident in self.census["clients"]:
            row = dict.fromkeys(m.mapping.CLIENT, "")
            row.update(legacyId=ident, company="Synthetic", type="Individual", status="Active")
            self.transfer["clients"].append(row)
        for item in self.census["contacts"]:
            row = dict.fromkeys(m.mapping.CONTACT, "")
            row.update(item, first="Synthetic", primary=True)
            self.transfer["contacts"].append(row)

    def check(self, census=None, transfer=None):
        return m.reconcile(self.census if census is None else census,
                           self.transfer if transfer is None else transfer, self.hashes)

    def test_exact_match_counts_only(self):
        result = self.check()
        self.assertEqual(result["clientCount"], 2)
        self.assertEqual(result["contactCount"], 2)
        self.assertFalse(result["sourceAuthenticityVerified"])
        self.assertFalse(result["importAuthorized"])
        self.assertNotIn("clients", result)

    def test_order_independent(self):
        self.transfer["clients"].reverse()
        self.transfer["contacts"].reverse()
        self.assertEqual(self.check()["contactCount"], 2)

    def test_hashes_must_match(self):
        for key in self.hashes:
            bad = copy.deepcopy(self.census)
            bad[key] = "f"*64
            with self.subTest(key=key), self.assertRaises(ValueError):
                self.check(census=bad)

    def test_missing_replaced_and_case_changed_identifiers(self):
        for change in ("missing", "replace", "case"):
            bad = copy.deepcopy(self.transfer)
            if change == "missing":
                bad["clients"].pop()
            else:
                bad["clients"][1]["legacyId"] = "OTHER" if change == "replace" else "c-2"
            with self.subTest(change=change), self.assertRaises(ValueError):
                self.check(transfer=bad)

    def test_parent_relationship_and_orphan(self):
        for parent in ("C-2", "missing"):
            bad = copy.deepcopy(self.transfer)
            bad["contacts"][0]["legacyClientId"] = parent
            with self.subTest(parent=parent), self.assertRaises(ValueError):
                self.check(transfer=bad)

    def test_case_insensitive_duplicate_source_or_transfer(self):
        for side in ("source", "transfer"):
            census, transfer = copy.deepcopy(self.census), copy.deepcopy(self.transfer)
            if side == "source":
                census["clients"].append("c-2")
            else:
                row = copy.deepcopy(transfer["clients"][1])
                row["legacyId"] = "c-2"
                transfer["clients"].append(row)
            with self.subTest(side=side), self.assertRaises(ValueError):
                self.check(census, transfer)

    def test_multiple_primary_and_bad_shape(self):
        for change in ("primary", "extra", "number"):
            census, transfer = copy.deepcopy(self.census), copy.deepcopy(self.transfer)
            if change == "primary":
                census["contacts"][1]["legacyClientId"] = "001"
                transfer["contacts"][1]["legacyClientId"] = "001"
            elif change == "extra":
                transfer["clients"][0]["password"] = "synthetic"
            else:
                transfer["clients"][0]["legacyId"] = 1
            with self.subTest(change=change), self.assertRaises(ValueError):
                self.check(census, transfer)

    def test_census_extra_fields_rejected(self):
        self.census["contacts"][0]["name"] = "Synthetic"
        with self.assertRaises(ValueError):
            self.check()
