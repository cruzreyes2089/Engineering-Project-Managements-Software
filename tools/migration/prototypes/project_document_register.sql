-- INACTIVE structural candidate only. Do not apply to working databases.
-- No schema version or permission changes, filesystem access or transmission.
CREATE TABLE project_folder_revisions(
 project_id INTEGER NOT NULL REFERENCES projects(id),
 revision INTEGER NOT NULL CHECK(revision BETWEEN 1 AND 500),
 operation_key TEXT NOT NULL UNIQUE CHECK(length(operation_key)=32 AND operation_key NOT GLOB '*[^0-9a-f]*'),
 previous_hash TEXT,
 record_hash TEXT NOT NULL CHECK(length(record_hash)=64 AND record_hash NOT GLOB '*[^0-9a-f]*'),
 state TEXT NOT NULL CHECK(state IN ('Registered','Retired')),
 -- Canonical ASCII uppercase slash-separated full path key produced by the future service.
 path_key TEXT NOT NULL CHECK(length(path_key) BETWEEN 4 AND 512 AND path_key=upper(path_key)
   AND path_key NOT GLOB '*[^A-Z0-9 _./:-]*' AND substr(path_key,-1)!='/' AND instr(path_key,'//')=0),
 reason TEXT NOT NULL CHECK(length(trim(reason)) BETWEEN 1 AND 1000),
 actor_id INTEGER NOT NULL REFERENCES users(id),
 recorded_utc TEXT NOT NULL CHECK(length(recorded_utc) BETWEEN 1 AND 64),
 audit_id INTEGER NOT NULL UNIQUE REFERENCES audit_events(id),
 PRIMARY KEY(project_id,revision),
 UNIQUE(project_id,record_hash),
 FOREIGN KEY(project_id,previous_hash) REFERENCES project_folder_revisions(project_id,record_hash)
);
CREATE VIEW current_project_folder_references AS
 SELECT f.* FROM project_folder_revisions f WHERE f.state='Registered'
 AND NOT EXISTS(SELECT 1 FROM project_folder_revisions n WHERE n.project_id=f.project_id AND n.revision>f.revision);
CREATE TRIGGER folder_revision_guard BEFORE INSERT ON project_folder_revisions
BEGIN
 SELECT CASE WHEN EXISTS(SELECT 1 FROM project_folder_revisions WHERE operation_key=NEW.operation_key OR audit_id=NEW.audit_id OR
 (project_id=NEW.project_id AND (revision=NEW.revision OR record_hash=NEW.record_hash)))
 THEN RAISE(ABORT,'Duplicate folder identity') END;
 SELECT CASE WHEN NEW.revision!=1+coalesce((SELECT max(revision) FROM project_folder_revisions WHERE project_id=NEW.project_id),0)
 OR NEW.previous_hash IS NOT (SELECT record_hash FROM project_folder_revisions WHERE project_id=NEW.project_id ORDER BY revision DESC LIMIT 1)
 THEN RAISE(ABORT,'Stale folder predecessor') END;
 SELECT CASE WHEN NEW.state='Retired' AND NOT EXISTS(SELECT 1 FROM current_project_folder_references f WHERE f.project_id=NEW.project_id AND f.path_key=NEW.path_key)
 THEN RAISE(ABORT,'Current matching folder required to retire') END;
 SELECT CASE WHEN NEW.state='Registered' AND EXISTS(SELECT 1 FROM current_project_folder_references f WHERE f.project_id=NEW.project_id AND f.path_key=NEW.path_key)
 THEN RAISE(ABORT,'Unchanged folder reference') END;
 SELECT CASE WHEN NEW.state='Registered' AND EXISTS(SELECT 1 FROM current_project_folder_references f WHERE f.project_id!=NEW.project_id AND
 (f.path_key=NEW.path_key OR substr(f.path_key,1,length(NEW.path_key)+1)=NEW.path_key||'/' OR substr(NEW.path_key,1,length(f.path_key)+1)=f.path_key||'/'))
 THEN RAISE(ABORT,'Overlapping current project folder') END;
 SELECT CASE WHEN NOT EXISTS(SELECT 1 FROM audit_events a WHERE a.id=NEW.audit_id AND a.actor_id=NEW.actor_id AND
 a.action='RecordProjectFolderReference' AND a.entity='Project' AND a.entity_id=NEW.project_id)
 THEN RAISE(ABORT,'Folder audit mismatch') END;
END;
CREATE TABLE project_transmittal_intents(
 id INTEGER PRIMARY KEY,
 project_id INTEGER NOT NULL REFERENCES projects(id),
 operation_key TEXT NOT NULL UNIQUE CHECK(length(operation_key)=32 AND operation_key NOT GLOB '*[^0-9a-f]*'),
 intent_hash TEXT NOT NULL CHECK(length(intent_hash)=64 AND intent_hash NOT GLOB '*[^0-9a-f]*'),
 recipient TEXT NOT NULL CHECK(length(trim(recipient)) BETWEEN 1 AND 200),
 purpose TEXT NOT NULL CHECK(length(trim(purpose)) BETWEEN 1 AND 200),
 expected_documents INTEGER NOT NULL CHECK(expected_documents BETWEEN 1 AND 50),
 UNIQUE(project_id,intent_hash)
);
CREATE TABLE project_transmittal_documents(
 intent_id INTEGER NOT NULL REFERENCES project_transmittal_intents(id),
 document_id INTEGER NOT NULL REFERENCES project_documents(id),
 file_hash TEXT NOT NULL CHECK(length(file_hash)=64),
 PRIMARY KEY(intent_id,document_id)
);
CREATE TABLE project_transmittal_events(
 intent_id INTEGER NOT NULL REFERENCES project_transmittal_intents(id),
 sequence INTEGER NOT NULL CHECK(sequence BETWEEN 1 AND 4),
 operation_key TEXT NOT NULL UNIQUE CHECK(length(operation_key)=32 AND operation_key NOT GLOB '*[^0-9a-f]*'),
 previous_hash TEXT,
 event_hash TEXT NOT NULL CHECK(length(event_hash)=64 AND event_hash NOT GLOB '*[^0-9a-f]*'),
 state TEXT NOT NULL CHECK(state IN ('Prepared','IssueRecorded','DeliveryRecorded','Withdrawn')),
 evidence_ref TEXT NOT NULL CHECK(length(trim(evidence_ref)) BETWEEN 1 AND 200),
 note TEXT NOT NULL CHECK(length(trim(note)) BETWEEN 1 AND 1000),
 actor_id INTEGER NOT NULL REFERENCES users(id),
 recorded_utc TEXT NOT NULL CHECK(length(recorded_utc) BETWEEN 1 AND 64),
 audit_id INTEGER NOT NULL UNIQUE REFERENCES audit_events(id),
 PRIMARY KEY(intent_id,sequence),UNIQUE(intent_id,event_hash),
 FOREIGN KEY(intent_id,previous_hash) REFERENCES project_transmittal_events(intent_id,event_hash)
);
CREATE TRIGGER transmittal_intent_guard BEFORE INSERT ON project_transmittal_intents
WHEN EXISTS(SELECT 1 FROM project_transmittal_intents WHERE id=NEW.id OR operation_key=NEW.operation_key OR (project_id=NEW.project_id AND intent_hash=NEW.intent_hash))
BEGIN SELECT RAISE(ABORT,'Duplicate intent'); END;
CREATE TRIGGER transmittal_document_guard BEFORE INSERT ON project_transmittal_documents
BEGIN
 SELECT CASE WHEN EXISTS(SELECT 1 FROM project_transmittal_events WHERE intent_id=NEW.intent_id)
 OR EXISTS(SELECT 1 FROM project_transmittal_documents WHERE intent_id=NEW.intent_id AND document_id=NEW.document_id)
 OR (SELECT count(*) FROM project_transmittal_documents WHERE intent_id=NEW.intent_id)>=(SELECT expected_documents FROM project_transmittal_intents WHERE id=NEW.intent_id)
 THEN RAISE(ABORT,'Manifest sealed or duplicate') END;
 SELECT CASE WHEN NOT EXISTS(SELECT 1 FROM project_documents d JOIN project_transmittal_intents i ON i.project_id=d.project_id
 WHERE i.id=NEW.intent_id AND d.id=NEW.document_id AND d.file_hash=NEW.file_hash AND d.classification='Production'
 AND d.category IN ('Drawings','Calculations','Reports') AND lower(substr(d.file_name,-4))='.pdf'
 AND NOT EXISTS(SELECT 1 FROM project_documents n WHERE n.supersedes_id=d.id))
 THEN RAISE(ABORT,'Ineligible current document version') END;
END;
CREATE TRIGGER transmittal_event_guard BEFORE INSERT ON project_transmittal_events
BEGIN
 SELECT CASE WHEN EXISTS(SELECT 1 FROM project_transmittal_events WHERE operation_key=NEW.operation_key OR audit_id=NEW.audit_id
 OR (intent_id=NEW.intent_id AND (sequence=NEW.sequence OR event_hash=NEW.event_hash)))
 OR EXISTS(SELECT 1 FROM project_folder_revisions WHERE audit_id=NEW.audit_id)
 THEN RAISE(ABORT,'Duplicate event identity') END;
 SELECT CASE WHEN NEW.sequence!=1+coalesce((SELECT max(sequence) FROM project_transmittal_events WHERE intent_id=NEW.intent_id),0)
 OR NEW.previous_hash IS NOT (SELECT event_hash FROM project_transmittal_events WHERE intent_id=NEW.intent_id ORDER BY sequence DESC LIMIT 1)
 THEN RAISE(ABORT,'Stale event predecessor') END;
 SELECT CASE WHEN NOT(
 (NEW.sequence=1 AND NEW.state='Prepared') OR
 (NEW.sequence>1 AND EXISTS(SELECT 1 FROM project_transmittal_events e WHERE e.intent_id=NEW.intent_id AND e.sequence=NEW.sequence-1 AND
 ((e.state='Prepared' AND NEW.state IN ('IssueRecorded','Withdrawn')) OR
  (e.state='IssueRecorded' AND NEW.state IN ('DeliveryRecorded','Withdrawn')) OR
  (e.state='DeliveryRecorded' AND NEW.state='Withdrawn')))))
 THEN RAISE(ABORT,'Invalid issue or delivery transition') END;
 SELECT CASE WHEN (SELECT count(*) FROM project_transmittal_documents WHERE intent_id=NEW.intent_id)!=(SELECT expected_documents FROM project_transmittal_intents WHERE id=NEW.intent_id)
 THEN RAISE(ABORT,'Incomplete manifest') END;
 SELECT CASE WHEN NEW.state IN ('Prepared','IssueRecorded') AND EXISTS(
 SELECT 1 FROM project_transmittal_documents m JOIN project_documents d ON d.id=m.document_id
 JOIN project_transmittal_intents i ON i.id=m.intent_id WHERE m.intent_id=NEW.intent_id AND
 (d.project_id!=i.project_id OR d.file_hash!=m.file_hash OR d.classification!='Production' OR d.category NOT IN ('Drawings','Calculations','Reports')
 OR lower(substr(d.file_name,-4))!='.pdf' OR EXISTS(SELECT 1 FROM project_documents n WHERE n.supersedes_id=d.id)))
 THEN RAISE(ABORT,'Stale or ineligible issue manifest') END;
 SELECT CASE WHEN NOT EXISTS(SELECT 1 FROM audit_events a JOIN project_transmittal_intents i ON i.project_id=a.entity_id
 WHERE i.id=NEW.intent_id AND a.id=NEW.audit_id AND a.actor_id=NEW.actor_id AND a.entity='Project' AND a.action='RecordTransmittalEvent')
 THEN RAISE(ABORT,'Event audit mismatch') END;
END;
CREATE TRIGGER project_folder_revisions_no_update BEFORE UPDATE ON project_folder_revisions BEGIN SELECT RAISE(ABORT,'Register history immutable'); END;
CREATE TRIGGER project_folder_revisions_no_delete BEFORE DELETE ON project_folder_revisions BEGIN SELECT RAISE(ABORT,'Register history immutable'); END;
CREATE TRIGGER project_transmittal_intents_no_update BEFORE UPDATE ON project_transmittal_intents BEGIN SELECT RAISE(ABORT,'Register history immutable'); END;
CREATE TRIGGER project_transmittal_intents_no_delete BEFORE DELETE ON project_transmittal_intents BEGIN SELECT RAISE(ABORT,'Register history immutable'); END;
CREATE TRIGGER project_transmittal_documents_no_update BEFORE UPDATE ON project_transmittal_documents BEGIN SELECT RAISE(ABORT,'Register history immutable'); END;
CREATE TRIGGER project_transmittal_documents_no_delete BEFORE DELETE ON project_transmittal_documents BEGIN SELECT RAISE(ABORT,'Register history immutable'); END;
CREATE TRIGGER project_transmittal_events_no_update BEFORE UPDATE ON project_transmittal_events BEGIN SELECT RAISE(ABORT,'Register history immutable'); END;
CREATE TRIGGER project_transmittal_events_no_delete BEFORE DELETE ON project_transmittal_events BEGIN SELECT RAISE(ABORT,'Register history immutable'); END;
CREATE TRIGGER register_audit_no_update BEFORE UPDATE ON audit_events
WHEN EXISTS(SELECT 1 FROM project_folder_revisions WHERE audit_id=OLD.id) OR EXISTS(SELECT 1 FROM project_transmittal_events WHERE audit_id=OLD.id)
BEGIN SELECT RAISE(ABORT,'Register audit immutable'); END;
CREATE TRIGGER register_audit_no_delete BEFORE DELETE ON audit_events
WHEN EXISTS(SELECT 1 FROM project_folder_revisions WHERE audit_id=OLD.id) OR EXISTS(SELECT 1 FROM project_transmittal_events WHERE audit_id=OLD.id)
BEGIN SELECT RAISE(ABORT,'Register audit immutable'); END;
CREATE TRIGGER register_audit_no_replace BEFORE INSERT ON audit_events
WHEN EXISTS(SELECT 1 FROM project_folder_revisions WHERE audit_id=NEW.id) OR EXISTS(SELECT 1 FROM project_transmittal_events WHERE audit_id=NEW.id)
BEGIN SELECT RAISE(ABORT,'Register audit immutable'); END;
