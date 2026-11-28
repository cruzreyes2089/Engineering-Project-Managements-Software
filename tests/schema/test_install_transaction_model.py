from dataclasses import replace
import importlib.util
from pathlib import Path
import sys
import unittest

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("install_transaction", ROOT / "tools/release/install_transaction_model.py")
m = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = m
spec.loader.exec_module(m)

class InstallTransactionTests(unittest.TestCase):
    def setUp(self):
        self.old = m.Version("a"*40, "b"*64, 46)
        self.new = m.Version("c"*40, "d"*64, 47)
        self.plan = m.Plan(self.old, self.new, "e"*64, "f"*64, "e"*64, self.new, "f"*64)
        self.obs = m.Observation(self.old, self.new, True, True, True, True, 46, "e"*64, "f"*64)

    def decision(self, phase, obs=None):
        result = m.assess(self.plan, phase, self.obs if obs is None else obs)
        self.assertFalse(result.release_authorized or result.execution_authorized or
                         result.automatic_rollback or result.data_removal_authorized)
        return result

    def test_normal_review_sequence(self):
        self.assertEqual(self.decision(m.Phase.PLANNED).next_phase, m.Phase.STAGED)
        self.assertEqual(self.decision(m.Phase.STAGED).next_phase, m.Phase.SWITCH_INTENT)
        self.assertIs(self.decision(m.Phase.SWITCH_INTENT).disposition, m.Disposition.REVIEW_SWITCH)
        switched = replace(self.obs, active=self.new)
        self.assertEqual(self.decision(m.Phase.SWITCH_INTENT, switched).next_phase, m.Phase.ACTIVATED)
        self.assertIsNone(self.decision(m.Phase.ACTIVATED, switched).next_phase)
        accepted = replace(switched, data_schema=47, data_sha256="1"*64, acceptance_verified=True)
        self.assertEqual(self.decision(m.Phase.ACTIVATED, accepted).next_phase, m.Phase.ACCEPTED)
        self.assertIs(self.decision(m.Phase.ACCEPTED, accepted).disposition, m.Disposition.RETAIN)

    def test_interrupted_staging_does_not_advance(self):
        missing = replace(self.obs, staged=None, staged_verified=False)
        self.assertIs(self.decision(m.Phase.PLANNED, missing).disposition, m.Disposition.STAGE)
        self.assertIs(self.decision(m.Phase.STAGED, missing).disposition, m.Disposition.HOLD)
        self.assertIs(self.decision(m.Phase.SWITCH_INTENT, missing).disposition, m.Disposition.HOLD)

    def test_ambiguous_pointer_or_post_switch_data_never_rolls_back(self):
        for phase in m.Phase:
            for obs in (replace(self.obs, active=None),
                        replace(self.obs, active_verified=False),
                        replace(self.obs, recovery_sha256="0"*64)):
                with self.subTest(phase=phase, obs=obs):
                    self.assertIs(self.decision(phase, obs).disposition, m.Disposition.HOLD)
        changed = replace(self.obs, active=self.new, data_schema=47, data_sha256="1"*64)
        self.assertIs(self.decision(m.Phase.SWITCH_INTENT, changed).disposition, m.Disposition.HOLD)
        self.assertIs(self.decision(m.Phase.ACTIVATED, replace(changed, active=self.old)).disposition, m.Disposition.HOLD)

    def test_pre_switch_data_change_and_stale_evidence_denied(self):
        for phase in (m.Phase.PLANNED, m.Phase.STAGED, m.Phase.SWITCH_INTENT):
            for obs in (replace(self.obs, data_sha256="1"*64),
                        replace(self.obs, data_schema=True),
                        replace(self.obs, staged_verified=False),
                        replace(self.obs, exclusive_lock=False),
                        replace(self.obs, application_closed=False)):
                self.assertIs(self.decision(phase, obs).disposition, m.Disposition.HOLD)

    def test_acceptance_cannot_be_inferred(self):
        switched = replace(self.obs, active=self.new, data_schema=47, data_sha256="1"*64)
        self.assertIsNone(self.decision(m.Phase.ACTIVATED, switched).next_phase)
        self.assertIs(self.decision(m.Phase.ACCEPTED, switched).disposition, m.Disposition.HOLD)
        premature = replace(self.obs, active=self.new, acceptance_verified=True)
        self.assertIsNone(self.decision(m.Phase.ACTIVATED, premature).next_phase)

    def test_bound_rehearsal_and_no_downgrade(self):
        for changes in ({"rehearsed_source_sha256":"0"*64}, {"rehearsed_recovery_sha256":"0"*64},
                        {"rehearsed_candidate":self.old}, {"candidate":self.old},
                        {"candidate":m.Version("0"*40,"1"*64,45)}, {"recovery_sha256":None}):
            with self.subTest(changes=changes), self.assertRaises(ValueError):
                replace(self.plan, **changes)

    def test_fresh_install_refuses_any_existing_data(self):
        plan = m.Plan(None, self.new, None, None, None, None, None)
        obs = m.Observation(None, self.new, True, True, True, True, None, None, None)
        self.assertEqual(m.assess(plan, m.Phase.PLANNED, obs).next_phase, m.Phase.STAGED)
        for changed in (replace(obs, data_schema=46), replace(obs, data_sha256="1"*64),
                        replace(obs, recovery_sha256="2"*64)):
            self.assertIs(m.assess(plan,m.Phase.PLANNED,changed).disposition,m.Disposition.HOLD)

    def test_uninstall_review_never_removes_data_or_unknown_files(self):
        for closed in (True, False):
            result = m.uninstall_review(self.old,self.old,closed,True,True)
            self.assertEqual(result["reviewCodeRemoval"], closed)
            self.assertTrue(result["preserveAllData"] and result["preserveRecoveryEvidence"])
            self.assertFalse(result["removeUnknownFiles"] or result["uninstallExecuted"] or result["releaseAuthorized"])
        self.assertFalse(m.uninstall_review(self.new,self.old,True,True,True)["reviewCodeRemoval"])
