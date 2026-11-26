using Microsoft.Data.Sqlite;
using PracticeManager.Core;
namespace PracticeManager.Persistence;
// Local development edition. No caller-supplied user identity or role; recheck active grants in every transaction.
public sealed partial class LocalPractice
{
 readonly string connectionString; long? userId; DateTimeOffset expires; long sessionGeneration; long sessionCredentialRevision;
 static readonly string[] Grants=["Clients.View","Clients.Edit","Contacts.View","Contacts.Edit","Accounting.Connection.Test","Accounting.Connection.Manage","Accounting.Customers.Map","Accounting.Identity.Manage","Accounting.Directory.Sync","Accounting.Posting.Rehearse","Accounting.Posting.RehearseApprove","Accounting.WireDraft.Prepare","Accounting.WireDraft.Approve","Accounting.WireDraft.Rehearse","Engagements.View","Engagements.Edit","Engagements.Issue","Engagements.Accept","Projects.View","Agreements.Record","Agreements.View","Projects.Authorize","Projects.Setup","Administration.Manage","Employees.View","Employees.Edit","Projects.Plan","Work.Own","Work.Review","Billing.Classify","Documents.View","Documents.Record","Documents.Restricted","QA.View","QA.Edit","QA.Close","Scope.Authorize","Billing.Rates","Billing.Invoices.View","Billing.Invoices.Edit","Billing.Invoices.Approve","Billing.Invoices.Export","Reports.Operations","Reports.Financial","Administration.RecoveryCopy","Compliance.View","Compliance.Record"];
 public LocalPractice(string path){path=Path.GetFullPath(path);Directory.CreateDirectory(Path.GetDirectoryName(path)!);connectionString=new SqliteConnectionStringBuilder{DataSource=path,ForeignKeys=true,DefaultTimeout=10,Pooling=false}.ToString();using var c=Open();using var t=c.BeginTransaction(deferred:false);var version=Convert.ToInt64(Scalar(c,t,"PRAGMA user_version"));if(version==0){if(Convert.ToInt64(Scalar(c,t,"SELECT count(*) FROM sqlite_master WHERE name NOT LIKE 'sqlite_%'"))!=0)throw new InvalidOperationException("Unknown database; refusing initialization.");using var stream=typeof(LocalPractice).Assembly.GetManifestResourceStream("PracticeManager.schema.sql")!;using var reader=new StreamReader(stream);Exec(c,t,reader.ReadToEnd());version=1;}if(version==1){UpgradeToV2(c,t);version=2;}if(version==2){UpgradeToV3(c,t);version=3;}if(version==3){UpgradeToV4(c,t);version=4;}if(version==4){UpgradeToV5(c,t);version=5;}if(version==5){UpgradeToV6(c,t);version=6;}if(version==6){UpgradeToV7(c,t);version=7;}if(version==7){UpgradeToV8(c,t);version=8;}if(version==8){UpgradeToV9(c,t);version=9;}if(version==9){UpgradeToV10(c,t);version=10;}if(version==10){UpgradeToV11(c,t);version=11;}if(version==11){UpgradeToV12(c,t);version=12;}if(version==12){UpgradeToV13(c,t);version=13;}if(version==13){UpgradeToV14(c,t);version=14;}if(version==14){UpgradeToV15(c,t);version=15;}if(version==15){UpgradeToV16(c,t);version=16;}if(version==16){UpgradeToV17(c,t);version=17;}if(version==17){UpgradeToV18(c,t);version=18;}if(version==18){UpgradeToV19(c,t);version=19;}if(version==19){UpgradeToV20(c,t);version=20;}if(version==20){UpgradeToV21(c,t);version=21;}if(version==21){UpgradeToV22(c,t);version=22;}if(version==22){UpgradeToV23(c,t);version=23;}if(version==23){UpgradeToV24(c,t);version=24;}if(version!=24)throw new InvalidOperationException("Unsupported schema version.");t.Commit();}
 static void UpgradeToV2(SqliteConnection c,SqliteTransaction t)
 {
  foreach(var grant in new[]{"Accounting.Connection.Test","Accounting.Connection.Manage"})
  {
   Exec(c,t,"INSERT OR IGNORE INTO permissions(code) VALUES($p)",("$p",grant));
   Exec(c,t,"INSERT OR IGNORE INTO role_permissions(role_id,permission_code,allowed) SELECT id,$p,1 FROM roles WHERE name='Administrator'",("$p",grant));
  }
  Exec(c,t,"PRAGMA user_version=2");
 }
 static void UpgradeToV3(SqliteConnection c,SqliteTransaction t)
 {
  Exec(c,t,"CREATE TABLE accounting_profiles(user_id INTEGER PRIMARY KEY REFERENCES users(id) ON DELETE CASCADE, endpoint TEXT NOT NULL, protected_value BLOB NOT NULL CHECK(length(protected_value)>0), last_success_utc TEXT NOT NULL, revision TEXT NOT NULL)");
  Exec(c,t,"PRAGMA user_version=3");
 }
 static void UpgradeToV4(SqliteConnection c,SqliteTransaction t)
 {
  Exec(c,t,"INSERT OR IGNORE INTO permissions(code) VALUES('Accounting.Customers.Map')");
  Exec(c,t,"INSERT OR IGNORE INTO role_permissions(role_id,permission_code,allowed) SELECT id,'Accounting.Customers.Map',1 FROM roles WHERE name='Administrator'");
  Exec(c,t,"CREATE TABLE accounting_link_scopes(user_id INTEGER NOT NULL REFERENCES users(id),revision TEXT NOT NULL,business_name TEXT NOT NULL CHECK(length(trim(business_name))>0),PRIMARY KEY(user_id,revision))");
  Exec(c,t,"CREATE TABLE accounting_customer_links(user_id INTEGER NOT NULL,revision TEXT NOT NULL,client_id INTEGER NOT NULL REFERENCES clients(id) ON DELETE RESTRICT,customer_key TEXT NOT NULL,PRIMARY KEY(user_id,revision,client_id),UNIQUE(user_id,revision,customer_key),FOREIGN KEY(user_id,revision) REFERENCES accounting_link_scopes(user_id,revision))");
  Exec(c,t,"PRAGMA user_version=4");
 }
 static void UpgradeToV5(SqliteConnection c,SqliteTransaction t)
 {
  Exec(c,t,"INSERT OR IGNORE INTO permissions(code) VALUES('Accounting.Identity.Manage')");
  Exec(c,t,"INSERT OR IGNORE INTO role_permissions(role_id,permission_code,allowed) SELECT id,'Accounting.Identity.Manage',1 FROM roles WHERE name='Administrator'");
  Exec(c,t,"CREATE TABLE accounting_business_marker(id INTEGER PRIMARY KEY CHECK(id=1),marker_key TEXT NOT NULL, bound_utc TEXT NOT NULL)");
  Exec(c,t,"PRAGMA user_version=5");
 }
 static void UpgradeToV6(SqliteConnection c,SqliteTransaction t)
 {
  Exec(c,t,"INSERT OR IGNORE INTO permissions(code) VALUES('Accounting.Directory.Sync')");
  Exec(c,t,"INSERT OR IGNORE INTO role_permissions(role_id,permission_code,allowed) SELECT id,'Accounting.Directory.Sync',1 FROM roles WHERE name='Administrator'");
  Exec(c,t,"CREATE TABLE accounting_directory_scopes(user_id INTEGER NOT NULL REFERENCES users(id),revision TEXT NOT NULL,marker_key TEXT NOT NULL,business_name TEXT NOT NULL,synced_utc TEXT NOT NULL,PRIMARY KEY(user_id,revision))");
  Exec(c,t,"CREATE TABLE accounting_customer_directory(user_id INTEGER NOT NULL,revision TEXT NOT NULL,customer_key TEXT NOT NULL,name TEXT NOT NULL,status TEXT NOT NULL,present INTEGER NOT NULL CHECK(present IN (0,1)),PRIMARY KEY(user_id,revision,customer_key),FOREIGN KEY(user_id,revision) REFERENCES accounting_directory_scopes(user_id,revision))");
  Exec(c,t,"CREATE TABLE accounting_account_roles(user_id INTEGER NOT NULL,revision TEXT NOT NULL,role TEXT NOT NULL CHECK(role IN ('ServiceIncome','OfficeExpense')),account_key TEXT NOT NULL,account_name TEXT NOT NULL,account_code TEXT,PRIMARY KEY(user_id,revision,role),UNIQUE(user_id,revision,account_key),FOREIGN KEY(user_id,revision) REFERENCES accounting_directory_scopes(user_id,revision))");
  Exec(c,t,"PRAGMA user_version=6");
 }
 static void UpgradeToV7(SqliteConnection c,SqliteTransaction t)
 {
  Exec(c,t,"INSERT OR IGNORE INTO permissions(code) VALUES('Accounting.Posting.Rehearse')");
  Exec(c,t,"INSERT OR IGNORE INTO role_permissions(role_id,permission_code,allowed) SELECT id,'Accounting.Posting.Rehearse',1 FROM roles WHERE name='Administrator'");
  // Rehearsal history is deliberately independent of user, endpoint, token and profile revision.
  Exec(c,t,"CREATE TABLE accounting_posting_rehearsals(id INTEGER PRIMARY KEY,source_key TEXT NOT NULL UNIQUE CHECK(length(source_key)=36),marker_key TEXT NOT NULL CHECK(length(marker_key)=36),kind TEXT NOT NULL CHECK(kind IN ('Invoice','Receipt')),payload_hash TEXT NOT NULL CHECK(length(payload_hash)=64),state TEXT NOT NULL CHECK(state IN ('Reserved','InFlight','Uncertain','SimulatedConfirmed','Cancelled')),attempt_key TEXT UNIQUE,simulated_external_key TEXT UNIQUE,created_by INTEGER NOT NULL REFERENCES users(id),updated_by INTEGER NOT NULL REFERENCES users(id),created_utc TEXT NOT NULL,updated_utc TEXT NOT NULL,CHECK((state IN ('Reserved','Cancelled') AND attempt_key IS NULL AND simulated_external_key IS NULL) OR (state IN ('InFlight','Uncertain') AND attempt_key IS NOT NULL AND simulated_external_key IS NULL) OR (state='SimulatedConfirmed' AND attempt_key IS NOT NULL AND simulated_external_key IS NOT NULL)))");
  Exec(c,t,"CREATE TABLE accounting_lifecycle_hold(id INTEGER PRIMARY KEY CHECK(id=1),reason TEXT NOT NULL CHECK(reason IN ('Clone','Restore','SuspectedIdentityChange')),declared_by INTEGER NOT NULL REFERENCES users(id),declared_utc TEXT NOT NULL)");
  Exec(c,t,"PRAGMA user_version=7");
 }
 static void UpgradeToV8(SqliteConnection c,SqliteTransaction t)
 {
  // All v7 identities, fingerprints, states, attempts, actor IDs and timestamps survive.
  // No grants are reset; lifecycle hold remains in its original table.
  Exec(c,t,"CREATE TABLE accounting_posting_rehearsals_v8(id INTEGER PRIMARY KEY,source_key TEXT NOT NULL UNIQUE CHECK(length(source_key)=36),marker_key TEXT NOT NULL CHECK(length(marker_key)=36),kind TEXT NOT NULL CHECK(kind IN ('Invoice','Receipt','ExpensePayment')),payload_hash TEXT NOT NULL CHECK(length(payload_hash)=64),state TEXT NOT NULL CHECK(state IN ('Reserved','InFlight','Uncertain','SimulatedConfirmed','Cancelled')),attempt_key TEXT UNIQUE,simulated_external_key TEXT UNIQUE,created_by INTEGER NOT NULL REFERENCES users(id),updated_by INTEGER NOT NULL REFERENCES users(id),created_utc TEXT NOT NULL,updated_utc TEXT NOT NULL,CHECK((state IN ('Reserved','Cancelled') AND attempt_key IS NULL AND simulated_external_key IS NULL) OR (state IN ('InFlight','Uncertain') AND attempt_key IS NOT NULL AND simulated_external_key IS NULL) OR (state='SimulatedConfirmed' AND attempt_key IS NOT NULL AND simulated_external_key IS NOT NULL)))");
  Exec(c,t,"INSERT INTO accounting_posting_rehearsals_v8(id,source_key,marker_key,kind,payload_hash,state,attempt_key,simulated_external_key,created_by,updated_by,created_utc,updated_utc) SELECT id,source_key,marker_key,kind,payload_hash,state,attempt_key,simulated_external_key,created_by,updated_by,created_utc,updated_utc FROM accounting_posting_rehearsals");
  if(Convert.ToInt64(Scalar(c,t,"SELECT count(*) FROM accounting_posting_rehearsals_v8"))!=Convert.ToInt64(Scalar(c,t,"SELECT count(*) FROM accounting_posting_rehearsals")))throw new InvalidOperationException("Rehearsal migration count mismatch.");
  Exec(c,t,"DROP TABLE accounting_posting_rehearsals");
  Exec(c,t,"ALTER TABLE accounting_posting_rehearsals_v8 RENAME TO accounting_posting_rehearsals");
  Exec(c,t,"PRAGMA user_version=8");
 }
 static void UpgradeToV9(SqliteConnection c,SqliteTransaction t)
 {
  Exec(c,t,"INSERT OR IGNORE INTO permissions(code) VALUES('Accounting.Posting.RehearseApprove')");
  Exec(c,t,"INSERT OR IGNORE INTO role_permissions(role_id,permission_code,allowed) SELECT id,'Accounting.Posting.RehearseApprove',1 FROM roles WHERE name='Administrator'");
  // Historical reservations remain unapproved. No retroactive approvals or state resets.
  Exec(c,t,"CREATE TABLE accounting_rehearsal_approvals(rehearsal_id INTEGER PRIMARY KEY REFERENCES accounting_posting_rehearsals(id) ON DELETE RESTRICT,marker_key TEXT NOT NULL CHECK(length(marker_key)=36),payload_hash TEXT NOT NULL CHECK(length(payload_hash)=64),approved_by INTEGER NOT NULL REFERENCES users(id),approved_utc TEXT NOT NULL)");
  Exec(c,t,"PRAGMA user_version=9");
 }
 static void UpgradeToV10(SqliteConnection c,SqliteTransaction t)
 {
  foreach(var grant in new[]{"Accounting.WireDraft.Prepare","Accounting.WireDraft.Approve"})
  {
   Exec(c,t,"INSERT OR IGNORE INTO permissions(code) VALUES($p)",("$p",grant));
   Exec(c,t,"INSERT OR IGNORE INTO role_permissions(role_id,permission_code,allowed) SELECT id,$p,1 FROM roles WHERE name='Administrator'",("$p",grant));
  }
  // Full-wire drafts never promote legacy rehearsal rows or reuse their approval.
  Exec(c,t,"CREATE TABLE accounting_wire_drafts(id INTEGER PRIMARY KEY,source_key TEXT NOT NULL UNIQUE CHECK(length(source_key)=36),marker_key TEXT NOT NULL CHECK(length(marker_key)=36),kind TEXT NOT NULL CHECK(kind IN ('Invoice','Receipt','ExpensePayment')),reference TEXT NOT NULL UNIQUE CHECK(length(reference)=40),intent_json TEXT NOT NULL CHECK(length(intent_json) BETWEEN 1 AND 8192),wire_json TEXT NOT NULL CHECK(length(wire_json) BETWEEN 1 AND 8192),payload_hash TEXT NOT NULL CHECK(length(payload_hash)=64),state TEXT NOT NULL CHECK(state IN ('Draft','Approved','Cancelled')),created_by INTEGER NOT NULL REFERENCES users(id),created_utc TEXT NOT NULL,approved_by INTEGER REFERENCES users(id),approved_utc TEXT,cancelled_by INTEGER REFERENCES users(id),cancelled_utc TEXT,CHECK((approved_by IS NULL)=(approved_utc IS NULL)),CHECK((cancelled_by IS NULL)=(cancelled_utc IS NULL)),CHECK((state='Draft' AND approved_by IS NULL AND cancelled_by IS NULL) OR (state='Approved' AND approved_by IS NOT NULL AND cancelled_by IS NULL) OR (state='Cancelled' AND cancelled_by IS NOT NULL)))");
  Exec(c,t,"CREATE TRIGGER wire_draft_no_legacy_collision BEFORE INSERT ON accounting_wire_drafts WHEN EXISTS(SELECT 1 FROM accounting_posting_rehearsals WHERE source_key=NEW.source_key) BEGIN SELECT RAISE(ABORT,'Legacy operation collision'); END");
  Exec(c,t,"CREATE TRIGGER legacy_no_wire_draft_collision BEFORE INSERT ON accounting_posting_rehearsals WHEN EXISTS(SELECT 1 FROM accounting_wire_drafts WHERE source_key=NEW.source_key) BEGIN SELECT RAISE(ABORT,'Full-wire operation collision'); END");
  Exec(c,t,"CREATE TRIGGER wire_draft_immutable_payload BEFORE UPDATE OF id,source_key,marker_key,kind,reference,intent_json,wire_json,payload_hash,created_by,created_utc ON accounting_wire_drafts BEGIN SELECT RAISE(ABORT,'Immutable wire draft'); END");
  Exec(c,t,"CREATE TRIGGER wire_draft_no_delete BEFORE DELETE ON accounting_wire_drafts BEGIN SELECT RAISE(ABORT,'Wire draft history retained'); END");
  Exec(c,t,"CREATE TRIGGER wire_draft_state_guard BEFORE UPDATE OF state,approved_by,approved_utc,cancelled_by,cancelled_utc ON accounting_wire_drafts WHEN NOT ((OLD.state='Draft' AND NEW.state='Approved') OR (OLD.state IN ('Draft','Approved') AND NEW.state='Cancelled' AND NEW.approved_by IS OLD.approved_by AND NEW.approved_utc IS OLD.approved_utc)) BEGIN SELECT RAISE(ABORT,'Invalid wire draft transition'); END");
  Exec(c,t,"PRAGMA user_version=10");
 }
 static void UpgradeToV11(SqliteConnection c,SqliteTransaction t)
 {
  Exec(c,t,"INSERT OR IGNORE INTO permissions(code) VALUES('Accounting.WireDraft.Rehearse')");
  Exec(c,t,"INSERT OR IGNORE INTO role_permissions(role_id,permission_code,allowed) SELECT id,'Accounting.WireDraft.Rehearse',1 FROM roles WHERE name='Administrator'");
  Exec(c,t,"CREATE TABLE accounting_wire_attempts(id INTEGER PRIMARY KEY,draft_id INTEGER NOT NULL UNIQUE REFERENCES accounting_wire_drafts(id) ON DELETE RESTRICT,attempt_key TEXT NOT NULL UNIQUE CHECK(length(attempt_key)=36),state TEXT NOT NULL CHECK(state IN ('Reserved','Witnessed','Uncertain','ConfirmationForReview','ReviewedSimulation')),reason TEXT NOT NULL,created_by INTEGER NOT NULL REFERENCES users(id),created_utc TEXT NOT NULL,updated_by INTEGER NOT NULL REFERENCES users(id),updated_utc TEXT NOT NULL)");
  Exec(c,t,"CREATE TRIGGER wire_attempt_no_delete BEFORE DELETE ON accounting_wire_attempts BEGIN SELECT RAISE(ABORT,'Wire attempt history retained'); END");
  Exec(c,t,"CREATE TRIGGER wire_attempt_immutable BEFORE UPDATE OF id,draft_id,attempt_key,created_by,created_utc ON accounting_wire_attempts BEGIN SELECT RAISE(ABORT,'Immutable attempt'); END");
  Exec(c,t,"CREATE TRIGGER wire_attempt_state_guard BEFORE UPDATE ON accounting_wire_attempts WHEN NOT ((OLD.state='Reserved' AND NEW.state IN ('Witnessed','Uncertain')) OR (OLD.state='Witnessed' AND NEW.state IN ('Uncertain','ConfirmationForReview')) OR (OLD.state='ConfirmationForReview' AND NEW.state='ReviewedSimulation')) BEGIN SELECT RAISE(ABORT,'Invalid attempt transition'); END");
  Exec(c,t,"CREATE TRIGGER wire_attempt_no_cancel BEFORE UPDATE OF state ON accounting_wire_drafts WHEN EXISTS(SELECT 1 FROM accounting_wire_attempts WHERE draft_id=OLD.id) BEGIN SELECT RAISE(ABORT,'Attempted draft retained'); END");
  Exec(c,t,"PRAGMA user_version=11");
 }
 static void UpgradeToV12(SqliteConnection c,SqliteTransaction t)
 {
  foreach(var grant in new[]{"Engagements.View","Engagements.Edit","Engagements.Issue","Engagements.Accept","Projects.View"}){
   Exec(c,t,"INSERT OR IGNORE INTO permissions(code) VALUES($p)",("$p",grant));
   Exec(c,t,"INSERT OR IGNORE INTO role_permissions(role_id,permission_code,allowed) SELECT id,$p,1 FROM roles WHERE name='Administrator'",("$p",grant));
  }
  Exec(c,t,"INSERT OR IGNORE INTO sequences(name,next_value) VALUES('Proposal',1),('Project',1)");
  Exec(c,t,@"CREATE TABLE proposals(id INTEGER PRIMARY KEY,proposal_no TEXT NOT NULL UNIQUE,client_id INTEGER NOT NULL REFERENCES clients(id) ON DELETE RESTRICT,title TEXT NOT NULL CHECK(length(trim(title)) BETWEEN 1 AND 200),site TEXT NOT NULL CHECK(length(trim(site)) BETWEEN 1 AND 300),scope TEXT NOT NULL CHECK(length(trim(scope)) BETWEEN 1 AND 20000),fee_cents INTEGER NOT NULL CHECK(fee_cents BETWEEN 0 AND 999999999999),revision INTEGER NOT NULL CHECK(revision>=1),state TEXT NOT NULL CHECK(state IN ('Draft','Issued','Accepted','Rejected')),snapshot TEXT,snapshot_hash TEXT,created_by INTEGER NOT NULL REFERENCES users(id),created_utc TEXT NOT NULL,issued_by INTEGER REFERENCES users(id),issued_utc TEXT,decision_by INTEGER REFERENCES users(id),decision_utc TEXT,decision_note TEXT,CHECK((state='Draft' AND snapshot IS NULL AND snapshot_hash IS NULL AND issued_by IS NULL AND issued_utc IS NULL AND decision_by IS NULL AND decision_utc IS NULL AND decision_note IS NULL) OR (state='Issued' AND snapshot IS NOT NULL AND length(snapshot_hash)=64 AND issued_by IS NOT NULL AND issued_utc IS NOT NULL AND decision_by IS NULL AND decision_utc IS NULL AND decision_note IS NULL) OR (state IN ('Accepted','Rejected') AND snapshot IS NOT NULL AND length(snapshot_hash)=64 AND issued_by IS NOT NULL AND issued_utc IS NOT NULL AND decision_by IS NOT NULL AND decision_utc IS NOT NULL AND length(trim(decision_note)) BETWEEN 1 AND 1000)))");
  Exec(c,t,"CREATE TABLE engagements(id INTEGER PRIMARY KEY,proposal_id INTEGER NOT NULL UNIQUE REFERENCES proposals(id) ON DELETE RESTRICT,created_by INTEGER NOT NULL REFERENCES users(id),created_utc TEXT NOT NULL)");
  Exec(c,t,"CREATE TABLE projects(id INTEGER PRIMARY KEY,project_no TEXT NOT NULL UNIQUE,engagement_id INTEGER NOT NULL UNIQUE REFERENCES engagements(id) ON DELETE RESTRICT,state TEXT NOT NULL CHECK(state='Pending'),setup_state TEXT NOT NULL CHECK(setup_state IN ('NotStarted','InProgress','Complete')),created_by INTEGER NOT NULL REFERENCES users(id),created_utc TEXT NOT NULL)");
  Exec(c,t,"CREATE TRIGGER proposal_no_delete BEFORE DELETE ON proposals BEGIN SELECT RAISE(ABORT,'Proposal history retained'); END");
  Exec(c,t,"CREATE TRIGGER proposal_identity_immutable BEFORE UPDATE OF id,proposal_no,client_id,created_by,created_utc ON proposals BEGIN SELECT RAISE(ABORT,'Proposal identity immutable'); END");
  Exec(c,t,"CREATE TRIGGER proposal_frozen BEFORE UPDATE OF title,site,scope,fee_cents,revision,snapshot,snapshot_hash,issued_by,issued_utc ON proposals WHEN OLD.state!='Draft' BEGIN SELECT RAISE(ABORT,'Issued proposal immutable'); END");
  Exec(c,t,"CREATE TRIGGER proposal_state_guard BEFORE UPDATE ON proposals WHEN NOT ((OLD.state='Draft' AND NEW.state IN ('Draft','Issued')) OR (OLD.state='Issued' AND NEW.state IN ('Accepted','Rejected'))) BEGIN SELECT RAISE(ABORT,'Invalid proposal transition'); END");
  Exec(c,t,"CREATE TRIGGER engagement_accepted_only BEFORE INSERT ON engagements WHEN NOT EXISTS(SELECT 1 FROM proposals WHERE id=NEW.proposal_id AND state='Accepted') BEGIN SELECT RAISE(ABORT,'Accepted proposal required'); END");
  Exec(c,t,"CREATE TRIGGER engagement_no_delete BEFORE DELETE ON engagements BEGIN SELECT RAISE(ABORT,'Engagement retained'); END");
  Exec(c,t,"CREATE TRIGGER engagement_immutable BEFORE UPDATE ON engagements BEGIN SELECT RAISE(ABORT,'Engagement immutable'); END");
  Exec(c,t,"CREATE TRIGGER project_no_delete BEFORE DELETE ON projects BEGIN SELECT RAISE(ABORT,'Project retained'); END");
  Exec(c,t,"CREATE TRIGGER project_identity_immutable BEFORE UPDATE OF id,project_no,engagement_id,created_by,created_utc ON projects BEGIN SELECT RAISE(ABORT,'Project identity immutable'); END");
  Exec(c,t,"PRAGMA user_version=12");
 }
 static void UpgradeToV13(SqliteConnection c,SqliteTransaction t)
 {
  foreach(var grant in new[]{"Agreements.Record","Agreements.View","Projects.Authorize","Projects.Setup"}){
   Exec(c,t,"INSERT OR IGNORE INTO permissions(code) VALUES($p)",("$p",grant));
   Exec(c,t,"INSERT OR IGNORE INTO role_permissions(role_id,permission_code,allowed) SELECT id,$p,1 FROM roles WHERE name='Administrator'",("$p",grant));
  }
  // Rebuild the previously Pending-only table before adding new foreign keys.
  Exec(c,t,"DROP TRIGGER project_no_delete; DROP TRIGGER project_identity_immutable; ALTER TABLE projects RENAME TO projects_v12");
  Exec(c,t,"CREATE TABLE projects(id INTEGER PRIMARY KEY,project_no TEXT NOT NULL UNIQUE,engagement_id INTEGER NOT NULL UNIQUE REFERENCES engagements(id) ON DELETE RESTRICT,state TEXT NOT NULL CHECK(state IN ('Pending','Active')),setup_state TEXT NOT NULL CHECK(setup_state IN ('NotStarted','InProgress','Complete')),created_by INTEGER NOT NULL REFERENCES users(id),created_utc TEXT NOT NULL)");
  Exec(c,t,"INSERT INTO projects SELECT * FROM projects_v12; DROP TABLE projects_v12");
  Exec(c,t,"CREATE TRIGGER project_no_delete BEFORE DELETE ON projects BEGIN SELECT RAISE(ABORT,'Project retained'); END");
  Exec(c,t,"CREATE TRIGGER project_identity_immutable BEFORE UPDATE OF id,project_no,engagement_id,created_by,created_utc ON projects BEGIN SELECT RAISE(ABORT,'Project identity immutable'); END");
  Exec(c,t,@"CREATE TABLE project_agreements(id INTEGER PRIMARY KEY,project_id INTEGER NOT NULL REFERENCES projects(id) ON DELETE RESTRICT,issued_hash TEXT NOT NULL CHECK(length(issued_hash)=64),file_name TEXT NOT NULL CHECK(length(file_name) BETWEEN 1 AND 180),pdf_bytes BLOB NOT NULL CHECK(length(pdf_bytes) BETWEEN 8 AND 10485760),pdf_hash TEXT NOT NULL CHECK(length(pdf_hash)=64),review_note TEXT NOT NULL CHECK(length(trim(review_note)) BETWEEN 1 AND 1000),recorded_by INTEGER NOT NULL REFERENCES users(id),recorded_utc TEXT NOT NULL,UNIQUE(project_id,pdf_hash))");
  Exec(c,t,"CREATE TABLE project_start_authorizations(project_id INTEGER PRIMARY KEY REFERENCES projects(id) ON DELETE RESTRICT,agreement_id INTEGER NOT NULL UNIQUE REFERENCES project_agreements(id) ON DELETE RESTRICT,authorization_note TEXT NOT NULL CHECK(length(trim(authorization_note)) BETWEEN 1 AND 1000),authorized_by INTEGER NOT NULL REFERENCES users(id),authorized_utc TEXT NOT NULL)");
  Exec(c,t,"CREATE TABLE project_setup_items(project_id INTEGER NOT NULL REFERENCES projects(id) ON DELETE RESTRICT,item TEXT NOT NULL CHECK(item IN ('ScopeReviewed','ConflictReviewed','ProjectContacts','DeliveryPlan')),complete INTEGER NOT NULL CHECK(complete IN (0,1)),note TEXT NOT NULL CHECK(length(trim(note)) BETWEEN 1 AND 1000),updated_by INTEGER NOT NULL REFERENCES users(id),updated_utc TEXT NOT NULL,PRIMARY KEY(project_id,item))");
  foreach(var table in new[]{"project_agreements","project_start_authorizations"}){
   Exec(c,t,$"CREATE TRIGGER {table}_no_delete BEFORE DELETE ON {table} BEGIN SELECT RAISE(ABORT,'Evidence retained'); END");
   Exec(c,t,$"CREATE TRIGGER {table}_immutable BEFORE UPDATE ON {table} BEGIN SELECT RAISE(ABORT,'Evidence immutable'); END");
  }
  Exec(c,t,"CREATE TRIGGER agreement_pending_only BEFORE INSERT ON project_agreements WHEN NOT EXISTS(SELECT 1 FROM projects p JOIN engagements e ON e.id=p.engagement_id JOIN proposals s ON s.id=e.proposal_id WHERE p.id=NEW.project_id AND p.state='Pending' AND s.state='Accepted' AND s.snapshot_hash=NEW.issued_hash) BEGIN SELECT RAISE(ABORT,'Pending accepted proposal required'); END");
  Exec(c,t,"CREATE TRIGGER start_authorization_current BEFORE INSERT ON project_start_authorizations WHEN NOT EXISTS(SELECT 1 FROM projects p JOIN project_agreements a ON a.project_id=p.id WHERE p.id=NEW.project_id AND p.state='Pending' AND a.id=NEW.agreement_id AND a.id=(SELECT max(id) FROM project_agreements WHERE project_id=p.id)) BEGIN SELECT RAISE(ABORT,'Current agreement required'); END");
  Exec(c,t,"CREATE TRIGGER project_pending_insert BEFORE INSERT ON projects WHEN NEW.state!='Pending' BEGIN SELECT RAISE(ABORT,'Pending creation required'); END");
  Exec(c,t,"CREATE TRIGGER project_start_guard BEFORE UPDATE OF state ON projects WHEN NOT (OLD.state='Pending' AND NEW.state='Active' AND EXISTS(SELECT 1 FROM project_start_authorizations WHERE project_id=OLD.id)) BEGIN SELECT RAISE(ABORT,'Explicit authorization required'); END");
  Exec(c,t,"CREATE TRIGGER setup_no_delete BEFORE DELETE ON project_setup_items BEGIN SELECT RAISE(ABORT,'Setup history retained'); END");
  Exec(c,t,"CREATE TRIGGER setup_identity_immutable BEFORE UPDATE OF project_id,item ON project_setup_items BEGIN SELECT RAISE(ABORT,'Setup identity immutable'); END");
  Exec(c,t,"PRAGMA user_version=13");
 }
 static void UpgradeToV14(SqliteConnection c,SqliteTransaction t)
 {
  Exec(c,t,"ALTER TABLE users ADD COLUMN credential_revision INTEGER NOT NULL DEFAULT 1 CHECK(credential_revision>=1)");
  foreach(var grant in new[]{"Administration.Manage","Employees.View","Employees.Edit"}){
   Exec(c,t,"INSERT OR IGNORE INTO permissions(code) VALUES($p)",("$p",grant));
   Exec(c,t,"INSERT OR IGNORE INTO role_permissions(role_id,permission_code,allowed) SELECT id,$p,1 FROM roles WHERE name='Administrator'",("$p",grant));
  }
  // Do not seed non-administrator access implicitly, including on migration.
  Exec(c,t,"INSERT OR IGNORE INTO roles(name) SELECT 'Engineer' WHERE EXISTS(SELECT 1 FROM roles WHERE name='Administrator'); INSERT OR IGNORE INTO roles(name) SELECT 'Administrative' WHERE EXISTS(SELECT 1 FROM roles WHERE name='Administrator'); INSERT OR IGNORE INTO roles(name) SELECT 'Project Manager' WHERE EXISTS(SELECT 1 FROM roles WHERE name='Administrator'); INSERT OR IGNORE INTO roles(name) SELECT 'Financial / Accounting' WHERE EXISTS(SELECT 1 FROM roles WHERE name='Administrator')");
  Exec(c,t,"INSERT OR IGNORE INTO sequences(name,next_value) VALUES('Employee',1)");
  Exec(c,t,"CREATE TABLE employees(id INTEGER PRIMARY KEY,employee_no TEXT NOT NULL UNIQUE,user_id INTEGER UNIQUE REFERENCES users(id) ON DELETE RESTRICT,first_name TEXT NOT NULL CHECK(length(trim(first_name)) BETWEEN 1 AND 60),last_name TEXT NOT NULL CHECK(length(trim(last_name)) BETWEEN 1 AND 60),email TEXT NOT NULL,title TEXT NOT NULL,active INTEGER NOT NULL CHECK(active IN(0,1)),revision INTEGER NOT NULL CHECK(revision>=1),created_by INTEGER NOT NULL REFERENCES users(id),created_utc TEXT NOT NULL)");
  Exec(c,t,"CREATE TABLE employee_licenses(id INTEGER PRIMARY KEY,employee_id INTEGER NOT NULL REFERENCES employees(id) ON DELETE RESTRICT,jurisdiction TEXT NOT NULL,license_type TEXT NOT NULL,license_number TEXT NOT NULL,expires_on TEXT NOT NULL CHECK(length(expires_on)=10),revision INTEGER NOT NULL CHECK(revision>=1),UNIQUE(employee_id,jurisdiction,license_type,license_number))");
  Exec(c,t,"CREATE TRIGGER employee_identity_immutable BEFORE UPDATE OF id,employee_no,user_id,created_by,created_utc ON employees BEGIN SELECT RAISE(ABORT,'Employee identity immutable'); END");
  Exec(c,t,"CREATE TRIGGER employee_no_delete BEFORE DELETE ON employees BEGIN SELECT RAISE(ABORT,'Employee retained'); END");
  Exec(c,t,"CREATE TRIGGER employee_license_identity_immutable BEFORE UPDATE OF id,employee_id,jurisdiction,license_type,license_number ON employee_licenses BEGIN SELECT RAISE(ABORT,'License identity immutable'); END");
  Exec(c,t,"CREATE TRIGGER employee_license_no_delete BEFORE DELETE ON employee_licenses BEGIN SELECT RAISE(ABORT,'License retained'); END");
  Exec(c,t,"PRAGMA user_version=14");
 }
 static void UpgradeToV15(SqliteConnection c,SqliteTransaction t)
 {
  foreach(var grant in new[]{"Projects.Plan","Work.Own"}){
   Exec(c,t,"INSERT OR IGNORE INTO permissions(code) VALUES($p)",("$p",grant));
   Exec(c,t,"INSERT OR IGNORE INTO role_permissions(role_id,permission_code,allowed) SELECT id,$p,1 FROM roles WHERE name='Administrator'",("$p",grant));
  }
  Exec(c,t,"CREATE TABLE project_phases(id INTEGER PRIMARY KEY,project_id INTEGER NOT NULL REFERENCES projects(id) ON DELETE RESTRICT,name TEXT NOT NULL CHECK(length(trim(name)) BETWEEN 1 AND 120),start_on TEXT NOT NULL CHECK(length(start_on)=10),end_on TEXT NOT NULL CHECK(length(end_on)=10 AND end_on>=start_on),revision INTEGER NOT NULL CHECK(revision>=1),UNIQUE(project_id,name))");
  Exec(c,t,"CREATE TABLE project_tasks(id INTEGER PRIMARY KEY,phase_id INTEGER NOT NULL REFERENCES project_phases(id) ON DELETE RESTRICT,employee_id INTEGER NOT NULL REFERENCES employees(id) ON DELETE RESTRICT,title TEXT NOT NULL CHECK(length(trim(title)) BETWEEN 1 AND 200),start_on TEXT NOT NULL CHECK(length(start_on)=10),end_on TEXT NOT NULL CHECK(length(end_on)=10 AND end_on>=start_on),estimate_minutes INTEGER NOT NULL CHECK(estimate_minutes BETWEEN 1 AND 600000),state TEXT NOT NULL CHECK(state IN ('Planned','InProgress','Completed')),revision INTEGER NOT NULL CHECK(revision>=1))");
  Exec(c,t,"CREATE INDEX ix_tasks_employee_dates ON project_tasks(employee_id,start_on,end_on)");
  Exec(c,t,"CREATE TRIGGER phase_no_delete BEFORE DELETE ON project_phases BEGIN SELECT RAISE(ABORT,'Phase history retained'); END");
  Exec(c,t,"CREATE TRIGGER phase_identity_immutable BEFORE UPDATE OF id,project_id ON project_phases BEGIN SELECT RAISE(ABORT,'Phase identity immutable'); END");
  Exec(c,t,"CREATE TRIGGER task_no_delete BEFORE DELETE ON project_tasks BEGIN SELECT RAISE(ABORT,'Task history retained'); END");
  Exec(c,t,"CREATE TRIGGER task_identity_immutable BEFORE UPDATE OF id,phase_id ON project_tasks BEGIN SELECT RAISE(ABORT,'Task identity immutable'); END");
  Exec(c,t,"CREATE TRIGGER task_insert_guard BEFORE INSERT ON project_tasks WHEN NEW.state!='Planned' OR NOT EXISTS(SELECT 1 FROM project_phases h JOIN employees e ON e.id=NEW.employee_id WHERE h.id=NEW.phase_id AND e.active=1 AND NEW.start_on>=h.start_on AND NEW.end_on<=h.end_on) BEGIN SELECT RAISE(ABORT,'Invalid task plan'); END");
  Exec(c,t,"CREATE TRIGGER task_plan_guard BEFORE UPDATE OF employee_id,title,start_on,end_on,estimate_minutes ON project_tasks WHEN OLD.state!='Planned' OR NOT EXISTS(SELECT 1 FROM project_phases h JOIN employees e ON e.id=NEW.employee_id WHERE h.id=NEW.phase_id AND e.active=1 AND NEW.start_on>=h.start_on AND NEW.end_on<=h.end_on) BEGIN SELECT RAISE(ABORT,'Invalid task plan change'); END");
  Exec(c,t,"CREATE TRIGGER task_state_guard BEFORE UPDATE OF state ON project_tasks WHEN NOT ((OLD.state='Planned' AND NEW.state='InProgress') OR (OLD.state='InProgress' AND NEW.state='Completed')) OR NOT EXISTS(SELECT 1 FROM project_phases h JOIN projects p ON p.id=h.project_id JOIN project_start_authorizations a ON a.project_id=p.id JOIN employees e ON e.id=OLD.employee_id WHERE h.id=OLD.phase_id AND p.state='Active' AND e.active=1) BEGIN SELECT RAISE(ABORT,'Authorized active project required'); END");
  Exec(c,t,"CREATE TRIGGER phase_bounds_guard BEFORE UPDATE OF start_on,end_on ON project_phases WHEN EXISTS(SELECT 1 FROM project_tasks WHERE phase_id=OLD.id AND (start_on<NEW.start_on OR end_on>NEW.end_on)) BEGIN SELECT RAISE(ABORT,'Existing task outside phase'); END");
  Exec(c,t,"PRAGMA user_version=15");
 }
 static void UpgradeToV16(SqliteConnection c,SqliteTransaction t)
 {
  foreach(var grant in new[]{"Work.Review","Billing.Classify"}){
   Exec(c,t,"INSERT OR IGNORE INTO permissions(code) VALUES($p)",("$p",grant));
   Exec(c,t,"INSERT OR IGNORE INTO role_permissions(role_id,permission_code,allowed) SELECT id,$p,1 FROM roles WHERE name='Administrator'",("$p",grant));
  }
  Exec(c,t,"CREATE TABLE employee_attendance(id INTEGER PRIMARY KEY,employee_id INTEGER NOT NULL REFERENCES employees(id) ON DELETE RESTRICT,clock_in INTEGER NOT NULL,clock_out INTEGER CHECK(clock_out>=clock_in),actor_id INTEGER NOT NULL REFERENCES users(id))");
  Exec(c,t,"CREATE UNIQUE INDEX ux_open_attendance ON employee_attendance(employee_id) WHERE clock_out IS NULL");
  Exec(c,t,"CREATE TRIGGER attendance_immutable BEFORE UPDATE OF id,employee_id,clock_in,actor_id ON employee_attendance BEGIN SELECT RAISE(ABORT,'Attendance identity immutable'); END");
  Exec(c,t,"CREATE TRIGGER attendance_close_once BEFORE UPDATE OF clock_out ON employee_attendance WHEN OLD.clock_out IS NOT NULL OR NEW.clock_out IS NULL BEGIN SELECT RAISE(ABORT,'Attendance already closed'); END");
  Exec(c,t,"CREATE TRIGGER attendance_no_delete BEFORE DELETE ON employee_attendance BEGIN SELECT RAISE(ABORT,'Attendance retained'); END");
  Exec(c,t,@"CREATE TABLE work_submissions(id INTEGER PRIMARY KEY,operation_key TEXT NOT NULL UNIQUE CHECK(length(operation_key)=36),employee_id INTEGER NOT NULL REFERENCES employees(id) ON DELETE RESTRICT,task_id INTEGER NOT NULL REFERENCES project_tasks(id) ON DELETE RESTRICT,kind TEXT NOT NULL CHECK(kind IN ('Time','Expense')),work_date TEXT NOT NULL CHECK(length(work_date)=10),minutes INTEGER,amount_cents INTEGER,category TEXT,description TEXT NOT NULL CHECK(length(trim(description)) BETWEEN 1 AND 1000),submitted_by INTEGER NOT NULL REFERENCES users(id),submitted_utc TEXT NOT NULL,CHECK((kind='Time' AND minutes IS NOT NULL AND minutes BETWEEN 1 AND 1440 AND amount_cents IS NULL AND category IS NULL) OR (kind='Expense' AND minutes IS NULL AND amount_cents IS NOT NULL AND amount_cents BETWEEN 1 AND 999999999999 AND category IN ('Travel','Meals','Supplies','Permit','Other'))))");
  Exec(c,t,"CREATE INDEX ix_work_employee_date ON work_submissions(employee_id,work_date)");
  Exec(c,t,"CREATE TABLE work_reviews(submission_id INTEGER PRIMARY KEY REFERENCES work_submissions(id) ON DELETE RESTRICT,decision TEXT NOT NULL CHECK(decision IN ('Approved','Rejected')),note TEXT NOT NULL CHECK(length(trim(note)) BETWEEN 1 AND 1000),reviewed_by INTEGER NOT NULL REFERENCES users(id),reviewed_utc TEXT NOT NULL)");
  Exec(c,t,"CREATE TABLE work_billing_classifications(submission_id INTEGER PRIMARY KEY REFERENCES work_submissions(id) ON DELETE RESTRICT,classification TEXT NOT NULL CHECK(classification IN ('Billable','Nonbillable')),note TEXT NOT NULL CHECK(length(trim(note)) BETWEEN 1 AND 1000),classified_by INTEGER NOT NULL REFERENCES users(id),classified_utc TEXT NOT NULL)");
  foreach(var table in new[]{"work_submissions","work_reviews","work_billing_classifications"}){
   Exec(c,t,$"CREATE TRIGGER {table}_immutable BEFORE UPDATE ON {table} BEGIN SELECT RAISE(ABORT,'Work evidence immutable'); END");
   Exec(c,t,$"CREATE TRIGGER {table}_no_delete BEFORE DELETE ON {table} BEGIN SELECT RAISE(ABORT,'Work evidence retained'); END");
  }
  Exec(c,t,"CREATE TRIGGER work_review_independent BEFORE INSERT ON work_reviews WHEN EXISTS(SELECT 1 FROM work_submissions WHERE id=NEW.submission_id AND submitted_by=NEW.reviewed_by) BEGIN SELECT RAISE(ABORT,'Self review prohibited'); END");
  Exec(c,t,"CREATE TRIGGER work_billing_approved_only BEFORE INSERT ON work_billing_classifications WHEN NOT EXISTS(SELECT 1 FROM work_reviews r JOIN work_submissions s ON s.id=r.submission_id WHERE r.submission_id=NEW.submission_id AND r.decision='Approved' AND s.submitted_by!=NEW.classified_by) BEGIN SELECT RAISE(ABORT,'Independent approved work required'); END");
  Exec(c,t,"PRAGMA user_version=16");
 }
 static void UpgradeToV17(SqliteConnection c,SqliteTransaction t)
 {
  foreach(var grant in new[]{"Documents.View","Documents.Record","Documents.Restricted","QA.View","QA.Edit","QA.Close","Scope.Authorize"}){
   Exec(c,t,"INSERT OR IGNORE INTO permissions(code) VALUES($p)",("$p",grant));
   Exec(c,t,"INSERT OR IGNORE INTO role_permissions(role_id,permission_code,allowed) SELECT id,$p,1 FROM roles WHERE name='Administrator'",("$p",grant));
  }
  Exec(c,t,@"CREATE TABLE project_documents(id INTEGER PRIMARY KEY,project_id INTEGER NOT NULL REFERENCES projects(id) ON DELETE RESTRICT,operation_key TEXT NOT NULL UNIQUE CHECK(length(operation_key)=36),supersedes_id INTEGER UNIQUE REFERENCES project_documents(id) ON DELETE RESTRICT,classification TEXT NOT NULL CHECK(classification IN ('Production','Restricted')),category TEXT NOT NULL CHECK(category IN ('Correspondence','Drawings','Calculations','Reports','Contracts','Other')),file_name TEXT NOT NULL CHECK(length(file_name) BETWEEN 1 AND 180),file_bytes BLOB NOT NULL CHECK(length(file_bytes) BETWEEN 1 AND 20971520),file_hash TEXT NOT NULL CHECK(length(file_hash)=64),recorded_by INTEGER NOT NULL REFERENCES users(id),recorded_utc TEXT NOT NULL)");
  Exec(c,t,"CREATE INDEX ix_project_documents ON project_documents(project_id,id)");
  Exec(c,t,"CREATE TRIGGER document_version_guard BEFORE INSERT ON project_documents WHEN NEW.supersedes_id IS NOT NULL AND NOT EXISTS(SELECT 1 FROM project_documents d WHERE d.id=NEW.supersedes_id AND d.project_id=NEW.project_id AND d.classification=NEW.classification AND d.category=NEW.category AND NOT EXISTS(SELECT 1 FROM project_documents n WHERE n.supersedes_id=d.id)) BEGIN SELECT RAISE(ABORT,'Current document version required'); END");
  Exec(c,t,"CREATE TABLE project_quality_items(id INTEGER PRIMARY KEY,project_id INTEGER NOT NULL REFERENCES projects(id) ON DELETE RESTRICT,title TEXT NOT NULL CHECK(length(trim(title)) BETWEEN 1 AND 200),severity TEXT NOT NULL CHECK(severity IN ('Observation','Correction','Critical')),description TEXT NOT NULL CHECK(length(trim(description)) BETWEEN 1 AND 4000),recorded_by INTEGER NOT NULL REFERENCES users(id),recorded_utc TEXT NOT NULL)");
  Exec(c,t,"CREATE TABLE project_quality_notes(id INTEGER PRIMARY KEY,item_id INTEGER NOT NULL REFERENCES project_quality_items(id) ON DELETE RESTRICT,note TEXT NOT NULL CHECK(length(trim(note)) BETWEEN 1 AND 4000),recorded_by INTEGER NOT NULL REFERENCES users(id),recorded_utc TEXT NOT NULL)");
  Exec(c,t,"CREATE TABLE project_quality_closures(item_id INTEGER PRIMARY KEY REFERENCES project_quality_items(id) ON DELETE RESTRICT,resolution TEXT NOT NULL CHECK(length(trim(resolution)) BETWEEN 1 AND 4000),closed_by INTEGER NOT NULL REFERENCES users(id),closed_utc TEXT NOT NULL)");
  Exec(c,t,"CREATE TRIGGER quality_no_note_after_close BEFORE INSERT ON project_quality_notes WHEN EXISTS(SELECT 1 FROM project_quality_closures WHERE item_id=NEW.item_id) BEGIN SELECT RAISE(ABORT,'Closed quality history'); END");
  Exec(c,t,"CREATE TABLE project_scope_changes(id INTEGER PRIMARY KEY,project_id INTEGER NOT NULL REFERENCES projects(id) ON DELETE RESTRICT,title TEXT NOT NULL CHECK(length(trim(title)) BETWEEN 1 AND 200),scope TEXT NOT NULL CHECK(length(trim(scope)) BETWEEN 1 AND 4000),recorded_by INTEGER NOT NULL REFERENCES users(id),recorded_utc TEXT NOT NULL)");
  Exec(c,t,"CREATE TABLE project_scope_decisions(change_id INTEGER PRIMARY KEY REFERENCES project_scope_changes(id) ON DELETE RESTRICT,decision TEXT NOT NULL CHECK(decision IN ('Authorized','Rejected')),evidence TEXT NOT NULL CHECK(length(trim(evidence)) BETWEEN 1 AND 4000),decided_by INTEGER NOT NULL REFERENCES users(id),decided_utc TEXT NOT NULL)");
  foreach(var table in new[]{"project_documents","project_quality_items","project_quality_notes","project_quality_closures","project_scope_changes","project_scope_decisions"}){
   Exec(c,t,$"CREATE TRIGGER {table}_immutable BEFORE UPDATE ON {table} BEGIN SELECT RAISE(ABORT,'Historical evidence immutable'); END");
   Exec(c,t,$"CREATE TRIGGER {table}_no_delete BEFORE DELETE ON {table} BEGIN SELECT RAISE(ABORT,'Historical evidence retained'); END");
  }
  Exec(c,t,"PRAGMA user_version=17");
 }
 static void UpgradeToV18(SqliteConnection c,SqliteTransaction t)
 {
  Exec(c,t,"ALTER TABLE project_tasks ADD COLUMN scope_change_id INTEGER REFERENCES project_scope_changes(id) ON DELETE RESTRICT");
  Exec(c,t,"CREATE TRIGGER task_scope_immutable BEFORE UPDATE OF scope_change_id ON project_tasks BEGIN SELECT RAISE(ABORT,'Task scope identity immutable'); END");
  Exec(c,t,"CREATE TRIGGER task_scope_insert_guard BEFORE INSERT ON project_tasks WHEN NEW.scope_change_id IS NOT NULL AND NOT EXISTS(SELECT 1 FROM project_scope_changes s JOIN project_phases h ON h.project_id=s.project_id LEFT JOIN project_scope_decisions d ON d.change_id=s.id WHERE s.id=NEW.scope_change_id AND h.id=NEW.phase_id AND coalesce(d.decision,'Proposed')!='Rejected') BEGIN SELECT RAISE(ABORT,'Invalid additional scope'); END");
  Exec(c,t,"CREATE TRIGGER task_scope_start_guard BEFORE UPDATE OF state ON project_tasks WHEN OLD.scope_change_id IS NOT NULL AND NOT EXISTS(SELECT 1 FROM project_scope_changes s JOIN project_scope_decisions d ON d.change_id=s.id JOIN project_phases h ON h.project_id=s.project_id WHERE s.id=OLD.scope_change_id AND h.id=OLD.phase_id AND d.decision='Authorized') BEGIN SELECT RAISE(ABORT,'Additional scope authorization required'); END");
  Exec(c,t,"PRAGMA user_version=18");
 }
 static void UpgradeToV19(SqliteConnection c,SqliteTransaction t)
 {
  foreach(var grant in new[]{"Billing.Rates","Billing.Invoices.View","Billing.Invoices.Edit","Billing.Invoices.Approve"}){
   Exec(c,t,"INSERT OR IGNORE INTO permissions(code) VALUES($p)",("$p",grant));
   Exec(c,t,"INSERT OR IGNORE INTO role_permissions(role_id,permission_code,allowed) SELECT id,$p,1 FROM roles WHERE name='Administrator'",("$p",grant));
  }
  Exec(c,t,"INSERT OR IGNORE INTO sequences(name,next_value) VALUES('InvoiceDraft',1)");
  Exec(c,t,"CREATE TABLE employee_rates(id INTEGER PRIMARY KEY,employee_id INTEGER NOT NULL REFERENCES employees(id) ON DELETE RESTRICT,effective_on TEXT NOT NULL CHECK(length(effective_on)=10),bill_cents INTEGER NOT NULL CHECK(bill_cents BETWEEN 0 AND 100000000),cost_cents INTEGER NOT NULL CHECK(cost_cents BETWEEN 0 AND 100000000),recorded_by INTEGER NOT NULL REFERENCES users(id),recorded_utc TEXT NOT NULL,UNIQUE(employee_id,effective_on))");
  Exec(c,t,"CREATE TABLE project_billing_policies(project_id INTEGER PRIMARY KEY REFERENCES projects(id) ON DELETE RESTRICT,mode TEXT NOT NULL CHECK(mode IN ('FixedFee','Hourly','Hybrid')),fixed_limit INTEGER NOT NULL CHECK(fixed_limit>=0),total_limit INTEGER CHECK(total_limit>=fixed_limit),expenses INTEGER NOT NULL CHECK(expenses IN(0,1)),evidence TEXT NOT NULL CHECK(length(trim(evidence)) BETWEEN 1 AND 1000),recorded_by INTEGER NOT NULL REFERENCES users(id),recorded_utc TEXT NOT NULL,CHECK(mode!='Hourly' OR fixed_limit=0))");
  Exec(c,t,@"CREATE TABLE invoice_drafts(id INTEGER PRIMARY KEY,project_id INTEGER NOT NULL REFERENCES projects(id) ON DELETE RESTRICT,operation_key TEXT NOT NULL UNIQUE CHECK(length(operation_key)=36),draft_no TEXT NOT NULL UNIQUE,invoice_date TEXT NOT NULL CHECK(length(invoice_date)=10),due_date TEXT NOT NULL CHECK(length(due_date)=10 AND due_date>=invoice_date),input_hash TEXT NOT NULL CHECK(length(input_hash)=64),payload_hash TEXT NOT NULL CHECK(length(payload_hash)=64),total_cents INTEGER NOT NULL CHECK(total_cents BETWEEN 1 AND 999999999999),created_by INTEGER NOT NULL REFERENCES users(id),created_utc TEXT NOT NULL)");
  Exec(c,t,@"CREATE TABLE invoice_draft_lines(id INTEGER PRIMARY KEY,draft_id INTEGER NOT NULL REFERENCES invoice_drafts(id) ON DELETE RESTRICT,source_id INTEGER REFERENCES work_submissions(id) ON DELETE RESTRICT,line_type TEXT NOT NULL CHECK(line_type IN ('FixedFee','Time','Expense')),description TEXT NOT NULL,minutes INTEGER,rate_cents INTEGER,amount_cents INTEGER NOT NULL CHECK(amount_cents BETWEEN 1 AND 999999999999),UNIQUE(draft_id,source_id),CHECK((line_type='FixedFee' AND source_id IS NULL AND minutes IS NULL AND rate_cents IS NULL) OR (line_type='Time' AND source_id IS NOT NULL AND minutes>0 AND rate_cents>0) OR (line_type='Expense' AND source_id IS NOT NULL AND minutes IS NULL AND rate_cents IS NULL)))");
  Exec(c,t,"CREATE TABLE invoice_draft_decisions(draft_id INTEGER PRIMARY KEY REFERENCES invoice_drafts(id) ON DELETE RESTRICT,decision TEXT NOT NULL CHECK(decision IN ('Approved','Cancelled')),note TEXT NOT NULL CHECK(length(trim(note)) BETWEEN 1 AND 1000),actor_id INTEGER NOT NULL REFERENCES users(id),decided_utc TEXT NOT NULL)");
  foreach(var table in new[]{"employee_rates","project_billing_policies","invoice_drafts","invoice_draft_lines","invoice_draft_decisions"}){
   Exec(c,t,$"CREATE TRIGGER {table}_immutable BEFORE UPDATE ON {table} BEGIN SELECT RAISE(ABORT,'Billing history immutable'); END");
   Exec(c,t,$"CREATE TRIGGER {table}_no_delete BEFORE DELETE ON {table} BEGIN SELECT RAISE(ABORT,'Billing history retained'); END");
  }
  Exec(c,t,@"CREATE TRIGGER invoice_source_guard BEFORE INSERT ON invoice_draft_lines WHEN NEW.source_id IS NOT NULL AND (NOT EXISTS(SELECT 1 FROM work_submissions s JOIN work_reviews r ON r.submission_id=s.id JOIN work_billing_classifications b ON b.submission_id=s.id JOIN project_tasks k ON k.id=s.task_id JOIN project_phases h ON h.id=k.phase_id JOIN invoice_drafts d ON d.id=NEW.draft_id WHERE s.id=NEW.source_id AND s.kind=NEW.line_type AND r.decision='Approved' AND b.classification='Billable' AND h.project_id=d.project_id AND k.scope_change_id IS NULL) OR EXISTS(SELECT 1 FROM invoice_draft_lines l LEFT JOIN invoice_draft_decisions x ON x.draft_id=l.draft_id WHERE l.source_id=NEW.source_id AND coalesce(x.decision,'Draft')!='Cancelled')) BEGIN SELECT RAISE(ABORT,'Unavailable billing source'); END");
  Exec(c,t,"CREATE TRIGGER invoice_lines_closed BEFORE INSERT ON invoice_draft_lines WHEN EXISTS(SELECT 1 FROM invoice_draft_decisions WHERE draft_id=NEW.draft_id) BEGIN SELECT RAISE(ABORT,'Draft closed'); END");
  Exec(c,t,"PRAGMA user_version=19");
 }
 static void UpgradeToV20(SqliteConnection c,SqliteTransaction t)
 {
  foreach(var grant in new[]{"Billing.Invoices.Export","Reports.Operations","Reports.Financial"}){
   Exec(c,t,"INSERT OR IGNORE INTO permissions(code) VALUES($p)",("$p",grant));
   Exec(c,t,"INSERT OR IGNORE INTO role_permissions(role_id,permission_code,allowed) SELECT id,$p,1 FROM roles WHERE name='Administrator'",("$p",grant));
  }
  Exec(c,t,"PRAGMA user_version=20");
 }
 static void UpgradeToV21(SqliteConnection c,SqliteTransaction t)
 {
  Exec(c,t,"INSERT OR IGNORE INTO permissions(code) VALUES('Administration.RecoveryCopy')");
  Exec(c,t,"INSERT OR IGNORE INTO role_permissions(role_id,permission_code,allowed) SELECT id,'Administration.RecoveryCopy',1 FROM roles WHERE name='Administrator'");
  Exec(c,t,"PRAGMA user_version=21");
 }
 static void UpgradeToV22(SqliteConnection c,SqliteTransaction t)
 {
  // Identify the founding account only through the unique native Bootstrap audit record.
  // Never infer ownership from a username, a role grant, or the lowest user ID.
  Exec(c,t,@"CREATE VIEW owner_approval_eligible AS SELECT u.id AS owner_id FROM users u JOIN roles r ON r.id=u.role_id WHERE r.name='Administrator' AND u.active=1 AND u.force_password_change=0 AND u.id=(SELECT CASE WHEN count(*)=1 THEN max(actor_id) END FROM audit_events WHERE action='Bootstrap' AND entity='Firm' AND entity_id=1) AND NOT EXISTS(SELECT 1 FROM users v JOIN role_permissions rp ON rp.role_id=v.role_id WHERE v.id!=u.id AND v.active=1 AND rp.allowed=1 AND rp.permission_code IN ('Work.Review','Billing.Classify','Billing.Invoices.Approve'))");
  Exec(c,t,@"CREATE TABLE owner_approval_events(id INTEGER PRIMARY KEY,owner_id INTEGER NOT NULL REFERENCES users(id),enabled INTEGER NOT NULL CHECK(enabled IN (0,1)),reason TEXT NOT NULL CHECK(length(trim(reason)) BETWEEN 1 AND 1000),actor_id INTEGER REFERENCES users(id),recorded_utc TEXT NOT NULL,CHECK((enabled=1 AND actor_id IS NOT NULL AND actor_id=owner_id) OR enabled=0))");
  Exec(c,t,"CREATE TRIGGER owner_approval_event_immutable BEFORE UPDATE ON owner_approval_events BEGIN SELECT RAISE(ABORT,'Policy history immutable'); END");
  Exec(c,t,"CREATE TRIGGER owner_approval_event_no_delete BEFORE DELETE ON owner_approval_events BEGIN SELECT RAISE(ABORT,'Policy history retained'); END");
  Exec(c,t,"CREATE TRIGGER owner_approval_enable_guard BEFORE INSERT ON owner_approval_events WHEN NEW.enabled=1 AND NOT EXISTS(SELECT 1 FROM owner_approval_eligible WHERE owner_id=NEW.owner_id) BEGIN SELECT RAISE(ABORT,'Owner exception unavailable'); END");
  Exec(c,t,@"CREATE VIEW owner_approval_current AS SELECT e.id,e.owner_id FROM owner_approval_events e JOIN owner_approval_eligible o ON o.owner_id=e.owner_id WHERE e.id=(SELECT max(id) FROM owner_approval_events) AND e.enabled=1");
  // Suspension is sticky, even if a reviewer is subsequently disabled or their grant removed.
  var suspend=@" INSERT INTO owner_approval_events(owner_id,enabled,reason,actor_id,recorded_utc) SELECT e.owner_id,0,'Automatically suspended: owner eligibility changed or another active approval account exists',NULL,strftime('%Y-%m-%dT%H:%M:%fZ','now') FROM owner_approval_events e WHERE e.id=(SELECT max(id) FROM owner_approval_events) AND e.enabled=1 AND NOT EXISTS(SELECT 1 FROM owner_approval_eligible WHERE owner_id=e.owner_id); INSERT INTO audit_events(actor_id,action,entity,entity_id) SELECT NULL,'OwnerExceptionAutoSuspended','Firm',1 WHERE changes()=1;";
  foreach(var (suffix,eventSql) in new[]{("user_insert","AFTER INSERT ON users"),("user_update","AFTER UPDATE OF role_id,active,force_password_change ON users"),("grant_insert","AFTER INSERT ON role_permissions"),("grant_update","AFTER UPDATE ON role_permissions"),("grant_delete","AFTER DELETE ON role_permissions")}){
   Exec(c,t,$"CREATE TRIGGER owner_exception_{suffix} {eventSql} BEGIN {suspend} END");
  }
  foreach(var table in new[]{"work_reviews","work_billing_classifications","invoice_draft_decisions"})Exec(c,t,$"ALTER TABLE {table} ADD COLUMN owner_exception_event_id INTEGER REFERENCES owner_approval_events(id) ON DELETE RESTRICT");
  Exec(c,t,"DROP TRIGGER work_review_independent; DROP TRIGGER work_billing_approved_only");
  Exec(c,t,@"CREATE TRIGGER work_review_independent BEFORE INSERT ON work_reviews WHEN NOT EXISTS(SELECT 1 FROM work_submissions s WHERE s.id=NEW.submission_id AND ((s.submitted_by!=NEW.reviewed_by AND NEW.owner_exception_event_id IS NULL) OR (s.submitted_by=NEW.reviewed_by AND NEW.decision='Approved' AND EXISTS(SELECT 1 FROM owner_approval_current o WHERE o.owner_id=NEW.reviewed_by AND o.id=NEW.owner_exception_event_id)))) BEGIN SELECT RAISE(ABORT,'Independent review or authorized owner exception required'); END");
  Exec(c,t,@"CREATE TRIGGER work_billing_approved_only BEFORE INSERT ON work_billing_classifications WHEN NOT EXISTS(SELECT 1 FROM work_reviews r JOIN work_submissions s ON s.id=r.submission_id WHERE r.submission_id=NEW.submission_id AND r.decision='Approved' AND ((s.submitted_by!=NEW.classified_by AND NEW.owner_exception_event_id IS NULL) OR (s.submitted_by=NEW.classified_by AND EXISTS(SELECT 1 FROM owner_approval_current o WHERE o.owner_id=NEW.classified_by AND o.id=NEW.owner_exception_event_id)))) BEGIN SELECT RAISE(ABORT,'Independent approved work or authorized owner exception required'); END");
  Exec(c,t,@"CREATE TRIGGER invoice_approval_independent BEFORE INSERT ON invoice_draft_decisions WHEN NOT EXISTS(SELECT 1 FROM invoice_drafts d WHERE d.id=NEW.draft_id AND ((NEW.decision='Cancelled' AND NEW.owner_exception_event_id IS NULL) OR (NEW.decision='Approved' AND ((d.created_by!=NEW.actor_id AND NEW.owner_exception_event_id IS NULL) OR (d.created_by=NEW.actor_id AND EXISTS(SELECT 1 FROM owner_approval_current o WHERE o.owner_id=NEW.actor_id AND o.id=NEW.owner_exception_event_id)))))) BEGIN SELECT RAISE(ABORT,'Independent invoice approval or authorized owner exception required'); END");
  Exec(c,t,"PRAGMA user_version=22");
 }
 static void UpgradeToV23(SqliteConnection c,SqliteTransaction t)
 {
  foreach(var grant in new[]{"Compliance.View","Compliance.Record"}){
   Exec(c,t,"INSERT OR IGNORE INTO permissions(code) VALUES($p)",("$p",grant));
   Exec(c,t,"INSERT OR IGNORE INTO role_permissions(role_id,permission_code,allowed) SELECT id,$p,1 FROM roles WHERE name='Administrator'",("$p",grant));
  }
  Exec(c,t,@"CREATE TABLE project_conflict_reviews(id INTEGER PRIMARY KEY,project_id INTEGER NOT NULL REFERENCES projects(id) ON DELETE RESTRICT,proposal_hash TEXT NOT NULL CHECK(length(proposal_hash)=64),jurisdiction TEXT NOT NULL CHECK(length(trim(jurisdiction)) BETWEEN 1 AND 300),restricted_jurisdiction TEXT NOT NULL CHECK(restricted_jurisdiction IN ('No','Yes','Unknown')),public_duty_overlap TEXT NOT NULL CHECK(public_duty_overlap IN ('No','Yes','Unknown')),public_resources TEXT NOT NULL CHECK(public_resources IN ('No','Yes','Unknown')),other_conflict TEXT NOT NULL CHECK(other_conflict IN ('No','Yes','Unknown')),evidence TEXT NOT NULL CHECK(length(trim(evidence)) BETWEEN 1 AND 4000),reviewed_by INTEGER NOT NULL REFERENCES users(id),reviewed_utc TEXT NOT NULL)");
  Exec(c,t,"CREATE INDEX ix_conflict_project ON project_conflict_reviews(project_id,id)");
  Exec(c,t,"CREATE TRIGGER conflict_immutable BEFORE UPDATE ON project_conflict_reviews BEGIN SELECT RAISE(ABORT,'Conflict review immutable'); END");
  Exec(c,t,"CREATE TRIGGER conflict_no_delete BEFORE DELETE ON project_conflict_reviews BEGIN SELECT RAISE(ABORT,'Conflict review retained'); END");
  Exec(c,t,@"CREATE VIEW project_conflict_current AS SELECT r.*,CASE WHEN r.restricted_jurisdiction='No' AND r.public_duty_overlap='No' AND r.public_resources='No' AND r.other_conflict='No' THEN 'Clear' ELSE 'Hold' END AS disposition FROM project_conflict_reviews r WHERE r.id=(SELECT max(x.id) FROM project_conflict_reviews x WHERE x.project_id=r.project_id)");
  Exec(c,t,@"CREATE VIEW project_conflict_cleared AS SELECT p.id AS project_id,r.id AS review_id FROM projects p JOIN engagements e ON e.id=p.engagement_id JOIN proposals s ON s.id=e.proposal_id JOIN project_conflict_current r ON r.project_id=p.id AND r.proposal_hash=s.snapshot_hash WHERE r.disposition='Clear' AND s.state='Accepted'");
  Exec(c,t,@"CREATE TRIGGER conflict_start_guard BEFORE INSERT ON project_start_authorizations WHEN NOT EXISTS(SELECT 1 FROM project_conflict_cleared WHERE project_id=NEW.project_id) BEGIN SELECT RAISE(ABORT,'Current conflict review required'); END");
  Exec(c,t,@"CREATE TRIGGER conflict_task_start_guard BEFORE UPDATE OF state ON project_tasks WHEN NEW.state='InProgress' AND NOT EXISTS(SELECT 1 FROM project_phases h JOIN project_conflict_cleared r ON r.project_id=h.project_id WHERE h.id=OLD.phase_id) BEGIN SELECT RAISE(ABORT,'Current conflict review required'); END");
  Exec(c,t,@"CREATE TRIGGER conflict_scope_guard BEFORE INSERT ON project_scope_decisions WHEN NEW.decision='Authorized' AND NOT EXISTS(SELECT 1 FROM project_scope_changes s JOIN project_conflict_cleared r ON r.project_id=s.project_id WHERE s.id=NEW.change_id) BEGIN SELECT RAISE(ABORT,'Current conflict review required'); END");
  Exec(c,t,"PRAGMA user_version=23");
 }
 static void UpgradeToV24(SqliteConnection c,SqliteTransaction t)
 {
  Exec(c,t,@"CREATE TABLE proposal_conflict_reviews(id INTEGER PRIMARY KEY,proposal_id INTEGER NOT NULL REFERENCES proposals(id) ON DELETE RESTRICT,revision INTEGER NOT NULL CHECK(revision>=1),snapshot_hash TEXT NOT NULL CHECK(length(snapshot_hash)=64),jurisdiction TEXT NOT NULL CHECK(length(trim(jurisdiction)) BETWEEN 1 AND 300),restricted_jurisdiction TEXT NOT NULL CHECK(restricted_jurisdiction IN ('No','Yes','Unknown')),public_duty_overlap TEXT NOT NULL CHECK(public_duty_overlap IN ('No','Yes','Unknown')),public_resources TEXT NOT NULL CHECK(public_resources IN ('No','Yes','Unknown')),other_conflict TEXT NOT NULL CHECK(other_conflict IN ('No','Yes','Unknown')),evidence TEXT NOT NULL CHECK(length(trim(evidence)) BETWEEN 1 AND 4000),reviewed_by INTEGER NOT NULL REFERENCES users(id),reviewed_utc TEXT NOT NULL)");
  Exec(c,t,"CREATE INDEX ix_proposal_conflict ON proposal_conflict_reviews(proposal_id,id)");
  Exec(c,t,"CREATE TRIGGER proposal_conflict_immutable BEFORE UPDATE ON proposal_conflict_reviews BEGIN SELECT RAISE(ABORT,'Proposal conflict review immutable'); END");
  Exec(c,t,"CREATE TRIGGER proposal_conflict_no_delete BEFORE DELETE ON proposal_conflict_reviews BEGIN SELECT RAISE(ABORT,'Proposal conflict review retained'); END");
  Exec(c,t,@"CREATE VIEW proposal_conflict_current AS SELECT r.*,CASE WHEN r.restricted_jurisdiction='No' AND r.public_duty_overlap='No' AND r.public_resources='No' AND r.other_conflict='No' THEN 'Clear' ELSE 'Hold' END AS disposition FROM proposal_conflict_reviews r WHERE r.id=(SELECT max(x.id) FROM proposal_conflict_reviews x WHERE x.proposal_id=r.proposal_id)");
  Exec(c,t,@"CREATE TRIGGER proposal_conflict_issue_guard BEFORE UPDATE OF state ON proposals WHEN OLD.state='Draft' AND NEW.state='Issued' AND NOT EXISTS(SELECT 1 FROM proposal_conflict_current r WHERE r.proposal_id=OLD.id AND r.revision=OLD.revision AND r.snapshot_hash=NEW.snapshot_hash AND r.disposition='Clear') BEGIN SELECT RAISE(ABORT,'Current proposal conflict review required'); END");
  Exec(c,t,"PRAGMA user_version=24");
 }
 SqliteConnection Open(){var c=new SqliteConnection(connectionString);c.Open();return c;}
 static SqliteCommand Command(SqliteConnection c,SqliteTransaction? t,string sql,params (string,object?)[] args){var q=c.CreateCommand();q.Transaction=t;q.CommandText=sql;foreach(var (key,value) in args)q.Parameters.AddWithValue(key,value??DBNull.Value);return q;}
 static object? Scalar(SqliteConnection c,SqliteTransaction? t,string sql,params (string,object?)[] args){using var q=Command(c,t,sql,args);return q.ExecuteScalar();}
 static int Exec(SqliteConnection c,SqliteTransaction? t,string sql,params (string,object?)[] args){using var q=Command(c,t,sql,args);return q.ExecuteNonQuery();}
 static long Last(SqliteConnection c,SqliteTransaction t)=>Convert.ToInt64(Scalar(c,t,"SELECT last_insert_rowid()"));
 public bool IsInitialized(){using var c=Open();return Convert.ToInt64(Scalar(c,null,"SELECT count(*) FROM firms"))==1;}
 public string FirmName(){using var c=Open();return Convert.ToString(Scalar(c,null,"SELECT name FROM firms WHERE id=1"))??"";}
 public void Bootstrap(string firm,string username,string password,string prefix){firm=Validation.Text(firm,150,"Company",true);var normalized=Validation.Username(username);prefix=Validation.Text(prefix,10,"Client prefix",true).ToUpperInvariant();if(prefix.Any(x=>!char.IsAsciiLetterOrDigit(x)))throw new ArgumentException("Prefix must be alphanumeric.");var hash=PasswordCodec.Hash(password);using var c=Open();using var t=c.BeginTransaction(deferred:false);if(Convert.ToInt64(Scalar(c,t,"SELECT count(*) FROM firms"))!=0)throw new ArgumentException("Company already initialized.");Exec(c,t,"INSERT INTO firms(id,name,client_prefix) VALUES(1,$n,$p)",("$n",firm),("$p",prefix));Exec(c,t,"INSERT INTO roles(id,name) VALUES(1,'Administrator')");Exec(c,t,"INSERT OR IGNORE INTO roles(name) VALUES('Engineer'),('Administrative'),('Project Manager'),('Financial / Accounting')");foreach(var grant in Grants){Exec(c,t,"INSERT OR IGNORE INTO permissions(code) VALUES($g)",("$g",grant));Exec(c,t,"INSERT INTO role_permissions VALUES(1,$g,1)",("$g",grant));}Exec(c,t,"INSERT INTO users(username,normalized_username,password_hash,role_id,active) VALUES($u,$n,$h,1,1)",("$u",username.Trim()),("$n",normalized),("$h",hash));var id=Last(c,t);Exec(c,t,"INSERT INTO sequences VALUES('Client',1)");Audit(c,t,id,"Bootstrap","Firm",1);t.Commit();}
 public bool Login(string username,string password){Logout();string n;try{n=Validation.Username(username);}catch(ArgumentException){return false;}using var c=Open();using var t=c.BeginTransaction(deferred:false);long id,locked,attempts,revision;string hash;bool active,force;using(var q=Command(c,t,"SELECT id,password_hash,active,failed_attempts,locked_until,force_password_change,credential_revision FROM users WHERE normalized_username=$n",("$n",n)))using(var r=q.ExecuteReader()){if(!r.Read())return false;id=r.GetInt64(0);hash=r.GetString(1);active=r.GetInt64(2)==1;attempts=r.GetInt64(3);locked=r.GetInt64(4);force=r.GetInt64(5)==1;revision=r.GetInt64(6);}var now=DateTimeOffset.UtcNow.ToUnixTimeSeconds();if(!active||force||locked>now)return false;if(!PasswordCodec.Verify(password,hash)){attempts=locked>0&&locked<=now?1:attempts+1;Exec(c,t,"UPDATE users SET failed_attempts=$a,locked_until=$l WHERE id=$i",("$a",attempts),("$l",attempts>=5?now+300:0),("$i",id));Audit(c,t,id,"LoginFailed","User",id);t.Commit();return false;}Exec(c,t,"UPDATE users SET failed_attempts=0,locked_until=0 WHERE id=$i",("$i",id));Audit(c,t,id,"Login","User",id);t.Commit();userId=id;sessionCredentialRevision=revision;expires=DateTimeOffset.UtcNow.AddHours(8);return true;}
 public void Logout(){userId=null;sessionCredentialRevision=0;expires=default;sessionGeneration++;}
 long Require(SqliteConnection c,SqliteTransaction t,string permission){if(userId is not long id||DateTimeOffset.UtcNow>=expires)throw new UnauthorizedAccessException("Sign in again.");var allowed=Convert.ToInt64(Scalar(c,t,"SELECT count(*) FROM users u JOIN role_permissions rp ON rp.role_id=u.role_id WHERE u.id=$i AND u.active=1 AND u.force_password_change=0 AND u.credential_revision=$v AND rp.permission_code=$p AND rp.allowed=1",("$i",id),("$p",permission),("$v",sessionCredentialRevision)));if(allowed!=1)throw new UnauthorizedAccessException("Permission denied.");return id;}
 public bool Can(string permission){using var c=Open();using var t=c.BeginTransaction();try{Require(c,t,permission);return true;}catch(UnauthorizedAccessException){return false;}}
 // Accounting permissions are separate from CRM permissions and checked for each operation.
 public bool CanTestAccountingConnection()=>Can("Accounting.Connection.Test");
 public bool CanManageAccountingConnection()=>Can("Accounting.Connection.Manage");
 public async Task<AccountingProbeResult> TestAccountingConnectionAsync(string endpoint,string secret,CancellationToken cancellationToken=default)
 {
  var actor=userId;
  if(!CanTestAccountingConnection())throw new UnauthorizedAccessException("Accounting connection diagnostic requires the accounting test permission.");
  var result=await new ManagerDesktopProbe().TestAsync(endpoint,secret,cancellationToken);
  if(userId!=actor||!CanTestAccountingConnection())throw new UnauthorizedAccessException("Accounting diagnostic session ended.");
  return result;
 }
 static void Audit(SqliteConnection c,SqliteTransaction t,long actor,string action,string entity,long id)=>Exec(c,t,"INSERT INTO audit_events(actor_id,action,entity,entity_id) VALUES($a,$x,$e,$i)",("$a",actor),("$x",action),("$e",entity),("$i",id));
 public List<Client> SearchClients(string search){using var c=Open();using var t=c.BeginTransaction();Require(c,t,"Clients.View");var result=new List<Client>();using var q=Command(c,t,"SELECT id,client_no,client_type,company_name,first_name,last_name,email,phone,address1,address2,city,state,zip,notes,status FROM clients WHERE instr(lower(client_no||' '||company_name||' '||first_name||' '||last_name||' '||email),lower($s))>0 OR $s='' ORDER BY client_no,id",("$s",Validation.Text(search,150,"Search")));using var r=q.ExecuteReader();while(r.Read())result.Add(new Client(r.GetInt64(0),r.GetString(1),r.GetString(2),r.GetString(3),r.GetString(4),r.GetString(5),r.GetString(6),Validation.PhoneForDisplay(r.GetString(7)),r.GetString(8),r.GetString(9),r.GetString(10),r.GetString(11),r.GetString(12),r.GetString(13),r.GetString(14)));return result;}
 public long SaveClient(Client x){using var c=Open();using var t=c.BeginTransaction(deferred:false);var actor=Require(c,t,"Clients.Edit");Require(c,t,"Clients.View");if(!Validation.ClientTypes.Contains(x.Type)||x.Status is not ("Active" or "Inactive"))throw new ArgumentException("Invalid client type or status.");var company=Validation.Text(x.Company,150,"Company");var first=Validation.Text(x.First,60,"First name");var last=Validation.Text(x.Last,60,"Last name");if(company.Length+first.Length+last.Length==0)throw new ArgumentException("Client name required.");var p=new List<(string,object?)>{("$t",x.Type),("$co",company),("$f",first),("$l",last),("$e",Validation.Text(x.Email,150,"Email")),("$p",Validation.Phone(x.Phone)),("$a1",Validation.Text(x.Address1,150,"Address 1")),("$a2",Validation.Text(x.Address2,150,"Address 2")),("$city",Validation.Text(x.City,80,"City")),("$st",Validation.Text(x.State,30,"State")),("$z",Validation.Text(x.Zip,15,"ZIP")),("$n",Validation.Text(x.Notes,20000,"Notes")),("$s",x.Status)};long id=x.Id;if(id==0){var next=Convert.ToInt64(Scalar(c,t,"SELECT next_value FROM sequences WHERE name='Client'"));var prefix=Convert.ToString(Scalar(c,t,"SELECT client_prefix FROM firms WHERE id=1"));p.Add(("$no",$"{prefix}-{next:D6}"));Exec(c,t,"INSERT INTO clients(client_no,client_type,company_name,first_name,last_name,email,phone,address1,address2,city,state,zip,notes,status) VALUES($no,$t,$co,$f,$l,$e,$p,$a1,$a2,$city,$st,$z,$n,$s)",p.ToArray());id=Last(c,t);Exec(c,t,"UPDATE sequences SET next_value=next_value+1 WHERE name='Client'");}else{p.Add(("$id",id));if(Exec(c,t,"UPDATE clients SET client_type=$t,company_name=$co,first_name=$f,last_name=$l,email=$e,phone=$p,address1=$a1,address2=$a2,city=$city,state=$st,zip=$z,notes=$n,status=$s WHERE id=$id",p.ToArray())!=1)throw new ArgumentException("Client not found.");}Audit(c,t,actor,x.Id==0?"Create":"Update","Client",id);t.Commit();return id;}
 public List<Contact> Contacts(long clientId){using var c=Open();using var t=c.BeginTransaction();Require(c,t,"Clients.View");Require(c,t,"Contacts.View");var list=new List<Contact>();using var q=Command(c,t,"SELECT id,client_id,first_name,last_name,title,email,phone,is_primary FROM contacts WHERE client_id=$i ORDER BY is_primary DESC,last_name,first_name,id",("$i",clientId));using var r=q.ExecuteReader();while(r.Read())list.Add(new Contact(r.GetInt64(0),r.GetInt64(1),r.GetString(2),r.GetString(3),r.GetString(4),r.GetString(5),Validation.PhoneForDisplay(r.GetString(6)),r.GetInt64(7)==1));return list;}
 public long SaveContact(Contact x){using var c=Open();using var t=c.BeginTransaction(deferred:false);var actor=Require(c,t,"Contacts.Edit");Require(c,t,"Contacts.View");Require(c,t,"Clients.View");if(Convert.ToString(Scalar(c,t,"SELECT status FROM clients WHERE id=$i",("$i",x.ClientId)))!="Active")throw new ArgumentException("Select an active client.");var f=Validation.Text(x.First,60,"First name");var l=Validation.Text(x.Last,60,"Last name");if(f.Length+l.Length==0)throw new ArgumentException("Contact name required.");var p=new List<(string,object?)>{("$c",x.ClientId),("$f",f),("$l",l),("$t",Validation.Text(x.Title,80,"Title")),("$e",Validation.Text(x.Email,150,"Email")),("$p",Validation.Phone(x.Phone)),("$b",x.Primary?1:0)};if(x.Primary)Exec(c,t,"UPDATE contacts SET is_primary=0 WHERE client_id=$c AND is_primary=1",("$c",x.ClientId));long id=x.Id;if(id==0){Exec(c,t,"INSERT INTO contacts(client_id,first_name,last_name,title,email,phone,is_primary) VALUES($c,$f,$l,$t,$e,$p,$b)",p.ToArray());id=Last(c,t);}else{p.Add(("$i",id));if(Exec(c,t,"UPDATE contacts SET first_name=$f,last_name=$l,title=$t,email=$e,phone=$p,is_primary=$b WHERE id=$i AND client_id=$c",p.ToArray())!=1)throw new ArgumentException("Contact not found for selected client.");}Audit(c,t,actor,x.Id==0?"Create":"Update","Contact",id);t.Commit();return id;}
}
