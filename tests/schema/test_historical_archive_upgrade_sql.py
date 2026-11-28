"""Exact v47 migration SQL on a minimal synthetic v46 boundary, not native CLR."""
from pathlib import Path
import re
import sqlite3
import unittest

SOURCE = Path(__file__).resolve().parents[2] / 'src/PracticeManager.Persistence.Sqlite/HistoricalProjectArchiveMigration.cs'
TEXT = SOURCE.read_text(encoding='utf-8')
DDL = re.search(r'Exec\(c,t,"""\n(.*?)\n"""\);', TEXT, re.S).group(1)
GRANTS = re.search(r'foreach\(var grant in new\[\]\{(.*?)\}\)', TEXT).group(1)
GRANTS = re.findall(r'"([^"]+)"', GRANTS)
STATEMENTS = re.findall(r'Exec\(c,t,"([^"\n]+)"', TEXT)

class ArchiveUpgradeSqlTests(unittest.TestCase):
    def setUp(self):
        self.db = sqlite3.connect(':memory:')
        self.addCleanup(self.db.close)
        self.db.executescript('''PRAGMA foreign_keys=ON;
            CREATE TABLE roles(id INTEGER PRIMARY KEY,name TEXT UNIQUE);
            CREATE TABLE permissions(code TEXT PRIMARY KEY);
            CREATE TABLE role_permissions(role_id INTEGER REFERENCES roles(id),permission_code TEXT REFERENCES permissions(code),allowed INTEGER,PRIMARY KEY(role_id,permission_code));
            CREATE TABLE users(id INTEGER PRIMARY KEY);
            CREATE TABLE clients(id INTEGER PRIMARY KEY,client_no TEXT,company_name TEXT,zip TEXT,status TEXT);
            CREATE TABLE contacts(id INTEGER PRIMARY KEY,client_id INTEGER REFERENCES clients(id),first_name TEXT,is_primary INTEGER);
            CREATE TABLE legacy_crm_batches(id INTEGER PRIMARY KEY,source_hash TEXT,client_count INTEGER,contact_count INTEGER,review_note TEXT,imported_by INTEGER REFERENCES users(id),imported_utc TEXT);
            CREATE TABLE legacy_client_map(legacy_id TEXT PRIMARY KEY COLLATE NOCASE,legacy_number TEXT,client_id INTEGER REFERENCES clients(id),batch_id INTEGER REFERENCES legacy_crm_batches(id));
            CREATE TABLE legacy_contact_map(legacy_id TEXT PRIMARY KEY COLLATE NOCASE,legacy_client_id TEXT REFERENCES legacy_client_map(legacy_id),contact_id INTEGER REFERENCES contacts(id),batch_id INTEGER REFERENCES legacy_crm_batches(id));
            CREATE TABLE sequences(name TEXT PRIMARY KEY,next_value INTEGER);
            INSERT INTO roles VALUES(1,'Administrator'),(2,'Engineer');
            INSERT INTO permissions VALUES('Clients.View'),('Clients.Edit');
            INSERT INTO role_permissions VALUES(1,'Clients.View',1),(2,'Clients.View',1),(2,'Clients.Edit',0);
            INSERT INTO users VALUES(1);
            INSERT INTO clients VALUES(7,'UP-000007','Synthetic Alpha','00501','Active'),(8,'UP-000008','Synthetic Beta','00999','Inactive');
            INSERT INTO contacts VALUES(11,7,'Synthetic First',1),(12,8,'Synthetic Second',0);
            INSERT INTO legacy_client_map VALUES('001','OLD-1',7,NULL),('002','OLD-2',8,NULL);
            INSERT INTO legacy_contact_map VALUES('11','001',11,NULL),('12','002',12,NULL);
            INSERT INTO sequences VALUES('Client',9),('Project',23);
            PRAGMA user_version=46;''')
        self.db.execute('INSERT INTO legacy_crm_batches VALUES(1,?,2,2,\'review\',1,\'date\')', ('A'*64,))
        self.db.execute('UPDATE legacy_client_map SET batch_id=1')
        self.db.execute('UPDATE legacy_contact_map SET batch_id=1')
        self.db.commit()

    def snapshot(self):
        return [self.db.execute('SELECT * FROM '+table+' ORDER BY 1').fetchall() for table in
                ('clients','contacts','legacy_crm_batches','legacy_client_map','legacy_contact_map','sequences','roles','users')]

    def upgrade(self):
        self.assertEqual(len(STATEMENTS), 3)
        self.assertEqual(len(GRANTS), 2)
        try:
            self.db.executescript('BEGIN IMMEDIATE;\n'+DDL)
            for grant in GRANTS:
                for sql in STATEMENTS[:2]:
                    self.db.execute(sql, {'g': grant})
            self.db.execute(STATEMENTS[2])
            self.db.commit()
        except Exception:
            self.db.rollback()
            raise

    def test_upgrade_preserves_crm_and_existing_grants(self):
        before = self.snapshot()
        existing = self.db.execute('SELECT * FROM role_permissions ORDER BY 1,2').fetchall()
        self.upgrade()
        self.assertEqual(self.snapshot(), before)
        self.assertEqual(self.db.execute('PRAGMA user_version').fetchone(), (47,))
        self.assertEqual(self.db.execute('SELECT * FROM historical_project_register').fetchall(), [])
        self.assertEqual(self.db.execute('SELECT * FROM role_permissions ORDER BY 1,2').fetchall(), sorted(existing+[(1,g,1) for g in GRANTS]))
        self.assertEqual(self.db.execute('PRAGMA foreign_key_check').fetchall(), [])

    def test_preexisting_explicit_denial_not_overwritten(self):
        self.db.execute('INSERT INTO permissions VALUES(?)', (GRANTS[0],))
        self.db.execute('INSERT INTO role_permissions VALUES(1,?,0)', (GRANTS[0],))
        self.db.commit()
        self.upgrade()
        self.assertEqual(self.db.execute('SELECT allowed FROM role_permissions WHERE role_id=1 AND permission_code=?', (GRANTS[0],)).fetchone(), (0,))

    def test_late_grant_failure_rolls_back_ddl_grants_and_version(self):
        self.db.execute("CREATE TRIGGER synthetic_abort BEFORE INSERT ON permissions WHEN NEW.code='Administration.HistoricalProjectImport' BEGIN SELECT RAISE(ABORT,'synthetic fault'); END")
        self.db.commit()
        before = self.snapshot()
        schema = self.db.execute('SELECT * FROM sqlite_master ORDER BY name').fetchall()
        grants = self.db.execute('SELECT * FROM role_permissions ORDER BY 1,2').fetchall()
        with self.assertRaises(sqlite3.IntegrityError):
            self.upgrade()
        self.assertEqual(self.db.execute('PRAGMA user_version').fetchone(), (46,))
        self.assertEqual(self.db.execute('SELECT * FROM sqlite_master ORDER BY name').fetchall(), schema)
        self.assertEqual(self.db.execute('SELECT * FROM role_permissions ORDER BY 1,2').fetchall(), grants)
        self.assertEqual(self.snapshot(), before)
