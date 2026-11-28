"""Exercise SQL statements extracted from the staged C# service, not its CLR code."""
from pathlib import Path
import re
import sqlite3
import unittest
import test_historical_archive_sql as fixture

SOURCE = Path(__file__).resolve().parents[2] / "src/PracticeManager.Persistence.Sqlite/HistoricalProjectArchive.cs"
COMMANDS = dict(re.findall(r'private const string (Archive\w+)="([^"\n]*)";',
                           SOURCE.read_text(encoding="utf-8")))


class ArchiveCandidateSqlTests(unittest.TestCase):
    def setUp(self):
        self.fixture = fixture.HistoricalArchiveSqlTests()
        self.fixture.setUp()
        self.db = self.fixture.db
        self.db.execute("CREATE TABLE audit_events(actor_id INTEGER,action TEXT,entity TEXT,entity_id INTEGER)")
        self.db.commit()
        self.addCleanup(self.fixture.tearDown)

    def cmd(self, name, **params):
        return self.db.execute(COMMANDS[name], params)

    def counts(self):
        return self.cmd("ArchiveCountsQuery").fetchone()

    def batch(self):
        self.cmd("ArchiveInsertBatch", s="b"*64, c="a"*64, p="c"*64, h="d"*64,
                 n=2, r="Synthetic review", a=1, u="date")

    def row(self, key, parent):
        self.cmd("ArchiveInsertRow", i=key, c=parent, n="OLD-1", t=" Historical ",
                 s="", l="Active", a="Approved", e="Synthetic reference")

    def seal(self):
        self.cmd("ArchiveInsertSeal", u="date")

    def audit(self):
        self.db.execute("INSERT INTO audit_events VALUES(1,'CommitHistoricalProjectArchive','Firm',1)")

    def complete(self):
        self.db.execute("BEGIN IMMEDIATE")
        with self.db:
            self.batch()
            self.row("P1", "001")
            self.row("P2", "002")
            self.seal()
            self.audit()
            self.assertEqual(self.counts(), (2, 1, 1, 1))

    def test_empty_target(self):
        self.assertEqual(self.counts(), (0, 0, 0, 0))
        self.assertIsNone(self.cmd("ArchiveBatchQuery").fetchone())

    def test_exact_stored_readback_and_no_operational_writes(self):
        self.complete()
        self.assertEqual(self.cmd("ArchiveBatchQuery").fetchone(),
                         ("b"*64, "a"*64, "c"*64, "d"*64, 2))
        self.assertEqual(self.cmd("ArchiveRowsQuery").fetchall(), [
            ("P1", "001", 7, "OLD-1", " Historical ", "", "Active", "Approved", "Synthetic reference", 0),
            ("P2", "002", 8, "OLD-1", " Historical ", "", "Active", "Approved", "Synthetic reference", 0)])
        self.assertEqual(self.fixture.scalar("SELECT count(*) FROM projects"), 0)
        self.assertEqual(self.fixture.scalar("SELECT next_value FROM sequences WHERE name='Project'"), 23)

    def test_unsealed_and_unaudited_states_distinguishable(self):
        self.batch()
        self.row("P1", "001")
        self.assertEqual(self.counts(), (1, 0, 1, 0))
        self.assertEqual(self.cmd("ArchiveRowsQuery").fetchall(), [])
        self.row("P2", "002")
        self.seal()
        self.assertEqual(self.counts(), (2, 1, 1, 0))

    def test_row_failure_rolls_back(self):
        with self.assertRaises(sqlite3.IntegrityError):
            self.db.execute("BEGIN IMMEDIATE")
            with self.db:
                self.batch()
                self.row("P1", "001")
                self.row("P2", "missing")
        self.assertEqual(self.counts(), (0, 0, 0, 0))

    def test_audit_failure_rolls_back_sealed_rows(self):
        self.db.execute("CREATE TRIGGER reject_audit BEFORE INSERT ON audit_events BEGIN SELECT RAISE(ABORT,'Synthetic fault'); END")
        self.db.commit()
        with self.assertRaises(sqlite3.IntegrityError):
            self.complete()
        self.assertEqual(self.counts(), (0, 0, 0, 0))
        self.assertEqual(self.cmd("ArchiveRowsQuery").fetchall(), [])

    def test_second_batch_cannot_replace_sealed_evidence(self):
        self.complete()
        with self.assertRaises(sqlite3.IntegrityError):
            with self.db:
                self.batch()
        self.assertEqual(self.counts(), (2, 1, 1, 1))
