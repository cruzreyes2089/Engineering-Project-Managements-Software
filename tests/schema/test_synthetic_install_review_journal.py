from concurrent.futures import ThreadPoolExecutor
from contextlib import closing
from dataclasses import replace
from pathlib import Path
import sqlite3
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "tools/release"))
import install_transaction_model as m
import synthetic_install_review_journal as j

class InstallReviewJournalTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="crc-review-journal-")
        self.addCleanup(self.temp.cleanup)
        self.path = Path(self.temp.name) / "synthetic-review.db"
        with self.path.open("xb"):
            pass
        self.db = self.open()
        self.addCleanup(self.db.close)
        self.old = m.Version("a"*40, "b"*64, 46)
        self.new = m.Version("c"*40, "d"*64, 47)
        self.plan = m.Plan(self.old, self.new, "e"*64, "f"*64, "e"*64, self.new, "f"*64)
        self.obs = m.Observation(self.old, self.new, True, True, True, True, 46, "e"*64, "f"*64)
        self.initial = j.initialize(self.db, self.plan)

    def open(self):
        db = sqlite3.connect(self.path, timeout=5)
        db.execute("PRAGMA journal_mode=DELETE")
        db.execute("PRAGMA synchronous=FULL")
        return db

    def advance(self, expected=None, obs=None):
        return j.append_review(self.db, self.plan, expected or self.initial, obs or self.obs)

    def test_complete_review_chain_and_reopen(self):
        stage = self.advance()
        intent = self.advance(stage)
        switched = replace(self.obs, active=self.new)
        active = self.advance(intent, switched)
        accepted = self.advance(active, replace(switched, data_schema=47,
                                      data_sha256="1"*64, acceptance_verified=True))
        self.assertEqual(accepted.phase, m.Phase.ACCEPTED)
        with closing(self.open()) as db:
            self.assertEqual(j.reconcile(db, self.plan, accepted), accepted)
        with self.assertRaises(ValueError):
            self.advance(accepted, switched)

    def test_stale_checkpoint_and_lost_commit_receipt_hold(self):
        stage = self.advance()
        with closing(self.open()) as db:
            with self.assertRaises(ValueError):
                j.reconcile(db, self.plan, self.initial)
            with self.assertRaises(ValueError):
                j.append_review(db, self.plan, self.initial, self.obs)
            self.assertEqual(j.reconcile(db, self.plan, stage), stage)

    def test_failure_before_commit_preserves_previous_checkpoint(self):
        self.db.execute("""CREATE TRIGGER synthetic_fail BEFORE INSERT ON synthetic_install_review
                         WHEN NEW.seq=1 BEGIN SELECT RAISE(ABORT,'Synthetic disk/write fault'); END""")
        self.db.commit()
        with self.assertRaises(sqlite3.IntegrityError):
            self.advance()
        self.assertEqual(j.reconcile(self.db, self.plan, self.initial), self.initial)
        self.assertFalse(self.db.in_transaction)
        self.db.execute("DROP TRIGGER synthetic_fail")
        self.assertEqual(self.advance().phase, m.Phase.STAGED)

    def test_uncommitted_write_disappears_after_connection_close(self):
        # Model process termination before commit, not an OS power-loss simulation.
        with closing(self.open()) as db:
            db.execute("BEGIN IMMEDIATE")
            raw = j.canonical(j.asdict(self.obs))
            ph = j.plan_hash(self.plan)
            h = j.entry_hash(1, m.Phase.STAGED.value, ph, self.initial.sha256, raw)
            db.execute("INSERT INTO synthetic_install_review VALUES(?,?,?,?,?,?)",
                       (1, m.Phase.STAGED.value, ph, self.initial.sha256, raw, h))
        self.assertEqual(j.reconcile(self.db, self.plan, self.initial), self.initial)

    def test_append_only_and_reinitialization(self):
        stage = self.advance()
        for sql in ("UPDATE synthetic_install_review SET phase='accepted' WHERE seq=1",
                    "DELETE FROM synthetic_install_review WHERE seq=1",
                    "INSERT OR REPLACE INTO synthetic_install_review SELECT * FROM synthetic_install_review WHERE seq=1"):
            with self.subTest(sql=sql), self.assertRaises(sqlite3.IntegrityError):
                self.db.execute(sql)
            self.db.rollback()
        with self.assertRaises(ValueError):
            j.initialize(self.db, self.plan)
        self.assertEqual(j.reconcile(self.db, self.plan, stage), stage)

    def test_tampered_entry_or_truncated_chain_detected(self):
        stage = self.advance()
        self.db.execute("DROP TRIGGER synthetic_review_no_update")
        self.db.execute("UPDATE synthetic_install_review SET observation='{}' WHERE seq=1")
        self.db.commit()
        with self.assertRaises(ValueError):
            j.reconcile(self.db, self.plan, stage)
        self.db.execute("DROP TRIGGER synthetic_review_no_delete")
        self.db.execute("DELETE FROM synthetic_install_review WHERE seq=1")
        self.db.commit()
        with self.assertRaises(ValueError):
            j.reconcile(self.db, self.plan, stage)

    def test_exact_plan_and_observation_required(self):
        other = replace(self.plan, recovery_sha256="2"*64, rehearsed_recovery_sha256="2"*64)
        with self.assertRaises(ValueError):
            j.reconcile(self.db, other, self.initial)
        for obs in (replace(self.obs, exclusive_lock=False),
                    replace(self.obs, data_sha256="0"*64),
                    replace(self.obs, data_schema=True),
                    replace(self.obs, staged=None)):
            with self.subTest(obs=obs), self.assertRaises(ValueError):
                self.advance(obs=obs)
        self.assertEqual(j.reconcile(self.db, self.plan, self.initial), self.initial)

    def test_two_concurrent_review_writers_one_wins(self):
        def attempt():
            with closing(self.open()) as db:
                try:
                    return j.append_review(db, self.plan, self.initial, self.obs)
                except ValueError:
                    return None
        with ThreadPoolExecutor(max_workers=2) as pool:
            results = list(pool.map(lambda _: attempt(), range(2)))
        winners = [x for x in results if x is not None]
        self.assertEqual(len(winners), 1)
        self.assertEqual(j.reconcile(self.db, self.plan, winners[0]), winners[0])
