from contextlib import closing
from pathlib import Path
import sys
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "tools/release"))
import install_transaction_model as m
import synthetic_install_review_journal as j
import synthetic_install_pointer_fixture as f

class InstallPointerFixtureTests(unittest.TestCase):
    def setUp(self):
        self.fixture = f.Fixture()
        self.addCleanup(self.fixture.__exit__, None, None, None)
        self.initial = self.fixture.checkpoint

    def intent(self):
        self.fixture.stage(self.initial)
        staged = self.fixture.record(self.initial)
        return self.fixture.record(staged)

    def unchanged(self):
        self.assertEqual(self.fixture.data.read_bytes(), f.DATA)
        self.assertEqual(self.fixture.recovery.read_bytes(), f.RECOVERY)

    def test_normal_switch_and_record_no_acceptance_inferred(self):
        intent = self.intent()
        self.assertEqual(self.fixture.switch(intent).active, self.fixture.new)
        active = self.fixture.record(intent)
        self.assertIs(active.phase, m.Phase.ACTIVATED)
        with self.assertRaises(ValueError):
            self.fixture.record(active)
        self.unchanged()

    def test_partial_stage_holds_without_overwrite(self):
        with self.assertRaises(f.InjectedFault):
            self.fixture.stage(self.initial, "partial")
        for action in (self.fixture.observe, lambda: self.fixture.stage(self.initial),
                       lambda: self.fixture.record(self.initial)):
            with self.assertRaises(ValueError):
                action()
        self.assertEqual(self.fixture.pointer.read_bytes(), f.OLD_POINTER)
        with closing(self.fixture.connect()) as db:
            self.assertEqual(j.reconcile(db, self.fixture.plan, self.initial), self.initial)
        self.unchanged()

    def test_before_replace_fault_retains_old_pointer_and_pending_evidence(self):
        intent = self.intent()
        with self.assertRaises(f.InjectedFault):
            self.fixture.switch(intent, "before-replace")
        self.assertEqual(self.fixture.pointer.read_bytes(), f.OLD_POINTER)
        self.assertEqual(self.fixture.pending.read_bytes(), f.NEW_POINTER)
        for action in (lambda: self.fixture.switch(intent), lambda: self.fixture.record(intent)):
            with self.assertRaises(ValueError):
                action()
        self.unchanged()

    def test_after_replace_fault_review_actual_result_without_replay(self):
        intent = self.intent()
        with self.assertRaises(f.InjectedFault):
            self.fixture.switch(intent, "after-replace")
        self.assertEqual(self.fixture.observe().active, self.fixture.new)
        with self.assertRaises(ValueError):
            self.fixture.switch(intent)
        active = self.fixture.record(intent)
        self.assertIs(active.phase, m.Phase.ACTIVATED)
        self.unchanged()

    def test_replace_denial_keeps_pointer_and_pending(self):
        intent = self.intent()
        with patch.object(f.os, "replace", side_effect=PermissionError("synthetic denial")):
            with self.assertRaises(PermissionError):
                self.fixture.switch(intent)
        self.assertEqual(self.fixture.pointer.read_bytes(), f.OLD_POINTER)
        self.assertEqual(self.fixture.pending.read_bytes(), f.NEW_POINTER)
        self.unchanged()

    def test_modified_staging_data_or_recovery_denies_switch(self):
        intent = self.intent()
        for path in (self.fixture.stage_dir / "version.txt", self.fixture.data, self.fixture.recovery):
            with self.subTest(path=path.name):
                original = path.read_bytes()
                path.write_bytes(b"changed synthetic marker")
                with self.assertRaises(ValueError):
                    self.fixture.switch(intent)
                path.write_bytes(original)
        self.assertEqual(self.fixture.pointer.read_bytes(), f.OLD_POINTER)
        self.unchanged()

    def test_stale_checkpoint_and_unknown_pointer_fail_closed(self):
        intent = self.intent()
        with self.assertRaises(ValueError):
            self.fixture.switch(self.initial)
        self.fixture.pointer.write_bytes(b"unknown")
        with self.assertRaises(ValueError):
            self.fixture.switch(intent)
        self.assertEqual(self.fixture.pointer.read_bytes(), b"unknown")
        self.unchanged()

    def test_unexpected_stage_file_or_hardlink_denied(self):
        intent = self.intent()
        extra = self.fixture.stage_dir / "extra.txt"
        extra.write_bytes(b"unexpected")
        with self.assertRaises(ValueError):
            self.fixture.switch(intent)
        extra.unlink()
        link = self.fixture.root / "linked.txt"
        try:
            f.os.link(self.fixture.data, link)
        except OSError:
            self.skipTest("Host does not support hardlinks")
        with self.assertRaises(ValueError):
            self.fixture.switch(intent)
        self.assertEqual(self.fixture.pointer.read_bytes(), f.OLD_POINTER)
        self.unchanged()
