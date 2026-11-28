-- Inactive firm-wide folder configuration candidate. No schema activation.
CREATE TABLE project_folder_configurations(
 revision INTEGER PRIMARY KEY CHECK(revision BETWEEN 1 AND 500),
 operation_key TEXT NOT NULL UNIQUE CHECK(length(operation_key)=32 AND operation_key NOT GLOB '*[^0-9a-f]*'),
 previous_hash TEXT REFERENCES project_folder_configurations(record_hash),
 record_hash TEXT NOT NULL UNIQUE CHECK(length(record_hash)=64 AND record_hash NOT GLOB '*[^0-9a-f]*'),
 root_path TEXT NOT NULL CHECK(length(root_path) BETWEEN 4 AND 512),
 template_json TEXT NOT NULL CHECK(length(template_json) BETWEEN 2 AND 16384),
 reason TEXT NOT NULL CHECK(length(trim(reason)) BETWEEN 1 AND 1000),
 actor_id INTEGER NOT NULL REFERENCES users(id),
 recorded_utc TEXT NOT NULL CHECK(length(recorded_utc) BETWEEN 1 AND 64),
 audit_id INTEGER NOT NULL UNIQUE REFERENCES audit_events(id)
);
CREATE TRIGGER project_folder_config_guard BEFORE INSERT ON project_folder_configurations
BEGIN
 SELECT CASE WHEN EXISTS(SELECT 1 FROM project_folder_configurations WHERE revision=NEW.revision OR operation_key=NEW.operation_key OR record_hash=NEW.record_hash OR audit_id=NEW.audit_id)
 THEN RAISE(ABORT,'Duplicate folder configuration identity') END;
 SELECT CASE WHEN NEW.revision!=1+coalesce((SELECT max(revision) FROM project_folder_configurations),0)
 OR NEW.previous_hash IS NOT (SELECT record_hash FROM project_folder_configurations ORDER BY revision DESC LIMIT 1)
 THEN RAISE(ABORT,'Stale folder configuration') END;
 SELECT CASE WHEN EXISTS(SELECT 1 FROM project_folder_configurations WHERE revision=NEW.revision-1 AND root_path=NEW.root_path AND template_json=NEW.template_json)
 THEN RAISE(ABORT,'Unchanged folder configuration') END;
 SELECT CASE WHEN NOT EXISTS(SELECT 1 FROM audit_events a WHERE a.id=NEW.audit_id AND a.actor_id=NEW.actor_id AND a.action='RecordFolderConfiguration' AND a.entity='Firm' AND a.entity_id=1)
 THEN RAISE(ABORT,'Folder configuration audit mismatch') END;
END;
CREATE TRIGGER project_folder_config_no_update BEFORE UPDATE ON project_folder_configurations BEGIN SELECT RAISE(ABORT,'Folder configuration immutable'); END;
CREATE TRIGGER project_folder_config_no_delete BEFORE DELETE ON project_folder_configurations BEGIN SELECT RAISE(ABORT,'Folder configuration immutable'); END;
CREATE TRIGGER folder_config_audit_no_update BEFORE UPDATE ON audit_events WHEN EXISTS(SELECT 1 FROM project_folder_configurations WHERE audit_id=OLD.id) BEGIN SELECT RAISE(ABORT,'Folder configuration audit immutable'); END;
CREATE TRIGGER folder_config_audit_no_delete BEFORE DELETE ON audit_events WHEN EXISTS(SELECT 1 FROM project_folder_configurations WHERE audit_id=OLD.id) BEGIN SELECT RAISE(ABORT,'Folder configuration audit immutable'); END;
CREATE TRIGGER folder_config_audit_no_replace BEFORE INSERT ON audit_events WHEN EXISTS(SELECT 1 FROM project_folder_configurations WHERE audit_id=NEW.id) BEGIN SELECT RAISE(ABORT,'Folder configuration audit immutable'); END;
