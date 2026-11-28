"""Synthetic reference contract only; not a native service or migration."""
import hashlib
import json
from dataclasses import dataclass


def fingerprint(db):
    if not db.in_transaction:
        raise ValueError('Fingerprint requires a consistent transaction')
    digest = hashlib.sha256(b'CRC-association-history-v1\n')
    for sql in (
        'SELECT id,batch_id,legacy_id,native_project_id,archive_hash,proposal_hash,review_note,actor_id,recorded_utc FROM historical_associations ORDER BY id',
        'SELECT association_id,review_note,actor_id,recorded_utc FROM historical_association_reversals ORDER BY association_id',
    ):
        digest.update(sql.encode('utf-8') + b'\n')
        for row in db.execute(sql):
            digest.update(json.dumps(list(row), ensure_ascii=True, separators=(',', ':')).encode('ascii') + b'\n')
    return digest.hexdigest()


@dataclass(frozen=True)
class Review:
    batch: int
    legacy: str
    native: int
    archive: str
    proposal: str
    history: str


def preview(db, batch, legacy, native):
    row = db.execute('''SELECT h.project_transfer_hash,s.snapshot_hash,s.snapshot
        FROM historical_project_register h JOIN projects p ON p.id=?
        JOIN engagements e ON e.id=p.engagement_id JOIN proposals s ON s.id=e.proposal_id
        WHERE h.batch_id=? AND h.legacy_id=? COLLATE BINARY AND h.native_client_id=s.client_id
        AND s.state='Accepted' ''', (native, batch, legacy)).fetchone()
    if row is None or not isinstance(row[2], str) or hashlib.sha256(row[2].encode('utf-8')).hexdigest() != row[1].lower():
        raise ValueError('Missing or inconsistent evidence')
    if db.execute('''SELECT 1 FROM historical_active_associations WHERE
        (batch_id=? AND legacy_id=? COLLATE NOCASE) OR native_project_id=?''', (batch, legacy, native)).fetchone():
        raise ValueError('Active association already exists')
    return Review(batch, legacy, native, row[0], row[1], fingerprint(db))


def _write(db, confirmed, note, actor, timestamp, operation):
    if confirmed is not True or not isinstance(note, str) or not 1 <= len(note.strip()) <= 1000:
        raise ValueError('Explicit review and reason required')
    if db.in_transaction:
        raise ValueError('Independent write transaction required')
    db.execute('BEGIN IMMEDIATE')
    try:
        if db.execute('PRAGMA foreign_keys').fetchone()[0] != 1:
            raise ValueError('Foreign keys required')
        result = operation(note.strip())
        db.commit()
        return result
    except BaseException:
        db.rollback()
        raise


def commit(db, review, confirmed, note, actor, timestamp):
    def operation(reason):
        if preview(db, review.batch, review.legacy, review.native) != review:
            raise ValueError('Stale review')
        cursor = db.execute('''INSERT INTO historical_associations(batch_id,legacy_id,native_project_id,
            archive_hash,proposal_hash,review_note,actor_id,recorded_utc) VALUES(?,?,?,?,?,?,?,?)''',
            (review.batch, review.legacy, review.native, review.archive, review.proposal, reason, actor, timestamp))
        ident = cursor.lastrowid
        db.execute('INSERT INTO prototype_association_audit VALUES(?,?,?,?,?)', ('Associate', ident, reason, actor, timestamp))
        return ident
    return _write(db, confirmed, note, actor, timestamp, operation)


def reverse(db, ident, expected_history, confirmed, note, actor, timestamp):
    def operation(reason):
        if fingerprint(db) != expected_history:
            raise ValueError('Stale reversal review')
        db.execute('INSERT INTO historical_association_reversals VALUES(?,?,?,?)', (ident, reason, actor, timestamp))
        db.execute('INSERT INTO prototype_association_audit VALUES(?,?,?,?,?)', ('Reverse', ident, reason, actor, timestamp))
    return _write(db, confirmed, note, actor, timestamp, operation)
