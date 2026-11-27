-- PROTOTYPE ONLY. Not included in LocalPractice migrations; use disposable test databases.
-- Caller must enable foreign_keys and use one immediate transaction for batch, rows, seal and audit.
CREATE TABLE historical_project_batches(
 id INTEGER PRIMARY KEY CHECK(id=1),
 crm_batch_id INTEGER NOT NULL REFERENCES legacy_crm_batches(id),
 crm_transfer_hash TEXT NOT NULL CHECK(length(crm_transfer_hash)=64 AND crm_transfer_hash NOT GLOB '*[^0-9a-f]*'),
 source_hash TEXT NOT NULL CHECK(length(source_hash)=64 AND source_hash NOT GLOB '*[^0-9a-f]*'),
 project_transfer_hash TEXT NOT NULL CHECK(length(project_transfer_hash)=64 AND project_transfer_hash NOT GLOB '*[^0-9a-f]*'),
 census_hash TEXT NOT NULL CHECK(length(census_hash)=64 AND census_hash NOT GLOB '*[^0-9a-f]*'),
 expected_count INTEGER NOT NULL CHECK(expected_count BETWEEN 1 AND 10000),
 review_note TEXT NOT NULL CHECK(length(trim(review_note)) BETWEEN 1 AND 1000),
 imported_by INTEGER NOT NULL REFERENCES users(id),
 imported_utc TEXT NOT NULL CHECK(length(imported_utc)>0)
);
CREATE TRIGGER historical_batch_crm_guard BEFORE INSERT ON historical_project_batches
WHEN EXISTS(SELECT 1 FROM historical_project_batches WHERE id=NEW.id)
 OR NOT EXISTS(SELECT 1 FROM legacy_crm_batches c WHERE c.id=NEW.crm_batch_id
 AND lower(c.source_hash)=NEW.crm_transfer_hash)
BEGIN SELECT RAISE(ABORT,'Historical batch CRM provenance mismatch'); END;
CREATE TABLE historical_project_rows(
 batch_id INTEGER NOT NULL REFERENCES historical_project_batches(id),
 legacy_id TEXT NOT NULL COLLATE NOCASE CHECK(length(legacy_id) BETWEEN 1 AND 100 AND legacy_id NOT GLOB '*[^A-Za-z0-9._-]*'),
 legacy_client_id TEXT NOT NULL COLLATE NOCASE REFERENCES legacy_client_map(legacy_id)
 CHECK(length(legacy_client_id) BETWEEN 1 AND 100 AND legacy_client_id NOT GLOB '*[^A-Za-z0-9._-]*'),
 legacy_number TEXT NOT NULL CHECK(length(legacy_number) BETWEEN 1 AND 100 AND length(trim(legacy_number))>0),
 title TEXT NOT NULL CHECK(length(title) BETWEEN 1 AND 200 AND length(trim(title))>0),
 site TEXT NOT NULL CHECK(length(site)<=300),
 legacy_status TEXT NOT NULL CHECK(length(legacy_status) BETWEEN 1 AND 100 AND length(trim(legacy_status))>0),
 legacy_authorization_status TEXT NOT NULL CHECK(length(legacy_authorization_status) BETWEEN 1 AND 100 AND length(trim(legacy_authorization_status))>0),
 authorization_evidence_ref TEXT NOT NULL CHECK(length(authorization_evidence_ref)<=200),
 PRIMARY KEY(batch_id,legacy_id)
);
CREATE TABLE historical_project_seals(
 batch_id INTEGER PRIMARY KEY REFERENCES historical_project_batches(id),
 sealed_utc TEXT NOT NULL CHECK(length(sealed_utc)>0)
);
CREATE TRIGGER historical_row_guard BEFORE INSERT ON historical_project_rows
WHEN EXISTS(SELECT 1 FROM historical_project_seals s WHERE s.batch_id=NEW.batch_id)
 OR EXISTS(SELECT 1 FROM historical_project_rows r WHERE r.batch_id=NEW.batch_id AND r.legacy_id=NEW.legacy_id COLLATE NOCASE)
 OR NOT EXISTS(SELECT 1 FROM historical_project_batches b JOIN legacy_client_map m ON m.batch_id=b.crm_batch_id
 WHERE b.id=NEW.batch_id AND m.legacy_id=NEW.legacy_client_id COLLATE NOCASE)
 OR (SELECT count(*) FROM historical_project_rows WHERE batch_id=NEW.batch_id)>=
 (SELECT expected_count FROM historical_project_batches WHERE id=NEW.batch_id)
BEGIN SELECT RAISE(ABORT,'Historical row outside open reviewed batch'); END;
CREATE TRIGGER historical_seal_guard BEFORE INSERT ON historical_project_seals
WHEN EXISTS(SELECT 1 FROM historical_project_seals WHERE batch_id=NEW.batch_id)
 OR NOT EXISTS(SELECT 1 FROM historical_project_batches b WHERE b.id=NEW.batch_id
 AND b.expected_count=(SELECT count(*) FROM historical_project_rows r WHERE r.batch_id=b.id))
BEGIN SELECT RAISE(ABORT,'Historical archive count mismatch'); END;
CREATE TRIGGER historical_batches_no_update BEFORE UPDATE ON historical_project_batches BEGIN SELECT RAISE(ABORT,'Historical batch immutable'); END;
CREATE TRIGGER historical_batches_no_delete BEFORE DELETE ON historical_project_batches BEGIN SELECT RAISE(ABORT,'Historical batch immutable'); END;
CREATE TRIGGER historical_rows_no_update BEFORE UPDATE ON historical_project_rows BEGIN SELECT RAISE(ABORT,'Historical row immutable'); END;
CREATE TRIGGER historical_rows_no_delete BEFORE DELETE ON historical_project_rows BEGIN SELECT RAISE(ABORT,'Historical row immutable'); END;
CREATE TRIGGER historical_seals_no_update BEFORE UPDATE ON historical_project_seals BEGIN SELECT RAISE(ABORT,'Historical seal immutable'); END;
CREATE TRIGGER historical_seals_no_delete BEFORE DELETE ON historical_project_seals BEGIN SELECT RAISE(ABORT,'Historical seal immutable'); END;
CREATE VIEW historical_project_register AS
SELECT r.batch_id,r.legacy_id,r.legacy_client_id,m.client_id AS native_client_id,
 r.legacy_number,r.title,r.site,r.legacy_status,r.legacy_authorization_status,r.authorization_evidence_ref,
 b.source_hash,b.project_transfer_hash,b.census_hash,0 AS work_authorized
FROM historical_project_rows r JOIN historical_project_seals s ON s.batch_id=r.batch_id
JOIN historical_project_batches b ON b.id=r.batch_id
JOIN legacy_client_map m ON m.batch_id=b.crm_batch_id AND m.legacy_id=r.legacy_client_id COLLATE NOCASE;
