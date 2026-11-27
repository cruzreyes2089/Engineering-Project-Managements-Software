import pathlib, sqlite3, unittest
SQL = pathlib.Path(__file__).resolve().parents[2] / 'src/PracticeManager.Persistence.Sqlite/schema.sql'
class SchemaTests(unittest.TestCase):
    def setUp(self):
        self.db=sqlite3.connect(':memory:'); self.db.execute('PRAGMA foreign_keys=ON'); self.db.executescript(SQL.read_text());
        self.db.execute("INSERT INTO firms(id,name,client_prefix) VALUES(1,'Test firm','CL')")
        self.db.execute("INSERT INTO roles(id,name) VALUES(1,'Administrator')")
        self.db.execute("INSERT INTO users(id,username,normalized_username,password_hash,role_id,active) VALUES(1,'Test','TEST','synthetic',1,1)")
        self.db.execute("INSERT INTO clients(id,client_no,client_type,company_name,status) VALUES(1,'CL-000001','Business / Company','Test Client','Active')")
    def tearDown(self): self.db.close()
    def test_version(self): self.assertEqual(self.db.execute('PRAGMA user_version').fetchone()[0],1)
    def test_single_firm(self):
        with self.assertRaises(sqlite3.IntegrityError): self.db.execute("INSERT INTO firms(id,name,client_prefix) VALUES(2,'Other','CL')")
    def test_unique_client_number(self):
        with self.assertRaises(sqlite3.IntegrityError): self.db.execute("INSERT INTO clients(client_no,client_type,company_name,status) VALUES('CL-000001','Business / Company','Other','Active')")
    def test_orphan_contact(self):
        with self.assertRaises(sqlite3.IntegrityError): self.db.execute("INSERT INTO contacts(client_id,first_name,last_name) VALUES(999,'A','B')")
    def test_one_primary(self):
        self.db.execute("INSERT INTO contacts(client_id,first_name,last_name,is_primary) VALUES(1,'A','B',1)")
        with self.assertRaises(sqlite3.IntegrityError): self.db.execute("INSERT INTO contacts(client_id,first_name,last_name,is_primary) VALUES(1,'C','D',1)")
    def test_unique_login(self):
        with self.assertRaises(sqlite3.IntegrityError): self.db.execute("INSERT INTO users(username,normalized_username,password_hash,role_id,active) VALUES('test','TEST','synthetic',1,1)")
    def test_grants_default_empty(self): self.assertEqual(self.db.execute('SELECT count(*) FROM role_permissions').fetchone()[0],0)
    def test_invalid_status(self):
        with self.assertRaises(sqlite3.IntegrityError): self.db.execute("UPDATE clients SET status='Bogus' WHERE id=1")
    def test_orphan_role(self):
        with self.assertRaises(sqlite3.IntegrityError): self.db.execute("UPDATE users SET role_id=99 WHERE id=1")
if __name__=='__main__': unittest.main(verbosity=2)
