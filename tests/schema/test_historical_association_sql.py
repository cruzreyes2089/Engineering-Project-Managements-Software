import sqlite3
import unittest
from pathlib import Path

SQL = (Path(__file__).resolve().parents[2] / 'tools/migration/prototypes/historical_project_association.sql').read_text(encoding='utf-8')

class HistoricalAssociationSqlTests(unittest.TestCase):
    def setUp(self):
        self.db = sqlite3.connect(':memory:')
        self.db.executescript('''PRAGMA foreign_keys=ON;
            PRAGMA recursive_triggers=OFF;
            CREATE TABLE users(id INTEGER PRIMARY KEY);
            CREATE TABLE historical_project_seals(batch_id INTEGER PRIMARY KEY);
            CREATE TABLE historical_project_rows(batch_id INTEGER, legacy_id TEXT COLLATE NOCASE, native_client_id INTEGER, project_transfer_hash TEXT, PRIMARY KEY(batch_id,legacy_id));
            CREATE VIEW historical_project_register AS SELECT r.* FROM historical_project_rows r JOIN historical_project_seals s ON s.batch_id=r.batch_id;
            CREATE TABLE proposals(id INTEGER PRIMARY KEY,client_id INTEGER,state TEXT,snapshot_hash TEXT);
            CREATE TABLE engagements(id INTEGER PRIMARY KEY,proposal_id INTEGER);
            CREATE TABLE projects(id INTEGER PRIMARY KEY,engagement_id INTEGER,state TEXT);
            INSERT INTO users VALUES(1);
            INSERT INTO historical_project_seals VALUES(1);
            INSERT INTO engagements VALUES(1,1),(2,2),(3,3);
            INSERT INTO projects VALUES(1,1,'Pending'),(2,2,'Pending'),(3,3,'Pending');
        ''')
        self.db.executemany('INSERT INTO historical_project_rows VALUES(1,?,?,?)', [('P01',7,'a'*64),('P02',7,'a'*64),('P03',8,'a'*64)])
        self.db.executemany('INSERT INTO proposals VALUES(?,?,?,?)', [(1,7,'Accepted','B'*64),(2,7,'Accepted','B'*64),(3,8,'Accepted','B'*64)])
        self.db.executescript(SQL)

    def tearDown(self):
        self.db.close()

    def link(self, ident=1, legacy='P01', native=1, archive='a'*64, proposal='B'*64, prefix='INSERT'):
        self.db.execute(prefix+' INTO historical_associations VALUES(?,1,?,?,?,?,\'Review\',1,\'date\')', (ident,legacy,native,archive,proposal))

    def reverse(self, ident=1, prefix='INSERT'):
        self.db.execute(prefix+" INTO historical_association_reversals VALUES(?,'Reason',1,'date')", (ident,))

    def count(self, table):
        return self.db.execute('SELECT count(*) FROM '+table).fetchone()[0]

    def test_valid_link_cannot_authorize_work(self):
        self.link()
        self.assertEqual(self.db.execute('SELECT work_authorized FROM historical_active_associations').fetchone()[0],0)
        self.assertEqual(self.db.execute('SELECT state FROM projects WHERE id=1').fetchone()[0],'Pending')

    def test_one_to_one_active_links(self):
        self.link()
        for legacy,native in [('P01',2),('P02',1)]:
            with self.assertRaises(sqlite3.IntegrityError): self.link(2,legacy,native)

    def test_exact_evidence_and_client(self):
        for args in [dict(legacy='p01'),dict(native=3),dict(archive='c'*64),dict(proposal='D'*64),dict(native=999)]:
            with self.subTest(args=args), self.assertRaises(sqlite3.IntegrityError): self.link(**args)
        self.db.execute("UPDATE proposals SET state='Draft' WHERE id=1")
        with self.assertRaises(sqlite3.IntegrityError): self.link()

    def test_reversal_retains_history_and_allows_new_link(self):
        self.link()
        self.reverse()
        self.assertEqual(self.count('historical_active_associations'),0)
        self.link(2,'P01',2)
        self.assertEqual(self.count('historical_associations'),2)
        self.assertEqual(self.count('historical_association_reversals'),1)
        self.assertEqual(self.count('historical_active_associations'),1)
        with self.assertRaises(sqlite3.IntegrityError): self.reverse()

    def test_update_delete_replace_denied(self):
        self.link()
        self.reverse()
        with self.assertRaises(sqlite3.IntegrityError): self.link(prefix='INSERT OR REPLACE')
        with self.assertRaises(sqlite3.IntegrityError): self.reverse(prefix='INSERT OR REPLACE')
        for table in ('historical_associations','historical_association_reversals'):
            for sql in (f'UPDATE {table} SET rowid=rowid',f'DELETE FROM {table}'):
                with self.assertRaises(sqlite3.IntegrityError): self.db.execute(sql)

    def test_transaction_rollback(self):
        self.db.commit()
        with self.assertRaises(sqlite3.IntegrityError):
            with self.db:
                self.link()
                self.link(2,'P02',1)
        self.assertEqual(self.count('historical_associations'),0)
        self.assertEqual(self.count('projects'),3)
