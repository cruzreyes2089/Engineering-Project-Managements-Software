-- Candidate prototype only; not a LocalPractice migration.
-- Use foreign_keys=ON and one immediate transaction for each event and its audit.
CREATE TABLE historical_associations(
 id INTEGER PRIMARY KEY CHECK(id>0),
 batch_id INTEGER NOT NULL REFERENCES historical_project_seals(batch_id),
 legacy_id TEXT NOT NULL COLLATE NOCASE,
 native_project_id INTEGER NOT NULL REFERENCES projects(id),
 archive_hash TEXT NOT NULL CHECK(length(archive_hash)=64),
 proposal_hash TEXT NOT NULL CHECK(length(proposal_hash)=64),
 review_note TEXT NOT NULL CHECK(length(trim(review_note)) BETWEEN 1 AND 1000),
 actor_id INTEGER NOT NULL REFERENCES users(id),
 recorded_utc TEXT NOT NULL CHECK(length(recorded_utc)>0),
 FOREIGN KEY(batch_id,legacy_id) REFERENCES historical_project_rows(batch_id,legacy_id)
);
CREATE TABLE historical_association_reversals(
 association_id INTEGER PRIMARY KEY REFERENCES historical_associations(id),
 review_note TEXT NOT NULL CHECK(length(trim(review_note)) BETWEEN 1 AND 1000),
 actor_id INTEGER NOT NULL REFERENCES users(id),
 recorded_utc TEXT NOT NULL CHECK(length(recorded_utc)>0)
);
CREATE VIEW historical_active_associations AS
 SELECT a.*,0 AS work_authorized FROM historical_associations a
 WHERE NOT EXISTS(SELECT 1 FROM historical_association_reversals r WHERE r.association_id=a.id);
CREATE TRIGGER historical_association_insert_guard BEFORE INSERT ON historical_associations
WHEN EXISTS(SELECT 1 FROM historical_associations WHERE id=NEW.id)
 OR EXISTS(SELECT 1 FROM historical_active_associations a WHERE
 (a.batch_id=NEW.batch_id AND a.legacy_id=NEW.legacy_id COLLATE NOCASE) OR a.native_project_id=NEW.native_project_id)
 OR NOT EXISTS(SELECT 1 FROM historical_project_register h
 JOIN projects p ON p.id=NEW.native_project_id
 JOIN engagements e ON e.id=p.engagement_id
 JOIN proposals s ON s.id=e.proposal_id
 WHERE h.batch_id=NEW.batch_id AND h.legacy_id=NEW.legacy_id COLLATE BINARY
 AND h.native_client_id=s.client_id AND h.project_transfer_hash=NEW.archive_hash
 AND s.state='Accepted' AND s.snapshot_hash=NEW.proposal_hash)
BEGIN SELECT RAISE(ABORT,'Historical association identity or evidence mismatch'); END;
CREATE TRIGGER historical_association_reverse_guard BEFORE INSERT ON historical_association_reversals
WHEN NOT EXISTS(SELECT 1 FROM historical_active_associations WHERE id=NEW.association_id)
BEGIN SELECT RAISE(ABORT,'Current association required'); END;
CREATE TRIGGER historical_association_no_update BEFORE UPDATE ON historical_associations BEGIN SELECT RAISE(ABORT,'Association immutable'); END;
CREATE TRIGGER historical_association_no_delete BEFORE DELETE ON historical_associations BEGIN SELECT RAISE(ABORT,'Association immutable'); END;
CREATE TRIGGER historical_reversal_no_update BEFORE UPDATE ON historical_association_reversals BEGIN SELECT RAISE(ABORT,'Reversal immutable'); END;
CREATE TRIGGER historical_reversal_no_delete BEFORE DELETE ON historical_association_reversals BEGIN SELECT RAISE(ABORT,'Reversal immutable'); END;
