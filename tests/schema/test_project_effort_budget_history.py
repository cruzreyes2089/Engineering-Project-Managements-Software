"""Synthetic DDL qualification only; no real database or production migration."""
import sqlite3
import tempfile
import threading
import unittest
from concurrent.futures import ThreadPoolExecutor
from contextlib import closing
from pathlib import Path

DDL = (Path(__file__).resolve().parents[2] / "tools/migration/prototypes/project_effort_budget_history.sql").read_text()
HASH = lambda n: format(n, "064x")
KEY = lambda n: format(n, "032x")


def fixture(db):
    db.executescript("""
    PRAGMA foreign_keys=ON;
    CREATE TABLE projects(id INTEGER PRIMARY KEY);
    CREATE TABLE users(id INTEGER PRIMARY KEY);
    CREATE TABLE audit_events(id INTEGER PRIMARY KEY, actor_id INTEGER REFERENCES users(id),
      action TEXT NOT NULL, entity TEXT NOT NULL, entity_id INTEGER);
    INSERT INTO projects VALUES(1),(2);
    INSERT INTO users VALUES(1),(2);
    """)
    db.executescript(DDL)


def audit(db, action, project=1, actor=1):
    return db.execute("INSERT INTO audit_events(actor_id,action,entity,entity_id) VALUES(?,?,'Project',?)",
                      (actor,action,project)).lastrowid


def budget(db, rev=1, previous=None, minutes=120, project=1, ident=1, actor=1, audit_id=None, replace=False):
    if audit_id is None:
        audit_id = audit(db, "RecordEffortBudgetRevision", project, actor)
    db.execute(("INSERT OR REPLACE" if replace else "INSERT") + " INTO effort_budget_revisions VALUES(?,?,?,?,?,?,?,?,?,?,?,?,?)",
               (project,rev,KEY(ident),previous,HASH(ident),minutes,"FirstRecorded" if rev==1 else "ScopeReview",
                "Synthetic reviewed reason",HASH(99),b"synthetic snapshot",actor,"synthetic UTC",audit_id))


def forecast(db, seq=1, previous=None, budget_id=1, project=1, ident=10, first=120, current=120,
             total=150, actor=1, audit_id=None, replace=False):
    if audit_id is None:
        audit_id = audit(db, "RecordEffortForecast", project, actor)
    db.execute(("INSERT OR REPLACE" if replace else "INSERT") + " INTO effort_forecast_records VALUES(?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?)",
               (project,seq,KEY(ident),previous,HASH(ident),HASH(budget_id),HASH(99),b"synthetic snapshot",b"synthetic remaining",
                first,current,60,15,75,total,"Synthetic reviewed forecast",actor,"synthetic UTC",audit_id))


class BudgetHistoryTests(unittest.TestCase):
    def setUp(self):
        self.db=sqlite3.connect(":memory:")
        fixture(self.db)

    def tearDown(self):
        self.db.close()

    def reject(self, action):
        with self.assertRaises(sqlite3.IntegrityError):
            with self.db:
                action()

    def count(self, table):
        return self.db.execute("SELECT count(*) FROM "+table).fetchone()[0]

    def test_append_and_forecast_history(self):
        with self.db:
            budget(self.db)
            forecast(self.db)
            budget(self.db,2,HASH(1),150,ident=2)
            forecast(self.db,2,HASH(10),2,ident=11,current=150)
        self.assertEqual(self.db.execute("SELECT minutes FROM effort_budget_revisions ORDER BY revision").fetchall(),[(120,),(150,)])
        self.assertEqual(self.db.execute("SELECT current_budget_minutes FROM effort_forecast_records ORDER BY sequence").fetchall(),[(120,),(150,)])
        self.assertEqual(self.count("audit_events"),4)
        with closing(sqlite3.connect(":memory:")) as copy:
            self.db.backup(copy)
            for name in ("effort_budget_revisions","effort_forecast_records","audit_events"):
                self.assertEqual(self.db.execute("SELECT * FROM "+name).fetchall(),copy.execute("SELECT * FROM "+name).fetchall())

    def test_stale_unchanged_and_cross_project_revisions(self):
        with self.db:
            budget(self.db)
            budget(self.db,2,HASH(1),150,ident=2)
            budget(self.db,project=2,ident=3)
        self.reject(lambda:budget(self.db,3,HASH(1),180,ident=4))
        self.reject(lambda:budget(self.db,3,HASH(2),150,ident=4))
        self.reject(lambda:budget(self.db,3,HASH(3),180,ident=4))
        self.reject(lambda:budget(self.db,4,HASH(2),180,ident=4))
        self.assertEqual(self.count("audit_events"),3)

    def test_forecast_requires_current_budget_and_exact_amounts(self):
        with self.db:
            budget(self.db)
            budget(self.db,2,HASH(1),150,ident=2)
            budget(self.db,project=2,ident=3)
        self.reject(lambda:forecast(self.db))
        self.reject(lambda:forecast(self.db,budget_id=2,current=149))
        self.reject(lambda:forecast(self.db,budget_id=2,current=150,first=121))
        self.reject(lambda:forecast(self.db,budget_id=2,current=150,total=151))
        self.reject(lambda:forecast(self.db,budget_id=3,current=120))
        with self.db:
            forecast(self.db,budget_id=2,current=150)
        self.reject(lambda:forecast(self.db,seq=2,previous=HASH(99),budget_id=2,current=150,ident=11))
        self.assertEqual(self.count("effort_forecast_records"),1)
        self.assertEqual(self.count("audit_events"),4)

    def test_audit_identity_and_cross_kind_operation_key(self):
        with self.db:
            budget(self.db)
        self.reject(lambda:forecast(self.db,ident=1))
        def wrong_actor():
            forecast(self.db,audit_id=audit(self.db,"RecordEffortForecast",actor=2))
        self.reject(wrong_actor)
        def wrong_action():
            forecast(self.db,audit_id=audit(self.db,"RecordEffortBudgetRevision"))
        self.reject(wrong_action)
        def wrong_project():
            forecast(self.db,audit_id=audit(self.db,"RecordEffortForecast",project=2))
        self.reject(wrong_project)
        self.reject(lambda:forecast(self.db,audit_id=1))
        self.assertEqual(self.count("audit_events"),1)

    def test_replace_and_mutation_denied(self):
        with self.db:
            budget(self.db)
            forecast(self.db)
        for sql in ("UPDATE effort_budget_revisions SET minutes=121",
                    "DELETE FROM effort_budget_revisions","UPDATE effort_forecast_records SET reason='changed'",
                    "DELETE FROM effort_forecast_records","UPDATE audit_events SET actor_id=2 WHERE id=1",
                    "DELETE FROM audit_events WHERE id=2",
                    "INSERT OR REPLACE INTO audit_events(id,actor_id,action,entity,entity_id) VALUES(1,2,'changed','Project',1)"):
            self.reject(lambda sql=sql:self.db.execute(sql))
        self.reject(lambda:budget(self.db,replace=True))
        self.reject(lambda:forecast(self.db,replace=True))
        self.assertEqual(self.count("audit_events"),2)

    def test_audit_failure_rolls_back_and_no_orphan_on_failed_append(self):
        self.db.execute("CREATE TRIGGER synthetic_audit_failure BEFORE INSERT ON audit_events BEGIN SELECT RAISE(ABORT,'synthetic'); END")
        self.reject(lambda:budget(self.db))
        self.assertEqual(self.count("effort_budget_revisions"),0)
        self.db.execute("DROP TRIGGER synthetic_audit_failure")
        with self.db:
            budget(self.db)
        self.reject(lambda:budget(self.db,2,HASH(1),0,ident=2))
        self.assertEqual(self.count("audit_events"),1)

    def test_concurrent_revision_only_one_commits(self):
        with self.db:
            budget(self.db)
        with tempfile.TemporaryDirectory() as folder:
            path=str(Path(folder)/"synthetic.db")
            with closing(sqlite3.connect(path)) as db:
                self.db.backup(db)
            barrier=threading.Barrier(2)
            def worker(ident):
                with closing(sqlite3.connect(path,timeout=10)) as db:
                    db.execute("PRAGMA foreign_keys=ON")
                    barrier.wait(timeout=10)
                    try:
                        with db:
                            db.execute("BEGIN IMMEDIATE")
                            budget(db,2,HASH(1),150+ident,ident=ident)
                        return "committed"
                    except sqlite3.IntegrityError:
                        return "rejected"
            with ThreadPoolExecutor(max_workers=2) as pool:
                self.assertCountEqual(list(pool.map(worker,[2,3])),["committed","rejected"])
            with closing(sqlite3.connect(path)) as db:
                self.assertEqual(db.execute("SELECT count(*) FROM effort_budget_revisions").fetchone()[0],2)
                self.assertEqual(db.execute("SELECT count(*) FROM audit_events").fetchone()[0],2)
