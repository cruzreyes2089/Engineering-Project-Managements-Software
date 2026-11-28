"""SQLite query checks, not CLR service or native recovery execution."""
from pathlib import Path
import re
import unittest
import test_historical_archive_sql as fixture

source = Path(__file__).resolve().parents[2] / 'src/PracticeManager.Persistence.Sqlite/HistoricalProjectArchive.cs'
text = source.read_text(encoding='utf-8')
COUNT = re.search(r'Scalar\(c,t,"(SELECT count\(\*\) FROM audit_events WHERE action=\'ImportHistoricalProjects\')"\)', text).group(1)
MATCH = re.search(r'Scalar\(c,t,@"(SELECT count\(\*\) FROM audit_events a JOIN historical_project_batches b\s+ON b.id=a.entity_id AND b.imported_by=a.actor_id WHERE a.action=\'ImportHistoricalProjects\'\s+AND a.entity=\'HistoricalProjectBatch\' AND b.id=1)"\)', text).group(1)

class HistoricalArchiveAuditQueryTests(unittest.TestCase):
    def setUp(self):
        self.f = fixture.HistoricalArchiveSqlTests()
        self.f.setUp()
        self.addCleanup(self.f.tearDown)
        self.db = self.f.db
        self.db.execute('CREATE TABLE audit_events(id INTEGER PRIMARY KEY, actor_id INTEGER REFERENCES users(id), action TEXT NOT NULL, entity TEXT NOT NULL, entity_id INTEGER, occurred_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP)')
        self.db.commit()

    def witness(self):
        return (self.f.scalar(COUNT), self.f.scalar(MATCH))

    def audit(self, actor=1, entity='HistoricalProjectBatch', ident=1):
        self.db.execute("INSERT INTO audit_events(actor_id,action,entity,entity_id) VALUES(?,'ImportHistoricalProjects',?,?)", (actor, entity, ident))

    def populate(self):
        self.f.batch(count=1)
        self.f.row()
        self.f.seal()

    def test_missing_and_valid(self):
        self.assertEqual(self.witness(), (0, 0))
        self.populate()
        self.assertEqual(self.witness(), (0, 0))
        self.audit()
        self.assertEqual(self.witness(), (1, 1))

    def test_duplicate(self):
        self.populate()
        self.audit()
        self.audit()
        self.assertEqual(self.witness(), (2, 2))

    def test_unbound_event(self):
        self.audit()
        self.assertEqual(self.witness(), (1, 0))

    def test_wrong_actor_entity_or_batch(self):
        self.populate()
        for actor, entity, ident in [(None, 'HistoricalProjectBatch', 1), (1, 'Firm', 1), (1, 'HistoricalProjectBatch', 2)]:
            with self.subTest(actor=actor, entity=entity, ident=ident):
                self.audit(actor, entity, ident)
                self.assertEqual(self.witness(), (1, 0))
                self.db.execute('DELETE FROM audit_events')

    def test_unrelated_events_ignored(self):
        self.populate()
        self.audit()
        self.db.execute("INSERT INTO audit_events(actor_id,action,entity,entity_id) VALUES(1,'Login','User',1)")
        self.assertEqual(self.witness(), (1, 1))
