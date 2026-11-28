import copy
import sys
from pathlib import Path
import unittest

TOOLS = Path(__file__).resolve().parents[2] / "tools/migration"
sys.path.insert(0, str(TOOLS))
import historical_project_preflight as p
import qualify_synthetic_crm as fixtures

class HistoricalProjectTests(unittest.TestCase):
    def setUp(self):
        self.hashes = {"sourceSha256": "a"*64, "crmTransferSha256": "b"*64,
                       "projectTransferSha256": "c"*64}
        self.crm = fixtures.expected_transfer(False)
        self.projects = {"format": "CRC-Legacy-Projects-v1",
                         "sourceSha256": self.hashes["sourceSha256"],
                         "crmTransferSha256": self.hashes["crmTransferSha256"],
                         "projects": [
                             {"legacyId": "P01", "legacyClientId": "001", "legacyProjectNumber": "OLD-001",
                              "title": "Synthetic project", "site": "Synthetic site", "legacyStatus": "Active",
                              "legacyAuthorizationStatus": "Approved", "authorizationEvidenceRef": "Legacy-01"},
                             {"legacyId": "P02", "legacyClientId": "002", "legacyProjectNumber": "OLD-002",
                              "title": "Synthetic historic project", "site": "", "legacyStatus": "Closed",
                              "legacyAuthorizationStatus": "Unknown", "authorizationEvidenceRef": ""}]}
        self.census = {"format": "CRC-Legacy-Project-Census-v1", **self.hashes,
                       "projects": [{k:r[k] for k in ("legacyId","legacyClientId","legacyProjectNumber")}
                                    for r in self.projects["projects"]]}

    def check(self):
        return p.validate(self.crm, self.projects, self.census, self.hashes)

    def test_preserves_inactive_client_and_no_authorization(self):
        result = self.check()
        self.assertEqual(result["projectCount"], 2)
        self.assertEqual(result["inactiveClientProjectCount"], 1)
        self.assertEqual(result["historicalEvidenceReferenceCount"], 1)
        for key in ("nativeProjectCreationAuthorized", "workAuthorized", "financialMigrationAuthorized",
                    "sourceAuthenticityVerified", "historicalAuthorizationVerified"):
            self.assertIs(result[key], False)

    def test_order_independent(self):
        self.projects["projects"].reverse()
        self.assertEqual(self.check()["projectCount"], 2)

    def test_census_detects_substitution(self):
        for key, value in (("legacyId","P03"),("legacyClientId","002"),("legacyProjectNumber","OLD-003")):
            with self.subTest(key=key):
                saved = copy.deepcopy(self.projects)
                self.projects["projects"][0][key] = value
                with self.assertRaises(ValueError): self.check()
                self.projects = saved

    def test_orphan_and_case_ambiguous_id(self):
        for key, value in (("legacyClientId","missing"),("legacyId","p02")):
            with self.subTest(key=key):
                original = copy.deepcopy(self.projects)
                self.projects["projects"][0][key] = value
                self.census["projects"][0][key] = value
                with self.assertRaises(ValueError): self.check()
                self.projects = original
                self.census["projects"][0][key] = original["projects"][0][key]

    def test_duplicate_project_number_reported_not_merged(self):
        self.projects["projects"][1]["legacyProjectNumber"] = "old-001"
        self.census["projects"][1]["legacyProjectNumber"] = "old-001"
        result = self.check()
        self.assertEqual(result["duplicateNumberGroups"], 1)
        self.assertEqual(result["projectCount"], 2)

    def test_reject_native_state_and_financial_fields(self):
        for key, value in (("state","Active"),("fee",1000),("authorized",True)):
            with self.subTest(key=key):
                self.projects["projects"][0][key] = value
                with self.assertRaises(ValueError): self.check()
                del self.projects["projects"][0][key]

    def test_source_and_transfer_bindings(self):
        for key in self.hashes:
            with self.subTest(key=key):
                old = self.census[key]
                self.census[key] = "f"*64
                with self.assertRaises(ValueError): self.check()
                self.census[key] = old

    def test_no_silent_text_normalization(self):
        self.projects["projects"][0]["legacyStatus"] = "  "
        with self.assertRaises(ValueError): self.check()
