import copy
import importlib.util
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("acceptance_evidence", ROOT / "tools/release/validate_acceptance_evidence.py")
m = importlib.util.module_from_spec(spec)
spec.loader.exec_module(m)

class AcceptanceEvidenceTests(unittest.TestCase):
    def setUp(self):
        self.commit = "a" * 40
        self.digest = "b" * 64
        self.ids = m.scenario_ids(ROOT / "docs/legacy-acceptance-register.csv")
        self.manifest = {
            "format": "CRC-Acceptance-Evidence-v1", "commit": self.commit,
            "artifactSha256": self.digest, "syntheticOnly": True,
            "results": [{"testId": i, "status": "Not run"} for i in sorted(self.ids)]
        }

    def test_full_original_and_supplemental_scope(self):
        self.assertEqual(len(self.ids), 49)
        result = m.validate(self.manifest, self.ids, self.commit, self.digest)
        self.assertEqual(result["counts"]["Not run"], 49)
        self.assertFalse(result["releaseAuthorized"])

    def test_missing_duplicate_unknown_scenario(self):
        for case in ("missing", "duplicate", "unknown"):
            with self.subTest(case=case):
                bad = copy.deepcopy(self.manifest)
                if case == "missing": bad["results"].pop()
                if case == "duplicate": bad["results"].append(bad["results"][0])
                if case == "unknown": bad["results"][0]["testId"] = "NEW-99"
                with self.assertRaises(ValueError):
                    m.validate(bad, self.ids, self.commit, self.digest)

    def test_exact_candidate_and_synthetic_boundary(self):
        for key, value in (("commit", "c"*40), ("artifactSha256", "d"*64),
                           ("syntheticOnly", False), ("format", "unknown")):
            with self.subTest(key=key):
                bad = copy.deepcopy(self.manifest)
                bad[key] = value
                with self.assertRaises(ValueError):
                    m.validate(bad, self.ids, self.commit, self.digest)

    def test_pass_needs_evidence(self):
        row = self.manifest["results"][0]
        row["status"] = "Pass"
        with self.assertRaises(ValueError):
            m.validate(self.manifest, self.ids, self.commit, self.digest)
        row.update(reviewer="Reviewer-01", date="2026-11-26", environment="Windows-VM",
                   session="e"*32, fixture="Synthetic-01", observed="Expected invariant observed",
                   evidenceRef="Evidence-01")
        result = m.validate(self.manifest, self.ids, self.commit, self.digest)
        self.assertEqual(result["counts"]["Pass"], 1)
        self.assertFalse(result["releaseAuthorized"])
        for key in ("reviewer", "date", "environment", "session", "fixture", "observed", "evidenceRef"):
            with self.subTest(key=key):
                bad = copy.deepcopy(self.manifest)
                bad["results"][0][key] = ""
                with self.assertRaises(ValueError):
                    m.validate(bad, self.ids, self.commit, self.digest)

    def test_failure_and_blocker_not_pass(self):
        row = self.manifest["results"][0]
        row.update(status="Fail", reviewer="Reviewer-01", date="2026-11-26",
                   environment="Windows-VM", session="e"*32, fixture="Synthetic-01",
                   observed="Invariant failed", evidenceRef="Evidence-02")
        with self.assertRaises(ValueError):
            m.validate(self.manifest, self.ids, self.commit, self.digest)
        row["defectId"] = "DEF-01"
        self.manifest["results"][1].update(status="Blocked", reason="Adapter not qualified")
        result = m.validate(self.manifest, self.ids, self.commit, self.digest)
        self.assertEqual(result["counts"]["Fail"], 1)
        self.assertEqual(result["counts"]["Blocked"], 1)
        self.assertFalse(result["releaseAuthorized"])

    def test_no_extra_or_malformed_fields(self):
        for key, value in (("status", "Waived"), ("status", []), ("password", "secret")):
            with self.subTest(key=key, value=value):
                bad = copy.deepcopy(self.manifest)
                bad["results"][0][key] = value
                with self.assertRaises(ValueError):
                    m.validate(bad, self.ids, self.commit, self.digest)
