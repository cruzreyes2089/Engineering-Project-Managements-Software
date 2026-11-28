import hashlib
import sqlite3
import sys
import tempfile
import threading
import unittest
from concurrent.futures import ThreadPoolExecutor
from contextlib import closing
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / 'tools/migration/prototypes'))
import association_review as review
import test_historical_association_sql as fixture_module


class ReviewTests(unittest.TestCase):
    def setUp(self):
        fixture = fixture_module.HistoricalAssociationSqlTests()
        fixture.setUp()
        self.db = fixture.db
        self.db.execute('ALTER TABLE proposals ADD COLUMN snapshot TEXT')
        self.db.execute('UPDATE proposals SET snapshot=?,snapshot_hash=?', ('synthetic', hashlib.sha256(b'synthetic').hexdigest()))
        self.db.execute('CREATE TABLE prototype_association_audit(action TEXT,ident INTEGER,note TEXT,actor INTEGER,utc TEXT)')
        self.db.commit()

    def tearDown(self):
        self.db.close()

    def read(self, legacy='P01', native=1, db=None):
        db = self.db if db is None else db
        db.execute('BEGIN')
        try:
            return review.preview(db, 1, legacy, native)
        finally:
            db.rollback()

    def history(self, db=None):
        db = self.db if db is None else db
        db.execute('BEGIN')
        try:
            return review.fingerprint(db)
        finally:
            db.rollback()

    def link(self, token, db=None):
        return review.commit(self.db if db is None else db, token, True, 'Reviewed', 1, 'date')

    def test_fingerprint_survives_restore_and_covers_reversal(self):
        empty = self.history()
        ident = self.link(self.read())
        linked = self.history()
        review.reverse(self.db, ident, linked, True, 'Correction', 1, 'date2')
        reversed_hash = self.history()
        self.assertEqual(len({empty, linked, reversed_hash}), 3)
        with closing(sqlite3.connect(':memory:')) as copy:
            self.db.backup(copy)
            self.assertEqual(self.history(copy), reversed_hash)
        self.assertEqual(self.db.execute('SELECT count(*) FROM prototype_association_audit').fetchone()[0], 2)

    def test_stale_review_after_link_and_reversal(self):
        token = self.read()
        ident = self.link(token)
        review.reverse(self.db, ident, self.history(), True, 'Correction', 1, 'date2')
        with self.assertRaises(ValueError):
            self.link(token)
        self.link(self.read())

    def test_stale_snapshot_bytes(self):
        token = self.read()
        self.db.execute("UPDATE proposals SET snapshot='changed' WHERE id=1")
        self.db.commit()
        with self.assertRaises(ValueError):
            self.link(token)

    def test_audit_failure_rolls_back_link_and_reversal(self):
        token = self.read()
        self.db.execute("CREATE TRIGGER fail_audit BEFORE INSERT ON prototype_association_audit BEGIN SELECT RAISE(ABORT,'failure'); END")
        self.db.commit()
        with self.assertRaises(sqlite3.IntegrityError):
            self.link(token)
        self.assertEqual(self.history(), token.history)
        self.db.execute('DROP TRIGGER fail_audit')
        ident = self.link(token)
        linked = self.history()
        self.db.execute("CREATE TRIGGER fail_audit BEFORE INSERT ON prototype_association_audit BEGIN SELECT RAISE(ABORT,'failure'); END")
        self.db.commit()
        with self.assertRaises(sqlite3.IntegrityError):
            review.reverse(self.db, ident, linked, True, 'Correction', 1, 'date2')
        self.assertEqual(self.history(), linked)

    def test_concurrent_review_commits_only_once(self):
        token = self.read()
        with tempfile.TemporaryDirectory() as folder:
            path = str(Path(folder) / 'synthetic.db')
            with closing(sqlite3.connect(path)) as copy:
                self.db.backup(copy)
            barrier = threading.Barrier(2)
            def worker():
                with closing(sqlite3.connect(path, timeout=5)) as db:
                    db.execute('PRAGMA foreign_keys=ON')
                    barrier.wait(timeout=5)
                    try:
                        self.link(token, db)
                        return 'committed'
                    except ValueError:
                        return 'rejected'
            with ThreadPoolExecutor(max_workers=2) as pool:
                results = list(pool.map(lambda _: worker(), range(2)))
            self.assertCountEqual(results, ['committed', 'rejected'])
            with closing(sqlite3.connect(path)) as db:
                self.assertEqual(db.execute('SELECT count(*) FROM historical_associations').fetchone()[0], 1)
                self.assertEqual(db.execute('SELECT count(*) FROM prototype_association_audit').fetchone()[0], 1)

    def test_confirmation_and_reason_required(self):
        token = self.read()
        for confirmed, note in [(False, 'Reviewed'), (True, ' '), (True, 'x'*1001)]:
            with self.assertRaises(ValueError):
                review.commit(self.db, token, confirmed, note, 1, 'date')
        self.assertEqual(self.history(), token.history)
