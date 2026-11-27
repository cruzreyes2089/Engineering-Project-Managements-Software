import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "tools/migration"))
import qualify_installed_dao as dao


class InstalledDaoTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        root = Path(self.temp.name)
        self.shells = [("64", root / "64.exe"), ("32", root / "32.exe")]
        for _, shell in self.shells:
            shell.touch()
        self.calls = []
        self.probes = [3, 3]
        self.failure = None

    def invoke(self, args, **kwargs):
        self.calls.append(args)
        if "-Command" in args:
            return subprocess.CompletedProcess(args, self.probes.pop(0), b"", b"")
        historical = args[1].endswith("qualify_synthetic_historical_projects.py")
        if self.failure == "execution" and historical:
            return subprocess.CompletedProcess(args, 2, b"", b"")
        checks = ["Same-source CRM full values and independent identities reconciled",
                  "All normalized historical values match fixed fixture",
                  "Digest-bound combined native import and recovery",
                  "Final source bytes unchanged"]
        crm_checks = ["All normalized fields match fixed expected fixture", "Final source hash unchanged",
                      "Native fixed-fixture import and rollback checks"]
        payload = {"daoFixtureChecksPassed": True, "nativeImportExecuted": True,
                   "releaseAuthorized": False,
                   "checks": checks if historical else {k: crm_checks for k in
                       ("populated", "emptyContacts", "sharedContactKey")},
                   "nativeCases": ["populated", "emptyContacts"], "sharedKeyNativeQualified": False}
        if self.failure == "marker":
            payload["nativeImportExecuted"] = False
        if self.failure == "missing-crm-case" and not historical:
            del payload["checks"]["emptyContacts"]
        return subprocess.CompletedProcess(args, 0, json.dumps(payload).encode(), b"")

    def run_fixture(self):
        report = dao.run(self.shells, self.invoke)
        self.assertFalse(report["realMigrationAuthorized"])
        self.assertFalse(report["releaseAuthorized"])
        return report

    def test_no_registered_engine_is_not_qualified(self):
        result = self.run_fixture()
        self.assertEqual(result["status"], "NotQualified")
        self.assertEqual(len(self.calls), 2)
        self.assertFalse(result["historicalPassed"])

    def test_32_bit_engine_selected_when_only_one_registered(self):
        self.probes = [3, 0]
        result = self.run_fixture()
        self.assertEqual(result["status"], "PassedSynthetic")
        self.assertEqual(result["selectedBitness"], "32")
        self.assertTrue(result["historicalPassed"] and result["crmPassed"])
        self.assertTrue(all("--native-smoke" in a and a[a.index("--dao-bitness")+1]=="32"
                            for a in self.calls[2:]))

    def test_prefer_64_bit_without_hiding_failure(self):
        self.probes = [0, 0]
        self.failure = "execution"
        result = self.run_fixture()
        self.assertEqual(result["status"], "Failed")
        self.assertEqual(result["selectedBitness"], "64")
        self.assertEqual(len(self.calls), 3)

    def test_probe_failure_not_absence(self):
        self.probes = [2, 0]
        self.assertEqual(self.run_fixture()["reason"], "RegistrationProbeFailed")
        self.assertEqual(len(self.calls), 1)

    def test_exit_zero_without_required_native_evidence_fails(self):
        self.probes = [0, 3]
        self.failure = "marker"
        self.assertEqual(self.run_fixture()["status"], "Failed")

    def test_partial_crm_evidence_fails(self):
        self.probes = [0, 3]
        self.failure = "missing-crm-case"
        result = self.run_fixture()
        self.assertTrue(result["historicalPassed"])
        self.assertFalse(result["crmPassed"])
        self.assertEqual(result["status"], "Failed")

    def test_bitness_shell_selection(self):
        root = Path("windows")
        self.assertIn("Sysnative", str(dao.candidates(root, True, False)[0][1]))
        self.assertIn("System32", str(dao.candidates(root, True, True)[0][1]))
        self.assertEqual(dao.candidates(root, False, False)[0][0], "32")
