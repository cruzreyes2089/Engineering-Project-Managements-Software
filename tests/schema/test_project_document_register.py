"""Inactive document/folder register structural tests, entirely synthetic."""
import sqlite3
import unittest
from pathlib import Path

DDL = (Path(__file__).resolve().parents[2] / "tools/migration/prototypes/project_document_register.sql").read_text()
H = lambda n: format(n, "064x")
K = lambda n: format(n, "032x")


class DocumentRegisterTests(unittest.TestCase):
    def setUp(self):
        self.db = sqlite3.connect(":memory:")
        self.db.executescript("""
        PRAGMA foreign_keys=ON;
        CREATE TABLE projects(id INTEGER PRIMARY KEY);
        CREATE TABLE users(id INTEGER PRIMARY KEY);
        CREATE TABLE audit_events(id INTEGER PRIMARY KEY, actor_id INTEGER REFERENCES users(id),action TEXT,entity TEXT,entity_id INTEGER);
        CREATE TABLE project_documents(id INTEGER PRIMARY KEY,project_id INTEGER REFERENCES projects(id),supersedes_id INTEGER REFERENCES project_documents(id),classification TEXT,category TEXT,file_name TEXT,file_hash TEXT);
        INSERT INTO projects VALUES(1),(2);
        INSERT INTO users VALUES(1),(2);
        """)
        self.db.executemany("INSERT INTO project_documents VALUES(?,?,?,?,?,?,?)",[
            (1,1,None,"Production","Drawings","drawing.pdf",H(101)),
            (2,1,None,"Restricted","Drawings","private.pdf",H(102)),
            (3,2,None,"Production","Reports","report.pdf",H(103)),
            (4,1,None,"Production","Reports","report.txt",H(104))])
        self.db.executescript("CREATE TABLE project_folder_configurations(revision INTEGER PRIMARY KEY,record_hash TEXT UNIQUE);")
        self.db.execute("INSERT INTO project_folder_configurations VALUES(1,?)",(H(900),))
        self.db.executescript(DDL)

    def tearDown(self):
        self.db.close()

    def audit(self, project, action, actor=1):
        return self.db.execute("INSERT INTO audit_events(actor_id,action,entity,entity_id) VALUES(?,?,'Project',?)",(actor,action,project)).lastrowid

    def folder(self, project=1, rev=1, ident=1, prior=None, state="Registered", path="C:/PROJECTS/A"):
        aid=self.audit(project,"RecordProjectFolderReference")
        self.db.execute("INSERT INTO project_folder_revisions VALUES(?,?,?,?,?,?,?,?,?,?,?,?,?,?)",
                        (project,rev,K(ident),prior,H(ident),state,path,H(900),path.removeprefix("C:/PROJECTS/"),path,"Synthetic review",1,"UTC",aid))

    def intent(self, ident=1, project=1, count=1):
        self.db.execute("INSERT INTO project_transmittal_intents VALUES(?,?,?,?,?,?,?)",(ident,project,K(ident),H(ident),"Synthetic recipient","Review",count))

    def document(self, doc=1, intent=1):
        self.db.execute("INSERT INTO project_transmittal_documents VALUES(?,?,?)",(intent,doc,H(100+doc)))

    def event(self, seq=1, ident=10, prior=None, state="Prepared", intent=1, project=1):
        aid=self.audit(project,"RecordTransmittalEvent")
        self.db.execute("INSERT INTO project_transmittal_events VALUES(?,?,?,?,?,?,?,?,?,?,?,?)",
                        (intent,seq,K(ident),prior,H(ident),state,"Synthetic external evidence","Reviewed",1,"UTC",aid,H(1000+ident)))

    def reject(self, operation):
        with self.assertRaises(sqlite3.IntegrityError):
            with self.db:
                operation()

    def test_folder_overlap_and_retirement(self):
        with self.db:
            self.folder()
        self.reject(lambda:self.folder(project=2,ident=2,path="C:/PROJECTS/A/DRAWINGS"))
        self.reject(lambda:self.folder(project=2,ident=2,path="C:/PROJECTS"))
        self.reject(lambda:self.folder(project=2,ident=2,path="C:/PROJECTS/A"))
        self.reject(lambda:self.folder(rev=2,ident=2,prior=H(999),path="C:/PROJECTS/B"))
        self.reject(lambda:self.folder(rev=2,ident=2,prior=H(1)))
        with self.db:
            self.folder(rev=2,ident=2,prior=H(1),state="Retired")
            self.folder(project=2,ident=3)
        self.assertEqual(self.db.execute("SELECT project_id,path_key FROM current_project_folder_references").fetchall(),[(2,"C:/PROJECTS/A")])
        self.assertEqual(self.db.execute("SELECT count(*) FROM project_folder_revisions").fetchone()[0],3)
        self.assertEqual(self.db.execute("SELECT count(*) FROM audit_events").fetchone()[0],3)

    def test_manifest_restrictions_and_seal(self):
        with self.db:
            self.intent(count=2)
            self.document()
        for d in [1,2,3,4]:
            self.reject(lambda d=d:self.document(d))
        self.reject(lambda:self.event())
        self.assertEqual(self.db.execute("SELECT count(*) FROM audit_events").fetchone()[0],0)
        with self.db:
            self.db.execute("INSERT INTO project_documents VALUES(5,1,NULL,'Production','Reports','second.pdf',?)",(H(105),))
            self.document(5)
            self.event()
        self.reject(lambda:self.document(5))
        self.reject(lambda:self.db.execute("UPDATE project_transmittal_documents SET file_hash=?",(H(999),)))

    def test_issue_delivery_and_withdrawal_history(self):
        with self.db:
            self.intent()
            self.document()
            self.event()
        self.reject(lambda:self.event(2,11,H(10),"DeliveryRecorded"))
        self.reject(lambda:self.event(2,11,H(99),"IssueRecorded"))
        self.reject(lambda:self.event(2,11,H(10),"IssueRecorded",project=2))
        with self.db:
            self.event(2,11,H(10),"IssueRecorded")
            self.db.execute("INSERT INTO project_documents VALUES(5,1,1,'Production','Drawings','drawing.pdf',?)",(H(105),))
            self.event(3,12,H(11),"DeliveryRecorded")
            self.event(4,13,H(12),"Withdrawn")
        self.reject(lambda:self.event(4,14,H(13),"IssueRecorded"))
        self.assertEqual(self.db.execute("SELECT state FROM project_transmittal_events ORDER BY sequence").fetchall(),
                         [("Prepared",),("IssueRecorded",),("DeliveryRecorded",),("Withdrawn",)])

    def test_supersession_blocks_new_issue_not_prior_history(self):
        with self.db:
            self.intent()
            self.document()
            self.event()
            self.db.execute("INSERT INTO project_documents VALUES(5,1,1,'Production','Drawings','drawing.pdf',?)",(H(105),))
        self.reject(lambda:self.event(2,11,H(10),"IssueRecorded"))
        with self.db:
            self.event(2,11,H(10),"Withdrawn")
        self.assertEqual(self.db.execute("SELECT count(*) FROM project_transmittal_events").fetchone()[0],2)

    def test_immutable_records_and_linked_audit(self):
        with self.db:
            self.folder()
            self.intent()
            self.document()
            self.event()
        for sql in [
            "UPDATE project_folder_revisions SET reason='changed'",
            "DELETE FROM project_folder_revisions",
            "DELETE FROM project_transmittal_intents",
            "DELETE FROM project_transmittal_documents",
            "DELETE FROM project_transmittal_events",
            "UPDATE audit_events SET actor_id=2",
            "DELETE FROM audit_events",
            "INSERT OR REPLACE INTO audit_events VALUES(1,2,'changed','Project',1)",
            "INSERT OR REPLACE INTO project_transmittal_documents SELECT * FROM project_transmittal_documents",
            "INSERT OR REPLACE INTO project_transmittal_intents SELECT * FROM project_transmittal_intents",
            "INSERT OR REPLACE INTO project_transmittal_events SELECT * FROM project_transmittal_events",
            "INSERT OR REPLACE INTO project_folder_revisions SELECT * FROM project_folder_revisions"]:
            self.reject(lambda sql=sql:self.db.execute(sql))
        self.assertEqual(self.db.execute("SELECT count(*) FROM audit_events").fetchone()[0],2)

    def test_failed_preparation_rolls_back_complete_transaction(self):
        def fail():
            self.intent(count=2)
            self.document()
            self.event()
        self.reject(fail)
        for table in ["project_transmittal_intents","project_transmittal_documents","project_transmittal_events","audit_events"]:
            self.assertEqual(self.db.execute("SELECT count(*) FROM "+table).fetchone()[0],0)
