"""Synthetic review journal prototype. Accepts a disposable SQLite connection only.

No installer, executable, live database, path handling or automatic recovery action.
A checkpoint is a consistency witness, not a signature or trusted authorization.
"""
from dataclasses import asdict, dataclass
import hashlib
import json
import sqlite3

import install_transaction_model as model

DOMAIN = b"CRC-synthetic-install-review-journal-v1\n"
SCHEMA = """CREATE TABLE synthetic_install_review(
    seq INTEGER PRIMARY KEY CHECK(seq BETWEEN 0 AND 4),
    phase TEXT NOT NULL,
    plan_hash TEXT NOT NULL,
    previous_hash TEXT NOT NULL,
    observation TEXT NOT NULL,
    entry_hash TEXT NOT NULL UNIQUE
)"""
GUARDS = (
    """CREATE TRIGGER synthetic_review_no_update BEFORE UPDATE ON synthetic_install_review
       BEGIN SELECT RAISE(ABORT,'Review journal append only'); END""",
    """CREATE TRIGGER synthetic_review_no_delete BEFORE DELETE ON synthetic_install_review
       BEGIN SELECT RAISE(ABORT,'Review journal append only'); END""",
    """CREATE TRIGGER synthetic_review_no_replace BEFORE INSERT ON synthetic_install_review
       WHEN EXISTS(SELECT 1 FROM synthetic_install_review WHERE seq=NEW.seq)
       BEGIN SELECT RAISE(ABORT,'Review journal identity already exists'); END""",
)
PHASES = tuple(model.Phase)
LIMIT = 8192

@dataclass(frozen=True)
class Checkpoint:
    sequence: int
    phase: model.Phase
    sha256: str

def canonical(value):
    return json.dumps(value, sort_keys=True, separators=(",", ":"), ensure_ascii=True,
                      allow_nan=False)

def plan_hash(plan):
    if not isinstance(plan, model.Plan):
        raise ValueError("Exact synthetic plan required")
    return hashlib.sha256(DOMAIN + canonical(asdict(plan)).encode("ascii")).hexdigest()

def entry_hash(seq, phase, plan, previous, observation):
    return hashlib.sha256(DOMAIN + canonical([seq, phase, plan, previous, observation]).encode("ascii")).hexdigest()

def require(condition, message):
    if not condition:
        raise ValueError(message)

def decode_observation(raw):
    require(isinstance(raw, str) and len(raw) <= LIMIT, "Invalid observation size")
    values = json.loads(raw)
    require(isinstance(values, dict) and set(values) == set(model.Observation.__dataclass_fields__),
            "Invalid observation shape")
    require(canonical(values) == raw, "Noncanonical observation")
    for key in ("active", "staged"):
        if values[key] is not None:
            require(isinstance(values[key], dict), "Invalid version observation")
            try:
                values[key] = model.Version(**values[key])
            except TypeError:
                raise ValueError("Invalid version observation") from None
    for key in ("data_sha256", "recovery_sha256"):
        require(values[key] is None or model.digest(values[key]), "Invalid observed digest")
    require(values["data_schema"] is None or
            (type(values["data_schema"]) is int and 1 <= values["data_schema"] <= 10000),
            "Invalid observed schema")
    return model.Observation(**values)

def begin(db):
    require(not db.in_transaction, "Independent journal transaction required")
    db.execute("BEGIN IMMEDIATE")

def initialize(db, plan):
    """Only a new empty disposable journal; no reuse or deletion of existing tables."""
    ph = plan_hash(plan)
    begin(db)
    try:
        require(db.execute("SELECT count(*) FROM sqlite_schema WHERE name NOT LIKE 'sqlite_%'").fetchone()[0] == 0,
                "New empty synthetic database required")
        db.execute(SCHEMA)
        for guard in GUARDS:
            db.execute(guard)
        h = entry_hash(0, PHASES[0].value, ph, "", "")
        db.execute("INSERT INTO synthetic_install_review VALUES(?,?,?,?,?,?)",
                   (0, PHASES[0].value, ph, "", "", h))
        db.commit()
        return Checkpoint(0, PHASES[0], h)
    except BaseException:
        db.rollback()
        raise

def read_chain(db, plan):
    ph = plan_hash(plan)
    rows = db.execute("SELECT seq,phase,plan_hash,previous_hash,observation,entry_hash "
                      "FROM synthetic_install_review ORDER BY seq LIMIT 6").fetchall()
    require(1 <= len(rows) <= len(PHASES), "Incomplete or excessive journal")
    previous = ""
    for i, (seq, phase, stored_plan, prior, raw, h) in enumerate(rows):
        require(seq == i and phase == PHASES[i].value and stored_plan == ph and prior == previous,
                "Journal sequence or plan mismatch")
        require(isinstance(raw, str) and len(raw) <= LIMIT and
                h == entry_hash(seq, phase, ph, prior, raw), "Journal entry hash mismatch")
        if i == 0:
            require(raw == "", "Unexpected genesis observation")
        else:
            obs = decode_observation(raw)
            require(model.assess(plan, PHASES[i-1], obs).next_phase is PHASES[i],
                    "Journal contains invalid transition")
        previous = h
    return Checkpoint(len(rows)-1, PHASES[len(rows)-1], previous)

def reconcile(db, plan, expected):
    """Read-only reconciliation against the separately retained checkpoint.

    Both a truncated chain and a journal advanced past an externally retained
    checkpoint stop; the latter needs manual review of a lost receipt.
    """
    require(isinstance(expected, Checkpoint), "Independent checkpoint required")
    actual = read_chain(db, plan)
    require(actual == expected, "Journal checkpoint mismatch; manual review required")
    return actual

def append_review(db, plan, expected, observation):
    """Persist one eligible review transition, not a filesystem switch."""
    require(isinstance(observation, model.Observation), "Synthetic observation required")
    raw = canonical(asdict(observation))
    obs = decode_observation(raw)
    begin(db)
    try:
        prior = reconcile(db, plan, expected)
        result = model.assess(plan, prior.phase, obs)
        require(prior.sequence+1 < len(PHASES) and result.next_phase is PHASES[prior.sequence+1],
                "No eligible next review transition")
        seq = prior.sequence+1
        phase = result.next_phase
        ph = plan_hash(plan)
        h = entry_hash(seq, phase.value, ph, prior.sha256, raw)
        db.execute("INSERT INTO synthetic_install_review VALUES(?,?,?,?,?,?)",
                   (seq, phase.value, ph, prior.sha256, raw, h))
        candidate = Checkpoint(seq, phase, h)
        reconcile(db, plan, candidate)
        db.commit()
        return candidate
    except BaseException:
        db.rollback()
        raise
