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

    def scoped(self):
        self.record["format"] = "CRC-Release-Gate-Record-v2"
        self.record["scope"] = m.scope_for("Synthetic owner decision")
        return "Synthetic owner decision"
    def complete_scoped(self):
        ref = self.scoped()
        for row in self.scenarios["results"]:
            if row["testId"] in m.ACCESS_DEFERRED:
                row.update(status="Blocked", reason="Access provider unavailable")
            else:
                row.update(status="Pass", reviewer="Synthetic reviewer", date="2026-11-28",
                           environment="Synthetic fixture", session="c"*32, fixture="Test",
                           observed="Test only", evidenceRef="Private")
        for ident in m.GATES:
            self.pass_gate(ident)
        next(r for r in self.record["gates"] if r["id"] == "G3").update(
            status="Deferred", importDisabledEvidenceRef="Synthetic boundary check")
        return ref
    def test_approved_scope_preserves_all_scenario_results(self):
        ref = self.complete_scoped()
        report = m.validate(self.record, self.scenarios, self.commit, self.artifacts, ref)
        self.assertEqual(report["requiredScenarioCount"], 53)
        self.assertEqual(report["requiredScenariosPassed"], 53)
        self.assertEqual(report["scenarioCounts"]["Blocked"], 3)
        self.assertEqual(report["gateStatuses"]["G3"], "Deferred")
        self.assertTrue(report["scopedGateRecordsComplete"])
        self.assertFalse(report["allRecordedPassed"])
        self.assertFalse(report["releaseAuthorized"])
    def test_scope_requires_independent_reference(self):
        self.scoped()
        for ref in (None, "Different reference", ""):
            with self.subTest(ref=ref), self.assertRaises(ValueError):
                m.validate(self.record, self.scenarios, self.commit, self.artifacts, ref)
    def test_deferral_cannot_expand_to_security(self):
        ref = self.scoped()
        self.record["scope"]["deferredIds"].append("SEC-01")
        with self.assertRaises(ValueError):
            m.validate(self.record, self.scenarios, self.commit, self.artifacts, ref)
    def test_required_archive_scenario_cannot_be_skipped(self):
        ref = self.complete_scoped()
        row = next(r for r in self.scenarios["results"] if r["testId"] == "CRC-ARC-07")
        row.clear()
        row.update(testId="CRC-ARC-07", status="Not run")
        with self.assertRaises(ValueError):
            m.validate(self.record, self.scenarios, self.commit, self.artifacts, ref)
    def test_scope_requires_import_disabled_evidence(self):
        ref = self.complete_scoped()
        next(r for r in self.record["gates"] if r["id"] == "G3").pop("importDisabledEvidenceRef")
        with self.assertRaises(ValueError):
            m.validate(self.record, self.scenarios, self.commit, self.artifacts, ref)
    def test_deferred_gate_cannot_be_marked_pass(self):
        ref = self.complete_scoped()
        row = next(r for r in self.record["gates"] if r["id"] == "G3")
        row.pop("importDisabledEvidenceRef")
        row["status"] = "Pass"
        with self.assertRaises(ValueError):
            m.validate(self.record, self.scenarios, self.commit, self.artifacts, ref)
    def test_scope_still_requires_security_gate(self):
        ref = self.complete_scoped()
        row = next(r for r in self.record["gates"] if r["id"] == "G7")
        row.clear()
        row.update(id="G7", status="Blocked", reason="Independent review pending")
        with self.assertRaises(ValueError):
            m.validate(self.record, self.scenarios, self.commit, self.artifacts, ref)
    def test_scope_initialization_preserves_not_run(self):
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / "scope.json"
            record = m.initialize(path, self.commit, self.artifacts, "Synthetic owner decision")
            report = m.validate(record, self.scenarios, self.commit, self.artifacts, "Synthetic owner decision")
            self.assertEqual(report["scenarioCounts"]["Not run"], 56)
            self.assertFalse(report["scopedGateRecordsComplete"])
