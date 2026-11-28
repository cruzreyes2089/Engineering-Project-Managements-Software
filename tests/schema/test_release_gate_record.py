import copy
import sys
from pathlib import Path
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/release"))
import validate_release_gate_record as m
import validate_acceptance_evidence as a

class ReleaseGateRecordTests(unittest.TestCase):
    def setUp(self):
        self.commit = "a" * 40
        self.artifacts = {key: "b" * 64 for key in m.ARTIFACTS}
        self.scenarios = {"format": "CRC-Acceptance-Evidence-v2", "commit": self.commit,
                          "artifactSha256": self.artifacts["desktopSha256"], "syntheticOnly": True,
                          "results": [{"testId": i, "status": "Not run"} for i in sorted(a.V2_IDS)]}
        self.record = {"format": "CRC-Release-Gate-Record-v1", "commit": self.commit,
                       "artifacts": self.artifacts.copy(),
                       "gates": [{"id": i, "status": "Not run"} for i in m.GATES]}
    def validate(self):
        return m.validate(self.record, self.scenarios, self.commit, self.artifacts)
    def pass_gate(self, ident):
        row = next(x for x in self.record["gates"] if x["id"] == ident)
        row.update(status="Pass", reviewer="Synthetic reviewer", date="2026-11-28", evidenceRef="Synthetic record")
    def test_empty_record(self):
        self.assertEqual(self.validate()["scenarioCounts"]["Not run"], 56)
        self.assertFalse(self.validate()["releaseAuthorized"])
    def test_exact_artifacts(self):
        for key in m.ARTIFACTS:
            with self.subTest(key=key):
                self.record["artifacts"][key] = "c" * 64
                with self.assertRaises(ValueError): self.validate()
                self.record["artifacts"][key] = self.artifacts[key]
    def test_missing_gate(self):
        self.record["gates"].pop()
        with self.assertRaises(ValueError): self.validate()
    def test_duplicate_gate(self):
        self.record["gates"][-1] = copy.deepcopy(self.record["gates"][0])
        with self.assertRaises(ValueError): self.validate()
    def test_no_waivers(self):
        self.record["gates"][2]["status"] = "Waived"
        with self.assertRaises(ValueError): self.validate()
    def test_blocked_requires_reason(self):
        self.record["gates"][2]["status"] = "Blocked"
        with self.assertRaises(ValueError): self.validate()
        self.record["gates"][2]["reason"] = "Synthetic provider unavailable"
        self.assertEqual(self.validate()["gateStatuses"]["G3"], "Blocked")
    def test_desktop_cannot_pass_incomplete_cases(self):
        self.pass_gate("G5")
        with self.assertRaises(ValueError): self.validate()
    def test_final_cannot_pass_outstanding_gates(self):
        self.pass_gate("G8")
        with self.assertRaises(ValueError): self.validate()
    def test_all_pass_never_authorizes(self):
        for row in self.scenarios["results"]:
            row.update(status="Pass", reviewer="Synthetic reviewer", date="2026-11-28",
                       environment="Synthetic fixture", session="c"*32, fixture="Test",
                       observed="Test only", evidenceRef="Private")
        for ident in m.GATES: self.pass_gate(ident)
        report = self.validate()
        self.assertTrue(report["allRecordedPassed"])
        self.assertFalse(report["releaseAuthorized"])
        self.assertFalse(report["evidenceAuthenticityVerified"])
    def test_initialize_exclusive(self):
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / "gate.json"
            m.initialize(path, self.commit, self.artifacts)
            original = path.read_bytes()
            with self.assertRaises(FileExistsError): m.initialize(path, self.commit, self.artifacts)
            self.assertEqual(path.read_bytes(), original)
    def test_scenario_revision_mismatch(self):
        self.scenarios["commit"] = "d"*40
        with self.assertRaises(ValueError): self.validate()
