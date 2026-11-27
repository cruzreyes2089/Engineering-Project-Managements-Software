-- INACTIVE persistence candidate. Never apply to a working schema-47 database.
-- No user_version, grant changes, task updates, accounting writes or production activation.
CREATE TABLE effort_budget_revisions(
 project_id INTEGER NOT NULL REFERENCES projects(id),
 revision INTEGER NOT NULL CHECK(revision BETWEEN 1 AND 500),
 operation_key TEXT NOT NULL UNIQUE CHECK(length(operation_key)=32 AND operation_key NOT GLOB '*[^0-9a-f]*'),
 previous_hash TEXT,
 record_hash TEXT NOT NULL CHECK(length(record_hash)=64 AND record_hash NOT GLOB '*[^0-9a-f]*'),
 minutes INTEGER NOT NULL CHECK(typeof(minutes)='integer' AND minutes BETWEEN 1 AND 300000000),
 kind TEXT NOT NULL CHECK(kind IN ('FirstRecorded','ScopeReview','EstimateReview','ManagementReview')),
 reason TEXT NOT NULL CHECK(length(trim(reason)) BETWEEN 1 AND 1000),
 snapshot_hash TEXT NOT NULL CHECK(length(snapshot_hash)=64 AND snapshot_hash NOT GLOB '*[^0-9a-f]*'),
 snapshot_bytes BLOB NOT NULL CHECK(typeof(snapshot_bytes)='blob' AND length(snapshot_bytes) BETWEEN 1 AND 2097152),
 actor_id INTEGER NOT NULL REFERENCES users(id),
 recorded_utc TEXT NOT NULL CHECK(length(recorded_utc) BETWEEN 1 AND 64),
 audit_id INTEGER NOT NULL UNIQUE REFERENCES audit_events(id),
 PRIMARY KEY(project_id,revision),
 UNIQUE(project_id,record_hash),
 FOREIGN KEY(project_id,previous_hash) REFERENCES effort_budget_revisions(project_id,record_hash),
 CHECK((revision=1 AND previous_hash IS NULL AND kind='FirstRecorded') OR
       (revision>1 AND previous_hash IS NOT NULL AND kind!='FirstRecorded'))
);
CREATE TABLE effort_forecast_records(
 project_id INTEGER NOT NULL REFERENCES projects(id),
 sequence INTEGER NOT NULL CHECK(sequence BETWEEN 1 AND 500),
 operation_key TEXT NOT NULL UNIQUE CHECK(length(operation_key)=32 AND operation_key NOT GLOB '*[^0-9a-f]*'),
 previous_hash TEXT,
 record_hash TEXT NOT NULL CHECK(length(record_hash)=64 AND record_hash NOT GLOB '*[^0-9a-f]*'),
 budget_hash TEXT NOT NULL CHECK(length(budget_hash)=64 AND budget_hash NOT GLOB '*[^0-9a-f]*'),
 snapshot_hash TEXT NOT NULL CHECK(length(snapshot_hash)=64 AND snapshot_hash NOT GLOB '*[^0-9a-f]*'),
 snapshot_bytes BLOB NOT NULL CHECK(typeof(snapshot_bytes)='blob' AND length(snapshot_bytes) BETWEEN 1 AND 2097152),
 remaining_bytes BLOB NOT NULL CHECK(typeof(remaining_bytes)='blob' AND length(remaining_bytes) BETWEEN 1 AND 65536),
 first_budget_minutes INTEGER NOT NULL CHECK(typeof(first_budget_minutes)='integer' AND first_budget_minutes BETWEEN 1 AND 300000000),
 current_budget_minutes INTEGER NOT NULL CHECK(typeof(current_budget_minutes)='integer' AND current_budget_minutes BETWEEN 1 AND 300000000),
 approved_minutes INTEGER NOT NULL CHECK(typeof(approved_minutes)='integer' AND approved_minutes BETWEEN 0 AND 3000000000),
 pending_minutes INTEGER NOT NULL CHECK(typeof(pending_minutes)='integer' AND pending_minutes BETWEEN 0 AND 3000000000),
 remaining_minutes INTEGER NOT NULL CHECK(typeof(remaining_minutes)='integer' AND remaining_minutes BETWEEN 0 AND 300000000),
 total_minutes INTEGER NOT NULL CHECK(typeof(total_minutes)='integer' AND total_minutes=approved_minutes+pending_minutes+remaining_minutes),
 reason TEXT NOT NULL CHECK(length(trim(reason)) BETWEEN 1 AND 1000),
 actor_id INTEGER NOT NULL REFERENCES users(id),
 recorded_utc TEXT NOT NULL CHECK(length(recorded_utc) BETWEEN 1 AND 64),
 audit_id INTEGER NOT NULL UNIQUE REFERENCES audit_events(id),
 PRIMARY KEY(project_id,sequence),
 UNIQUE(project_id,record_hash),
 FOREIGN KEY(project_id,budget_hash) REFERENCES effort_budget_revisions(project_id,record_hash),
 FOREIGN KEY(project_id,previous_hash) REFERENCES effort_forecast_records(project_id,record_hash),
 CHECK((sequence=1 AND previous_hash IS NULL) OR (sequence>1 AND previous_hash IS NOT NULL))
);
CREATE TRIGGER effort_budget_insert_guard BEFORE INSERT ON effort_budget_revisions
WHEN EXISTS(SELECT 1 FROM effort_budget_revisions WHERE operation_key=NEW.operation_key OR audit_id=NEW.audit_id
 OR (project_id=NEW.project_id AND (revision=NEW.revision OR record_hash=NEW.record_hash)))
 OR EXISTS(SELECT 1 FROM effort_forecast_records WHERE operation_key=NEW.operation_key OR audit_id=NEW.audit_id)
 OR NEW.revision!=1+coalesce((SELECT max(revision) FROM effort_budget_revisions WHERE project_id=NEW.project_id),0)
 OR NEW.previous_hash IS NOT (SELECT record_hash FROM effort_budget_revisions WHERE project_id=NEW.project_id ORDER BY revision DESC LIMIT 1)
 OR NEW.minutes=(SELECT minutes FROM effort_budget_revisions WHERE project_id=NEW.project_id ORDER BY revision DESC LIMIT 1)
 OR NOT EXISTS(SELECT 1 FROM audit_events a WHERE a.id=NEW.audit_id AND a.actor_id=NEW.actor_id
 AND a.action='RecordEffortBudgetRevision' AND a.entity='Project' AND a.entity_id=NEW.project_id)
BEGIN SELECT RAISE(ABORT,'Invalid budget revision chain or audit'); END;
CREATE TRIGGER effort_forecast_insert_guard BEFORE INSERT ON effort_forecast_records
WHEN EXISTS(SELECT 1 FROM effort_forecast_records WHERE operation_key=NEW.operation_key OR audit_id=NEW.audit_id
 OR (project_id=NEW.project_id AND (sequence=NEW.sequence OR record_hash=NEW.record_hash)))
 OR EXISTS(SELECT 1 FROM effort_budget_revisions WHERE operation_key=NEW.operation_key OR audit_id=NEW.audit_id)
 OR NEW.sequence!=1+coalesce((SELECT max(sequence) FROM effort_forecast_records WHERE project_id=NEW.project_id),0)
 OR NEW.previous_hash IS NOT (SELECT record_hash FROM effort_forecast_records WHERE project_id=NEW.project_id ORDER BY sequence DESC LIMIT 1)
 OR NEW.budget_hash IS NOT (SELECT record_hash FROM effort_budget_revisions WHERE project_id=NEW.project_id ORDER BY revision DESC LIMIT 1)
 OR NEW.current_budget_minutes IS NOT (SELECT minutes FROM effort_budget_revisions WHERE project_id=NEW.project_id ORDER BY revision DESC LIMIT 1)
 OR NEW.first_budget_minutes IS NOT (SELECT minutes FROM effort_budget_revisions WHERE project_id=NEW.project_id AND revision=1)
 OR NOT EXISTS(SELECT 1 FROM audit_events a WHERE a.id=NEW.audit_id AND a.actor_id=NEW.actor_id
 AND a.action='RecordEffortForecast' AND a.entity='Project' AND a.entity_id=NEW.project_id)
BEGIN SELECT RAISE(ABORT,'Invalid forecast chain, budget or audit'); END;
CREATE TRIGGER effort_budget_no_update BEFORE UPDATE ON effort_budget_revisions BEGIN SELECT RAISE(ABORT,'Budget revision immutable'); END;
CREATE TRIGGER effort_budget_no_delete BEFORE DELETE ON effort_budget_revisions BEGIN SELECT RAISE(ABORT,'Budget revision immutable'); END;
CREATE TRIGGER effort_forecast_no_update BEFORE UPDATE ON effort_forecast_records BEGIN SELECT RAISE(ABORT,'Forecast record immutable'); END;
CREATE TRIGGER effort_forecast_no_delete BEFORE DELETE ON effort_forecast_records BEGIN SELECT RAISE(ABORT,'Forecast record immutable'); END;
CREATE TRIGGER effort_linked_audit_no_update BEFORE UPDATE ON audit_events
WHEN EXISTS(SELECT 1 FROM effort_budget_revisions WHERE audit_id=OLD.id)
 OR EXISTS(SELECT 1 FROM effort_forecast_records WHERE audit_id=OLD.id)
BEGIN SELECT RAISE(ABORT,'Linked effort audit immutable'); END;
CREATE TRIGGER effort_linked_audit_no_delete BEFORE DELETE ON audit_events
WHEN EXISTS(SELECT 1 FROM effort_budget_revisions WHERE audit_id=OLD.id)
 OR EXISTS(SELECT 1 FROM effort_forecast_records WHERE audit_id=OLD.id)
BEGIN SELECT RAISE(ABORT,'Linked effort audit immutable'); END;
CREATE TRIGGER effort_linked_audit_no_replace BEFORE INSERT ON audit_events
WHEN EXISTS(SELECT 1 FROM effort_budget_revisions WHERE audit_id=NEW.id)
 OR EXISTS(SELECT 1 FROM effort_forecast_records WHERE audit_id=NEW.id)
BEGIN SELECT RAISE(ABORT,'Linked effort audit immutable'); END;
