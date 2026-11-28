import sys
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/release"))
import stage1_acceptance_scope as m

class Stage1ScopeTests(unittest.TestCase):
    def test_partition_retains_every_original_and_supplemental_case(self):
        groups = [m.REQUIRED, set(m.MIXED), m.DEFERRED_2, m.DEFERRED_3]
        self.assertEqual(len(set().union(*groups)), 49)
        self.assertEqual(set().union(*groups), m.SCOPE)
        self.assertEqual(sum(map(len, groups)), 49)

    def test_mandatory_security_recovery_and_desktop_cases(self):
        self.assertTrue({"SEC-01", "SEC-03", "SEC-05", "REL-01", "REL-02", "UX-01",
                         "CRC-REC-01", "CRC-BILL-02"} <= m.REQUIRED)
        self.assertTrue({"SEC-02", "SEC-04", "FIN-02", "FIN-09"} <= set(m.MIXED))

    def test_empty_evidence_does_not_authorize_release(self):
        manifest = {"format": "CRC-Acceptance-Evidence-v1", "commit": "a"*40,
                    "artifactSha256": "b"*64, "syntheticOnly": True,
                    "results": [{"testId": key, "status": "Not run"} for key in sorted(m.SCOPE)]}
        report = m.summarize(manifest, "a"*40, "b"*64)
        self.assertEqual(set(report["requiredUnpassed"]), m.REQUIRED)
        self.assertTrue(report["mixedReviewRequired"])
        self.assertFalse(report["securityClearanceVerified"])
        self.assertFalse(report["releaseAuthorized"])
