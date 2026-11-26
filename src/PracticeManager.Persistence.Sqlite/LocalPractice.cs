using Microsoft.Data.Sqlite;
using PracticeManager.Core;
namespace PracticeManager.Persistence;
// Local development edition. No caller-supplied user identity or role; recheck active grants in every transaction.
public sealed partial class LocalPractice
{
 readonly string connectionString; long? userId; DateTimeOffset expires; long sessionGeneration;
 static readonly string[] Grants=["Clients.View","Clients.Edit","Contacts.View","Contacts.Edit","Accounting.Connection.Test","Accounting.Connection.Manage","Accounting.Customers.Map","Accounting.Identity.Manage","Accounting.Directory.Sync","Accounting.Posting.Rehearse","Accounting.Posting.RehearseApprove","Accounting.WireDraft.Prepare","Accounting.WireDraft.Approve","Accounting.WireDraft.Rehearse"];
 public LocalPractice(string path){path=Path.GetFullPath(path);Directory.CreateDirectory(Path.GetDirectoryName(path)!);connectionString=new SqliteConnectionStringBuilder{DataSource=path,ForeignKeys=true,DefaultTimeout=10,Pooling=false}.ToString();using var c=Open();using var t=c.BeginTransaction(deferred:false);var version=Convert.ToInt64(Scalar(c,t,"PRAGMA user_version"));if(version==0){if(Convert.ToInt64(Scalar(c,t,"SELECT count(*) FROM sqlite_master WHERE name NOT LIKE 'sqlite_%'"))!=0)throw new InvalidOperationException("Unknown database; refusing initialization.");using var stream=typeof(LocalPractice).Assembly.GetManifestResourceStream("PracticeManager.schema.sql")!;using var reader=new StreamReader(stream);Exec(c,t,reader.ReadToEnd());version=1;}if(version==1){UpgradeToV2(c,t);version=2;}if(version==2){UpgradeToV3(c,t);version=3;}if(version==3){UpgradeToV4(c,t);version=4;}if(version==4){UpgradeToV5(c,t);version=5;}if(version==5){UpgradeToV6(c,t);version=6;}if(version==6){UpgradeToV7(c,t);version=7;}if(version==7){UpgradeToV8(c,t);version=8;}if(version==8){UpgradeToV9(c,t);version=9;}if(version==9){UpgradeToV10(c,t);version=10;}if(version==10){UpgradeToV11(c,t);version=11;}if(version!=11)throw new InvalidOperationException("Unsupported schema version.");t.Commit();}
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
 SqliteConnection Open(){var c=new SqliteConnection(connectionString);c.Open();return c;}
 static SqliteCommand Command(SqliteConnection c,SqliteTransaction? t,string sql,params (string,object?)[] args){var q=c.CreateCommand();q.Transaction=t;q.CommandText=sql;foreach(var (key,value) in args)q.Parameters.AddWithValue(key,value??DBNull.Value);return q;}
 static object? Scalar(SqliteConnection c,SqliteTransaction? t,string sql,params (string,object?)[] args){using var q=Command(c,t,sql,args);return q.ExecuteScalar();}
 static int Exec(SqliteConnection c,SqliteTransaction? t,string sql,params (string,object?)[] args){using var q=Command(c,t,sql,args);return q.ExecuteNonQuery();}
 static long Last(SqliteConnection c,SqliteTransaction t)=>Convert.ToInt64(Scalar(c,t,"SELECT last_insert_rowid()"));
 public bool IsInitialized(){using var c=Open();return Convert.ToInt64(Scalar(c,null,"SELECT count(*) FROM firms"))==1;}
 public string FirmName(){using var c=Open();return Convert.ToString(Scalar(c,null,"SELECT name FROM firms WHERE id=1"))??"";}
 public void Bootstrap(string firm,string username,string password,string prefix){firm=Validation.Text(firm,150,"Company",true);var normalized=Validation.Username(username);prefix=Validation.Text(prefix,10,"Client prefix",true).ToUpperInvariant();if(prefix.Any(x=>!char.IsAsciiLetterOrDigit(x)))throw new ArgumentException("Prefix must be alphanumeric.");var hash=PasswordCodec.Hash(password);using var c=Open();using var t=c.BeginTransaction(deferred:false);if(Convert.ToInt64(Scalar(c,t,"SELECT count(*) FROM firms"))!=0)throw new ArgumentException("Company already initialized.");Exec(c,t,"INSERT INTO firms(id,name,client_prefix) VALUES(1,$n,$p)",("$n",firm),("$p",prefix));Exec(c,t,"INSERT INTO roles(id,name) VALUES(1,'Administrator')");foreach(var grant in Grants){Exec(c,t,"INSERT OR IGNORE INTO permissions(code) VALUES($g)",("$g",grant));Exec(c,t,"INSERT INTO role_permissions VALUES(1,$g,1)",("$g",grant));}Exec(c,t,"INSERT INTO users(username,normalized_username,password_hash,role_id,active) VALUES($u,$n,$h,1,1)",("$u",username.Trim()),("$n",normalized),("$h",hash));var id=Last(c,t);Exec(c,t,"INSERT INTO sequences VALUES('Client',1)");Audit(c,t,id,"Bootstrap","Firm",1);t.Commit();}
 public bool Login(string username,string password){Logout();string n;try{n=Validation.Username(username);}catch(ArgumentException){return false;}using var c=Open();using var t=c.BeginTransaction(deferred:false);long id,locked,attempts;string hash;bool active,force;using(var q=Command(c,t,"SELECT id,password_hash,active,failed_attempts,locked_until,force_password_change FROM users WHERE normalized_username=$n",("$n",n)))using(var r=q.ExecuteReader()){if(!r.Read())return false;id=r.GetInt64(0);hash=r.GetString(1);active=r.GetInt64(2)==1;attempts=r.GetInt64(3);locked=r.GetInt64(4);force=r.GetInt64(5)==1;}var now=DateTimeOffset.UtcNow.ToUnixTimeSeconds();if(!active||force||locked>now)return false;if(!PasswordCodec.Verify(password,hash)){attempts=locked>0&&locked<=now?1:attempts+1;Exec(c,t,"UPDATE users SET failed_attempts=$a,locked_until=$l WHERE id=$i",("$a",attempts),("$l",attempts>=5?now+300:0),("$i",id));Audit(c,t,id,"LoginFailed","User",id);t.Commit();return false;}Exec(c,t,"UPDATE users SET failed_attempts=0,locked_until=0 WHERE id=$i",("$i",id));Audit(c,t,id,"Login","User",id);t.Commit();userId=id;expires=DateTimeOffset.UtcNow.AddHours(8);return true;}
 public void Logout(){userId=null;expires=default;sessionGeneration++;}
 long Require(SqliteConnection c,SqliteTransaction t,string permission){if(userId is not long id||DateTimeOffset.UtcNow>=expires)throw new UnauthorizedAccessException("Sign in again.");var allowed=Convert.ToInt64(Scalar(c,t,"SELECT count(*) FROM users u JOIN role_permissions rp ON rp.role_id=u.role_id WHERE u.id=$i AND u.active=1 AND u.force_password_change=0 AND rp.permission_code=$p AND rp.allowed=1",("$i",id),("$p",permission)));if(allowed!=1)throw new UnauthorizedAccessException("Permission denied.");return id;}
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
