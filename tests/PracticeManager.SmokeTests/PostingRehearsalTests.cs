using System.Text.Json;
using Microsoft.Data.Sqlite;
using PracticeManager.Core;
using PracticeManager.Persistence;
internal static class PostingRehearsalTests
{
 public static async Task Run(string path,string pw,Action<bool,string> check)
 {
  var s=new LocalPractice(path);s.Bootstrap("Synthetic Rehearsal Firm","admin",pw,"CL");check(s.Login("admin",pw)&&s.CanRehearsePosting(),"separate rehearsal permission seeded");
  var marker=Guid.NewGuid();var customer=Guid.NewGuid();var account=Guid.NewGuid();
  var i=new SyntheticPostingIntent(Guid.NewGuid(),RehearsalKind.Invoice,customer,account,null,100m,new DateOnly(2026,11,25),"USD");
  check(i.Fingerprint()==(i with{Amount=100.00m}).Fingerprint(),"canonical decimal fingerprint");
  check(i.Fingerprint()!=(i with{Amount=101m}).Fingerprint(),"changed payload fingerprint");
  try{(i with{Amount=0.001m}).Fingerprint();throw new Exception("FAIL: subcent intent");}catch(ArgumentException){check(true,"subcent amount rejected");}
  try{(i with{Kind=RehearsalKind.Receipt}).Fingerprint();throw new Exception("FAIL: missing allocation");}catch(ArgumentException){check(true,"receipt requires allocation");}
  var payment=new SyntheticPostingIntent(Guid.NewGuid(),RehearsalKind.ExpensePayment,Guid.Empty,Guid.NewGuid(),null,20m,new DateOnly(2026,11,25),"USD",Guid.NewGuid(),"Synthetic Office Supplies Vendor");
  check(payment.Fingerprint()==(payment with{Amount=20.00m}).Fingerprint(),"expense canonical fingerprint");
  check(payment.Fingerprint()!=(payment with{VendorContact="Different synthetic vendor"}).Fingerprint(),"expense vendor bound to fingerprint");
  check(payment.Fingerprint()!=(payment with{CashAccountKey=Guid.NewGuid()}).Fingerprint(),"expense bank bound to fingerprint");
  void Invalid(SyntheticPostingIntent value,string name){try{value.Fingerprint();}catch(ArgumentException){check(true,name);return;}throw new Exception("FAIL: "+name);}
  Invalid(payment with{CashAccountKey=null},"expense requires bank");
  Invalid(payment with{CashAccountKey=payment.AccountKey},"expense bank and ledger account must differ");
  Invalid(payment with{VendorContact=" "},"expense requires vendor contact");
  Invalid(payment with{CustomerKey=customer},"expense must not impersonate customer");
  Invalid(payment with{AllocatedInvoiceKey=i.OperationKey},"direct expense cannot allocate sales invoice");
  Invalid(i with{VendorContact="Synthetic vendor"},"legacy intent cannot silently ignore expense fields");
  check(s.ReserveSyntheticPosting(i).Outcome=="FixtureScopeRequired","reservation requires configured scope");
  check(s.ReadPostingRehearsalStatus()==new PostingRehearsalStatus(null,0,0,0,0,0),"initial offline safety status");
  string Reply(string req)=>req.StartsWith("GET /api2/text-custom-fields?")?JsonSerializer.Serialize(new{business=new{name="CRC ACCOUNTING TEST"},skip=0,pageSize=2,totalRecords=1,textCustomFields=new[]{new{key=marker,name=BusinessMarker.TestName}}}):JsonSerializer.Serialize(new{business=new{name="CRC ACCOUNTING TEST"},skip=0,pageSize=20,totalRecords=1,customers=new[]{new{key=customer,name="CRC Test Customer 001",status="Paid"}}});
  var key="synthetic-key";
  await AccountingProbeTests.WithServer(200,"{}",async(url,_)=>{
   check((await s.SaveAccountingProfileAsync(url,key)).Success,"rehearsal test profile saved");
   check((await s.EnrollOrVerifyMarkerAsync(true)).Success,"rehearsal test marker enrolled");
   check((await s.SyncCustomerDirectoryAsync()).Outcome=="Synchronized","rehearsal test directory synchronized");
  },6,responseFor:Reply);
  var peer=new LocalPractice(path);check(peer.Login("admin",pw),"second session on same local database");
  var reserves=await Task.WhenAll(Task.Run(()=>s.ReserveSyntheticPosting(i)),Task.Run(()=>peer.ReserveSyntheticPosting(i)));
  check(reserves.Count(r=>r.Outcome=="Reserved")==1&&reserves.Count(r=>r.Outcome=="AlreadyRecorded")==1,"concurrent reservations create exactly one durable intent");
  check(s.ReserveSyntheticPosting(i with{Amount=101m}).Outcome=="IntentConflict","changed payload cannot reuse operation");
  check(s.BeginSyntheticPosting(i).Outcome=="ApprovalRequired","unapproved reservation cannot begin");
  check(s.ApproveSyntheticPosting(i with{Amount=101m}).Outcome=="IntentConflict","approval cannot change reserved payload");
  var approvals=await Task.WhenAll(Task.Run(()=>s.ApproveSyntheticPosting(i)),Task.Run(()=>peer.ApproveSyntheticPosting(i)));
  check(approvals.Count(r=>r.Outcome=="Approved")==1&&approvals.Count(r=>r.Outcome=="AlreadyApproved")==1,"concurrent approval recorded once");
  var begins=await Task.WhenAll(Task.Run(()=>s.BeginSyntheticPosting(i)),Task.Run(()=>peer.BeginSyntheticPosting(i)));
  check(begins.Count(r=>r.Outcome=="Started")==1&&begins.Count(r=>r.AttemptKey.HasValue)==1,"only one concurrent begin returns attempt key");
  var attempt=begins.Single(r=>r.AttemptKey.HasValue).AttemptKey!.Value;
  check(peer.ReadSyntheticPostingState(i.OperationKey).State=="InFlight"&&peer.ReadSyntheticPostingState(i.OperationKey).AttemptKey==null,"read does not reissue attempt");
  check(peer.BeginSyntheticPosting(i).Outcome=="DispatchNotAllowed","inflight retry blocked");
  check(peer.CancelSyntheticPosting(i).Outcome=="CancellationNotAllowed","cannot cancel inflight as unsent");
  check(peer.FinishSyntheticPosting(i,Guid.NewGuid(),SimulatedOutcome.Confirmed,Guid.NewGuid()).Outcome=="DispatchNotAllowed","wrong attempt rejected");
  var external=Guid.NewGuid();
  check(s.FinishSyntheticPosting(i,attempt,SimulatedOutcome.Confirmed,external).Outcome=="SimulatedConfirmed","simulated confirmation recorded");
  check(s.BeginSyntheticPosting(i).Outcome=="DispatchNotAllowed"&&s.ReserveSyntheticPosting(i).State=="SimulatedConfirmed","confirmed operation cannot dispatch again");
  check(s.ReserveSyntheticPosting(payment with{OperationKey=i.OperationKey}).Outcome=="IntentConflict","operation key cannot cross transaction kinds");
  check(s.ReserveSyntheticPosting(payment).Outcome=="Reserved","expense rehearsal reserved");
  check(peer.ReserveSyntheticPosting(payment with{VendorContact="Changed vendor"}).Outcome=="IntentConflict","expense vendor change cannot reuse key");
  check(s.BeginSyntheticPosting(payment).Outcome=="ApprovalRequired","unapproved expense blocked");
  check(s.ApproveSyntheticPosting(payment).Outcome=="Approved","explicit expense approval");
  using(var db=new SqliteConnection($"Data Source={path}")){db.Open();using var q=db.CreateCommand();q.CommandText="UPDATE role_permissions SET allowed=0 WHERE permission_code='Accounting.Posting.RehearseApprove'";q.ExecuteNonQuery();}
  check(!s.CanApprovePostingRehearsal()&&s.BeginSyntheticPosting(payment).Outcome=="ApprovalRequired","revoked approver permission blocks dispatch");
  try{peer.ApproveSyntheticPosting(payment);throw new Exception("FAIL: revoked approval permission");}catch(UnauthorizedAccessException){check(true,"approval permission separately enforced");}
  using(var db=new SqliteConnection($"Data Source={path}")){db.Open();using var q=db.CreateCommand();q.CommandText="UPDATE role_permissions SET allowed=1 WHERE permission_code='Accounting.Posting.RehearseApprove'";q.ExecuteNonQuery();}
  var expenseBegins=await Task.WhenAll(Task.Run(()=>s.BeginSyntheticPosting(payment)),Task.Run(()=>peer.BeginSyntheticPosting(payment)));
  check(expenseBegins.Count(r=>r.Outcome=="Started")==1&&expenseBegins.Count(r=>r.AttemptKey.HasValue)==1,"only one expense rehearsal begin");
  var expenseAttempt=expenseBegins.Single(r=>r.AttemptKey.HasValue).AttemptKey!.Value;
  check(s.FinishSyntheticPosting(payment,expenseAttempt,SimulatedOutcome.Confirmed,external).Outcome=="SimulatedExternalConflict","expense cannot reuse simulated invoice ID");
  check(s.FinishSyntheticPosting(payment,expenseAttempt,SimulatedOutcome.Confirmed,Guid.NewGuid()).Outcome=="SimulatedConfirmed","expense rehearsal confirmed offline");
  check(peer.BeginSyntheticPosting(payment).Outcome=="DispatchNotAllowed","expense repeat blocked");
  var c=i with{OperationKey=Guid.NewGuid()};
  check(s.ReserveSyntheticPosting(c).Outcome=="Reserved"&&s.CancelSyntheticPosting(c).Outcome=="Cancelled","only reserved intent may cancel");
  check(s.BeginSyntheticPosting(c).Outcome=="DispatchNotAllowed","cancelled key permanently consumed");
  check(s.ApproveSyntheticPosting(c).Outcome=="ApprovalNotAllowed","cancelled key cannot be approved");
  var second=payment with{OperationKey=Guid.NewGuid()};
  check(s.ReserveSyntheticPosting(second).Outcome=="Reserved","second operation reserved");
  check(s.ApproveSyntheticPosting(second).Outcome=="Approved","second expense approved");
  var secondAttempt=s.BeginSyntheticPosting(second).AttemptKey!.Value;
  check(s.FinishSyntheticPosting(second,secondAttempt,SimulatedOutcome.Confirmed,external).Outcome=="SimulatedExternalConflict","same simulated external ID cannot map twice");
  check(s.FinishSyntheticPosting(second,secondAttempt,SimulatedOutcome.Uncertain).Outcome=="Uncertain","ambiguous outcome durably quarantined");
  var third=i with{OperationKey=Guid.NewGuid()};
  check(s.ReserveSyntheticPosting(third).Outcome=="Reserved"&&s.BeginSyntheticPosting(third).Outcome=="UnresolvedRehearsal","uncertainty blocks later dispatch");
  check(s.ReadPostingRehearsalStatus()==new PostingRehearsalStatus(null,1,0,1,2,1),"status accurately counts simulated intent states");
  s.RemoveAccountingProfile();
  check(s.ReadSyntheticPostingState(second.OperationKey).State=="Uncertain","profile removal retains intent history");
  await AccountingProbeTests.WithServer(200,"{}",async(url,_)=>{
   check((await s.SaveAccountingProfileAsync(url,key)).Success,"same-marker replacement profile saved");
   check((await s.SyncCustomerDirectoryAsync()).Outcome=="Synchronized","replacement scope synchronized");
  },6,responseFor:Reply);
  var reopened=new LocalPractice(path);check(reopened.Login("admin",pw),"journal reopened");
  check(reopened.ReserveSyntheticPosting(i).Outcome=="AlreadyRecorded"&&reopened.BeginSyntheticPosting(second).Outcome=="DispatchNotAllowed","restart and profile rotation cannot replay");
  check(reopened.ReserveSyntheticPosting(payment).Outcome=="AlreadyRecorded"&&reopened.BeginSyntheticPosting(payment).Outcome=="DispatchNotAllowed","expense replay blocked after restart");
  check(reopened.BeginSyntheticPosting(third).Outcome=="UnresolvedRehearsal","unresolved state persists across restart");
  check(reopened.DeclareAccountingLifecycleHold(LifecycleHoldReason.Restore).Outcome=="LifecycleHold","restore declaration creates sticky hold");
  check(s.IsAccountingLifecycleHeld()&&s.ReserveSyntheticPosting(i with{OperationKey=Guid.NewGuid()}).Outcome=="LifecycleHold","hold blocks other sessions");
  check(s.ApproveSyntheticPosting(third).Outcome=="LifecycleHold","hold also blocks approval");
  var afterHold=new LocalPractice(path);check(afterHold.Login("admin",pw)&&afterHold.IsAccountingLifecycleHeld(),"hold survives restart");
  check(afterHold.ReadSyntheticPostingState(i.OperationKey).State=="SimulatedConfirmed","hold preserves history for investigation");
  check(afterHold.ReadPostingRehearsalStatus()==new PostingRehearsalStatus("Restore",1,0,1,2,1),"recovery screen retains hold reason and counts");
  using(var db=new SqliteConnection($"Data Source={path}")){db.Open();using var q=db.CreateCommand();q.CommandText="SELECT COUNT(*) FROM accounting_posting_rehearsals";check(Convert.ToInt64(q.ExecuteScalar())==5,"no duplicate durable intents");q.CommandText="UPDATE role_permissions SET allowed=0 WHERE permission_code='Accounting.Posting.Rehearse'";q.ExecuteNonQuery();}
  check(!afterHold.CanRehearsePosting(),"rehearsal grant revoked");
  try{afterHold.ReadSyntheticPostingState(i.OperationKey);throw new Exception("FAIL: unauthorized rehearsal");}catch(UnauthorizedAccessException){check(true,"rehearsal history requires authorization");}
  var again=new LocalPractice(path);check(again.Login("admin",pw)&&!again.CanRehearsePosting(),"reopen preserves revoked grant"); 
  VerifyV7Migration(path+".v7",pw,check);
 }
 private static void VerifyV7Migration(string path,string pw,Action<bool,string> check)
 {
  // Construct a synthetic v7 journal using the exact historical table definition.
  // No real business DB is read or downgraded.
  var fixture=new LocalPractice(path);fixture.Bootstrap("Synthetic Migration Firm","admin",pw,"CL");
  var marker=Guid.NewGuid();var customer=Guid.NewGuid();var income=Guid.NewGuid();
  var i=new SyntheticPostingIntent(Guid.NewGuid(),RehearsalKind.Invoice,customer,income,null,125m,new DateOnly(2026,11,25),"USD");
  var receipt=i with{OperationKey=Guid.NewGuid(),Kind=RehearsalKind.Receipt,AllocatedInvoiceKey=Guid.NewGuid(),Amount=50m};
  var uncertain=i with{OperationKey=Guid.NewGuid(),Amount=30m};
  var cancelled=i with{OperationKey=Guid.NewGuid(),Amount=40m};
  var reserved=i with{OperationKey=Guid.NewGuid(),Amount=60m};
  var oldFingerprint="CRC-SYNTHETIC-INTENT-V1|"+i.OperationKey.ToString("D")+"|Invoice|"+customer.ToString("D")+"|"+income.ToString("D")+"|-|125.00|2026-11-25|USD";
  check(i.Fingerprint()==Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(oldFingerprint))),"v7 invoice fingerprint preserved exactly");
  var oldReceiptFingerprint="CRC-SYNTHETIC-INTENT-V1|"+receipt.OperationKey.ToString("D")+"|Receipt|"+customer.ToString("D")+"|"+income.ToString("D")+"|"+receipt.AllocatedInvoiceKey!.Value.ToString("D")+"|50.00|2026-11-25|USD";
  check(receipt.Fingerprint()==Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(oldReceiptFingerprint))),"v7 receipt fingerprint preserved exactly");
  string Snapshot(SqliteConnection c)
  {
   using var q=c.CreateCommand();q.CommandText="SELECT * FROM accounting_posting_rehearsals ORDER BY id";
   using var reader=q.ExecuteReader();var rows=new List<string>();
   while(reader.Read())rows.Add(string.Join("|",Enumerable.Range(0,reader.FieldCount).Select(j=>reader.IsDBNull(j)?"<null>":Convert.ToString(reader.GetValue(j),System.Globalization.CultureInfo.InvariantCulture))));
   return string.Join("\n",rows);
  }
  string before;
  using(var c=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=path,ForeignKeys=true,Pooling=false}.ToString()))
  {
   c.Open();using var tx=c.BeginTransaction();using var q=c.CreateCommand();q.Transaction=tx;
   q.CommandText="DROP TRIGGER work_replacement_order_guard; DROP TRIGGER work_replacement_insert_guard; DROP INDEX ix_work_replacement; DROP TRIGGER lifecycle_phase_insert_guard; DROP TRIGGER lifecycle_phase_update_guard; DROP TRIGGER lifecycle_task_insert_guard; DROP TRIGGER lifecycle_task_update_guard; DROP TRIGGER lifecycle_work_insert_guard; DROP TRIGGER lifecycle_scope_authorize_guard; DROP TRIGGER project_lifecycle_insert_guard; DROP TRIGGER discontinued_task_update_guard; DROP TRIGGER discontinued_task_work_guard; DROP TABLE task_discontinuations; DROP TRIGGER cancelled_task_update_guard; DROP TRIGGER cancelled_task_work_guard; DROP TABLE task_cancellations; DROP VIEW project_operational_state; DROP TABLE project_lifecycle_events; DELETE FROM role_permissions WHERE permission_code='Projects.Closeout'; DELETE FROM permissions WHERE code='Projects.Closeout'; DROP TRIGGER conflict_party_revision_guard; DROP VIEW party_declaration_current; DROP VIEW project_party_evidence_revision; DROP TABLE party_declaration_reviews; DROP TABLE project_party_retirements; DROP TABLE project_parties; DROP TRIGGER project_overhead_time_guard; DROP VIEW combined_daily_time; DROP TABLE overhead_reviews; DROP TABLE overhead_time; DROP TRIGGER wire_assessment_transition_guard; DROP TABLE accounting_wire_assessments; DROP TABLE legacy_contact_map; DROP TABLE legacy_client_map; DROP TABLE legacy_crm_batches; DELETE FROM role_permissions WHERE permission_code='Administration.LegacyCrmImport'; DELETE FROM permissions WHERE code='Administration.LegacyCrmImport'; DROP TABLE generated_review_documents; DELETE FROM role_permissions WHERE permission_code='Engagements.Export'; DELETE FROM permissions WHERE code='Engagements.Export'; DROP TRIGGER project_conflict_policy_guard; DROP TRIGGER proposal_conflict_policy_guard; DROP TRIGGER proposal_conflict_issue_guard; DROP VIEW proposal_conflict_current; DROP TABLE proposal_conflict_reviews; DROP TRIGGER conflict_scope_guard; DROP TRIGGER conflict_task_start_guard; DROP TRIGGER conflict_start_guard; DROP VIEW project_conflict_cleared; DROP VIEW project_conflict_current; DROP TABLE project_conflict_reviews; DROP VIEW jurisdiction_policy_current; DROP TABLE jurisdiction_policy_events; DELETE FROM role_permissions WHERE permission_code='Compliance.Policy'; DELETE FROM permissions WHERE code='Compliance.Policy'; DELETE FROM role_permissions WHERE permission_code IN ('Compliance.View','Compliance.Record'); DELETE FROM permissions WHERE code IN ('Compliance.View','Compliance.Record'); DROP TRIGGER owner_exception_user_insert; DROP TRIGGER owner_exception_user_update; DROP TRIGGER owner_exception_grant_insert; DROP TRIGGER owner_exception_grant_update; DROP TRIGGER owner_exception_grant_delete; DROP TRIGGER invoice_approval_independent; DROP TRIGGER work_review_independent; DROP TRIGGER work_billing_approved_only; ALTER TABLE invoice_draft_decisions DROP COLUMN owner_exception_event_id; ALTER TABLE work_reviews DROP COLUMN owner_exception_event_id; ALTER TABLE work_billing_classifications DROP COLUMN owner_exception_event_id; DROP VIEW owner_approval_current; DROP VIEW owner_approval_eligible; DROP TABLE owner_approval_events; DELETE FROM role_permissions WHERE permission_code='Administration.RecoveryCopy'; DELETE FROM permissions WHERE code='Administration.RecoveryCopy'; DELETE FROM role_permissions WHERE permission_code IN ('Billing.Invoices.Export','Reports.Operations','Reports.Financial'); DELETE FROM permissions WHERE code IN ('Billing.Invoices.Export','Reports.Operations','Reports.Financial'); DROP TABLE invoice_draft_decisions; DROP TABLE invoice_draft_lines; DROP TABLE invoice_drafts; DROP TABLE project_billing_policies; DROP TABLE employee_rates; DELETE FROM role_permissions WHERE permission_code IN ('Billing.Rates','Billing.Invoices.View','Billing.Invoices.Edit','Billing.Invoices.Approve'); DELETE FROM permissions WHERE code IN ('Billing.Rates','Billing.Invoices.View','Billing.Invoices.Edit','Billing.Invoices.Approve'); DROP TRIGGER task_scope_immutable; DROP TRIGGER task_scope_insert_guard; DROP TRIGGER task_scope_start_guard; ALTER TABLE project_tasks DROP COLUMN scope_change_id; DROP TABLE project_scope_decisions; DROP TABLE project_scope_changes; DROP TABLE project_quality_closures; DROP TABLE project_quality_notes; DROP TABLE project_quality_items; DROP TABLE project_documents; DELETE FROM role_permissions WHERE permission_code IN ('Documents.View','Documents.Record','Documents.Restricted','QA.View','QA.Edit','QA.Close','Scope.Authorize'); DELETE FROM permissions WHERE code IN ('Documents.View','Documents.Record','Documents.Restricted','QA.View','QA.Edit','QA.Close','Scope.Authorize'); DROP TABLE work_billing_classifications; DROP TABLE work_reviews; DROP TABLE work_submissions; DROP TABLE employee_attendance; DELETE FROM role_permissions WHERE permission_code IN ('Work.Review','Billing.Classify'); DELETE FROM permissions WHERE code IN ('Work.Review','Billing.Classify'); DROP TABLE project_tasks; DROP TABLE project_phases; DELETE FROM role_permissions WHERE permission_code IN ('Projects.Plan','Work.Own'); DELETE FROM permissions WHERE code IN ('Projects.Plan','Work.Own'); DROP TABLE employee_licenses; DROP TABLE employees; ALTER TABLE users DROP COLUMN credential_revision; DELETE FROM role_permissions WHERE permission_code IN ('Administration.Manage','Employees.View','Employees.Edit'); DELETE FROM permissions WHERE code IN ('Administration.Manage','Employees.View','Employees.Edit'); DROP TABLE project_start_authorizations; DROP TABLE project_agreements; DROP TABLE project_setup_items; DELETE FROM role_permissions WHERE permission_code IN ('Agreements.Record','Agreements.View','Projects.Authorize','Projects.Setup'); DELETE FROM permissions WHERE code IN ('Agreements.Record','Agreements.View','Projects.Authorize','Projects.Setup'); DROP TABLE projects; DROP TABLE engagements; DROP TABLE proposals; DELETE FROM role_permissions WHERE permission_code IN ('Engagements.View','Engagements.Edit','Engagements.Issue','Engagements.Accept','Projects.View'); DELETE FROM permissions WHERE code IN ('Engagements.View','Engagements.Edit','Engagements.Issue','Engagements.Accept','Projects.View'); DROP TRIGGER wire_attempt_no_cancel; DROP TABLE accounting_wire_attempts; DELETE FROM role_permissions WHERE permission_code='Accounting.WireDraft.Rehearse'; DELETE FROM permissions WHERE code='Accounting.WireDraft.Rehearse'; DROP TRIGGER legacy_no_wire_draft_collision; DROP TABLE accounting_wire_drafts; DELETE FROM role_permissions WHERE permission_code IN ('Accounting.WireDraft.Prepare','Accounting.WireDraft.Approve'); DELETE FROM permissions WHERE code IN ('Accounting.WireDraft.Prepare','Accounting.WireDraft.Approve'); DROP TABLE accounting_rehearsal_approvals; DELETE FROM role_permissions WHERE permission_code='Accounting.Posting.RehearseApprove'; DELETE FROM permissions WHERE code='Accounting.Posting.RehearseApprove'; DROP TABLE accounting_posting_rehearsals; CREATE TABLE accounting_posting_rehearsals(id INTEGER PRIMARY KEY,source_key TEXT NOT NULL UNIQUE CHECK(length(source_key)=36),marker_key TEXT NOT NULL CHECK(length(marker_key)=36),kind TEXT NOT NULL CHECK(kind IN ('Invoice','Receipt')),payload_hash TEXT NOT NULL CHECK(length(payload_hash)=64),state TEXT NOT NULL CHECK(state IN ('Reserved','InFlight','Uncertain','SimulatedConfirmed','Cancelled')),attempt_key TEXT UNIQUE,simulated_external_key TEXT UNIQUE,created_by INTEGER NOT NULL REFERENCES users(id),updated_by INTEGER NOT NULL REFERENCES users(id),created_utc TEXT NOT NULL,updated_utc TEXT NOT NULL,CHECK((state IN ('Reserved','Cancelled') AND attempt_key IS NULL AND simulated_external_key IS NULL) OR (state IN ('InFlight','Uncertain') AND attempt_key IS NOT NULL AND simulated_external_key IS NULL) OR (state='SimulatedConfirmed' AND attempt_key IS NOT NULL AND simulated_external_key IS NOT NULL))); PRAGMA user_version=7;";
   q.ExecuteNonQuery();
   void Insert(SyntheticPostingIntent intent,string state,Guid? attempt,Guid? external)
   {
    q.Parameters.Clear();
    q.CommandText="INSERT INTO accounting_posting_rehearsals(source_key,marker_key,kind,payload_hash,state,attempt_key,simulated_external_key,created_by,updated_by,created_utc,updated_utc) SELECT $s,$m,$k,$h,$st,$a,$e,id,id,'2026-11-25T00:00:00Z','2026-11-25T00:00:00Z' FROM users WHERE username='admin'";
    q.Parameters.AddWithValue("$s",intent.OperationKey.ToString("D"));q.Parameters.AddWithValue("$m",marker.ToString("D"));q.Parameters.AddWithValue("$k",intent.Kind.ToString());q.Parameters.AddWithValue("$h",intent.Fingerprint());q.Parameters.AddWithValue("$st",state);
    q.Parameters.AddWithValue("$a",(object?)attempt?.ToString("D")??DBNull.Value);q.Parameters.AddWithValue("$e",(object?)external?.ToString("D")??DBNull.Value);
    if(q.ExecuteNonQuery()!=1)throw new Exception("Synthetic v7 fixture insert failed.");
   }
   Insert(i,"SimulatedConfirmed",Guid.NewGuid(),Guid.NewGuid());Insert(receipt,"InFlight",Guid.NewGuid(),null);Insert(uncertain,"Uncertain",Guid.NewGuid(),null);Insert(cancelled,"Cancelled",null,null);Insert(reserved,"Reserved",null,null);
   q.Parameters.Clear();q.CommandText="INSERT INTO accounting_lifecycle_hold(id,reason,declared_by,declared_utc) SELECT 1,'Restore',id,'2026-11-25T00:00:00Z' FROM users WHERE username='admin'; UPDATE role_permissions SET allowed=0 WHERE permission_code='Accounting.Posting.Rehearse';";q.ExecuteNonQuery();tx.Commit();
   before=Snapshot(c);
  }
  var upgraded=new LocalPractice(path);
  check(upgraded.Login("admin",pw)&&!upgraded.CanRehearsePosting(),"v7 migration preserves revoked rehearsal grant");
  using(var c=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=path,ForeignKeys=true,Pooling=false}.ToString()))
  {
   c.Open();using var q=c.CreateCommand();
   q.CommandText="PRAGMA user_version";check(Convert.ToInt32(q.ExecuteScalar())==35,"v7 migration reaches schema 35");
   check(before==Snapshot(c),"v7 journal rows and attempt history preserved byte-for-byte at logical row level");
   q.CommandText="SELECT count(*) FROM accounting_rehearsal_approvals";check(Convert.ToInt32(q.ExecuteScalar())==0,"migration never retroactively approves old reservations");
   q.CommandText="SELECT reason FROM accounting_lifecycle_hold WHERE id=1";check((string?)q.ExecuteScalar()=="Restore","v7 sticky hold preserved");
   q.CommandText="PRAGMA foreign_key_check";using var reader=q.ExecuteReader();check(!reader.Read(),"v8 migration foreign keys valid");
  }

 }
}
