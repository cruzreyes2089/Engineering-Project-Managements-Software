import sqlite3
import unittest
from pathlib import Path

SQL = (Path(__file__).resolve().parents[2] / 'tools/migration/prototypes/historical_project_archive.sql').read_text(encoding='utf-8')

class HistoricalArchiveSqlTests(unittest.TestCase):
    def setUp(self):
        self.db = sqlite3.connect(':memory:')
        self.db.executescript('''PRAGMA foreign_keys=ON;
            PRAGMA recursive_triggers=OFF;
            CREATE TABLE users(id INTEGER PRIMARY KEY);
            CREATE TABLE clients(id INTEGER PRIMARY KEY);
            CREATE TABLE legacy_crm_batches(id INTEGER PRIMARY KEY, source_hash TEXT);
            CREATE TABLE legacy_client_map(legacy_id TEXT PRIMARY KEY COLLATE NOCASE,
                client_id INTEGER REFERENCES clients(id), batch_id INTEGER REFERENCES legacy_crm_batches(id));
            CREATE TABLE projects(id INTEGER PRIMARY KEY);
            CREATE TABLE sequences(name TEXT PRIMARY KEY,next_value INTEGER);
            INSERT INTO users VALUES(1);
            INSERT INTO clients VALUES(7),(8);
            INSERT INTO legacy_client_map VALUES('001',7, NULL),('002',8,NULL);
            INSERT INTO sequences VALUES('Project',23);
        ''')
        self.db.execute('INSERT INTO legacy_crm_batches VALUES(1,?)', ('A'*64,))
        self.db.execute('UPDATE legacy_client_map SET batch_id=1')
        self.db.executescript(SQL)

    def tearDown(self):
        self.db.close()

    def batch(self, crm='a'*64, count=2, prefix='INSERT'):
        self.db.execute(prefix+' INTO historical_project_batches VALUES(1,1,?,?,?,?,?,\'Synthetic review\',1,\'date\')',
                        (crm,'b'*64,'c'*64,'d'*64,count))

    def row(self, key='P1', parent='001', prefix='INSERT'):
        self.db.execute(prefix+' INTO historical_project_rows VALUES(1,?,?,\'OLD-1\',\'Synthetic\',\'\',\'Active\',\'Approved\',\'Reference\')', (key,parent))

    def seal(self, prefix='INSERT'):
        self.db.execute(prefix+" INTO historical_project_seals VALUES(1,'date')")

    def scalar(self, sql):
        return self.db.execute(sql).fetchone()[0]

    def test_crm_provenance(self):
        with self.assertRaises(sqlite3.IntegrityError): self.batch(crm='f'*64)
        self.batch()
        self.assertEqual(self.scalar('SELECT count(*) FROM historical_project_batches'),1)

    def test_seal_visibility_and_no_work_authorization(self):
        self.batch()
        self.row()
        self.assertEqual(self.scalar('SELECT count(*) FROM historical_project_register'),0)
        with self.assertRaises(sqlite3.IntegrityError): self.seal()
        self.row('P2','002')
        self.seal()
        self.assertEqual(self.db.execute('SELECT legacy_id,native_client_id,legacy_status,work_authorized FROM historical_project_register ORDER BY legacy_id').fetchall(),
                         [('P1',7,'Active',0),('P2',8,'Active',0)])
        self.assertEqual(self.scalar('SELECT count(*) FROM projects'),0)
        self.assertEqual(self.scalar("SELECT next_value FROM sequences WHERE name='Project'"),23)

    def test_orphan_duplicate_and_overcount(self):
        self.batch(count=1)
        with self.assertRaises(sqlite3.IntegrityError): self.row(parent='missing')
        self.row()
        for key in ('p1','P2'):
            with self.assertRaises(sqlite3.IntegrityError): self.row(key)

    def test_batch_and_row_replace_denied_before_sealing(self):
        self.batch()
        self.row()
        with self.assertRaises(sqlite3.IntegrityError): self.batch(prefix='INSERT OR REPLACE')
        with self.assertRaises(sqlite3.IntegrityError): self.row('p1',prefix='INSERT OR REPLACE')

    def test_immutable_after_sealing(self):
        self.batch(count=1)
        self.row()
        self.seal()
        with self.assertRaises(sqlite3.IntegrityError): self.seal(prefix='INSERT OR REPLACE')
        with self.assertRaises(sqlite3.IntegrityError): self.row('P2')
        for table in ('historical_project_batches','historical_project_rows','historical_project_seals'):
            for sql in (f'UPDATE {table} SET rowid=rowid',f'DELETE FROM {table}'):
                with self.subTest(sql=sql), self.assertRaises(sqlite3.IntegrityError): self.db.execute(sql)

    def test_rollback_removes_partial_batch(self):
        self.db.commit()
        with self.assertRaises(sqlite3.IntegrityError):
            with self.db:
                self.batch()
                self.row()
                self.seal()
        for table in ('historical_project_batches','historical_project_rows','historical_project_seals'):
            self.assertEqual(self.scalar(f'SELECT count(*) FROM {table}'),0)
        self.assertEqual(self.scalar('SELECT count(*) FROM legacy_client_map'),2)
