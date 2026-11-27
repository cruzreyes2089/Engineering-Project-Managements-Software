using Microsoft.Data.Sqlite;
using PracticeManager.Core;
using PracticeManager.Persistence;

internal static class ProjectBudgetServiceTests
{
 public static void Run(string path,string pw,Action<bool,string> check)
 {
  var admin=new LocalPractice(path);admin.Bootstrap("Synthetic budget firm","admin",pw,"CL");check(admin.Login("admin",pw),"budget service fixture admin login");
  var role=admin.ReadStaffRoles().Single(r=>r.Name=="Engineer");
  admin.SetStaffGrant(role.Id,"Projects.View",true,false);admin.SetStaffGrant(role.Id,"Clients.View",true,false);admin.SetStaffGrant(role.Id,"Reports.Operations",true,false);
  var user=admin.CreateStaffAccount("budget-reader",role.Id,"Synthetic-budget-temp-0001");
  var worker=new LocalPractice(path);check(worker.ChangeRequiredPassword("budget-reader","Synthetic-budget-temp-0001","Synthetic-budget-password-0001")&&worker.Login("budget-reader","Synthetic-budget-password-0001"),"budget service reader login");
  var client=admin.SaveClient(new Client(0,"","Business / Company","Synthetic Client","","","","","","","","","","","Active"));
  var proposal=admin.CreateProposal(new(client,"Synthetic budget","Site","Scope",100m));
  var proposalHash=ConflictReviewTests.Issue(admin,proposal,1);
  var project=admin.RecordProposalDecision(proposal,proposalHash,true,"Synthetic").ProjectId!.Value;
  var employee=admin.CreateEmployee(new("Synthetic","Budget worker","","",true),user);
  var day=new DateOnly(2026,11,26);var phase=admin.CreateProjectPhase(project,"Design",day,day);
  var plan=new ProjectTaskPlan(phase,employee,"Design",day,day,120);var task=admin.CreateProjectTask(project,plan);
  var effort=admin.ReadProjectEffort(project);var snapshot=ProjectBudgetReview.SnapshotHash(project,effort);
  void Denied(Action a,string label){try{a();}catch(UnauthorizedAccessException){check(true,label);return;}throw new Exception("FAIL: "+label);}
  Denied(()=>admin.ReadProjectBudgetHistory(project),"ordinary schema 47 cannot access candidate budget service");
  // Qualify the exact internal candidate migration, on this unique synthetic fixture only.
  using(var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=path,ForeignKeys=true,Pooling=false}.ToString()))
  {
   db.Open();
   using(var copy=new SqliteConnection("Data Source=:memory:;Foreign Keys=True;Pooling=False"))
   {
    copy.Open();db.BackupDatabase(copy);
    using(var q=copy.CreateCommand()){q.CommandText="CREATE TRIGGER synthetic_budget_migration_fault AFTER INSERT ON permissions WHEN NEW.code='Projects.Budget.Record' BEGIN UPDATE users SET failed_attempts=failed_attempts+1; END";q.ExecuteNonQuery();}
    try
    {
     using var bad=copy.BeginTransaction(deferred:false);
     _=EffortBudgetMigrationCandidate.Apply(copy,bad);bad.Commit();
     throw new Exception("FAIL: candidate migration mutation");
    }
    catch(InvalidOperationException){check(true,"candidate migration refuses pre-existing user row mutation");}
    using var verify=copy.CreateCommand();verify.CommandText="SELECT (SELECT user_version FROM pragma_user_version)=47 AND NOT EXISTS(SELECT 1 FROM sqlite_schema WHERE name='effort_budget_revisions') AND NOT EXISTS(SELECT 1 FROM permissions WHERE code='Projects.Budget.Record')";
    check(Convert.ToInt64(verify.ExecuteScalar())==1,"candidate migration failure rolls back schema version tables and grants");
   }
   using var t=db.BeginTransaction(deferred:false);
   var witness=EffortBudgetMigrationCandidate.Apply(db,t);
   check(witness.Length==64,"candidate additive migration reconciles full schema and rows");
   t.Commit();
  }
  check(ProjectBudgetReview.SnapshotHash(project,admin.ReadProjectEffort(project))==snapshot,"candidate migration preserves current project effort evidence");
  Denied(()=>worker.ReadProjectBudgetHistory(project),"candidate budget read needs explicit budget grant");
  var op=Guid.NewGuid();var first=admin.RecordProjectBudgetRevision(project,op,null,snapshot,120,"FirstRecorded","Synthetic baseline",true);
  check(!first.Replayed&&first.Entry.Model.Minutes==120&&admin.ReadProjectBudgetHistory(project).Budgets.Count==1,"actual service records first budget in transaction");
  admin.SetStaffGrant(role.Id,"Projects.Budget.View",true,false);
  try{admin.SetStaffGrant(role.Id,"Projects.Budget.Record",true,false);throw new Exception("FAIL: budget recording delegated");}
  catch(ArgumentException){check(true,"budget recording grant cannot be delegated");}
  check(worker.ReadProjectBudgetHistory(project).Budgets.Single().Model.Hash==first.Entry.Model.Hash,"budget history read requires assigned permission");
  var view=worker.ReadProjectBudgetWorkspace(project);
  check(!view.CanRecord&&view.History.Budgets.Single().Model.Hash==first.Entry.Model.Hash&&view.ReviewedSnapshotHash==snapshot,"budget workspace provides consistent read-only history and current evidence");
  var management=admin.ReadProjectBudgetWorkspace(project);
  check(management.CanRecord&&management.ReviewedSnapshotHash==snapshot&&management.History.Budgets.Count==1,"administrator budget workspace permits explicit review");
  Denied(()=>worker.RecordProjectBudgetRevision(project,Guid.NewGuid(),first.Entry.Model.Hash,snapshot,150,"ManagementReview","Synthetic denied",true),"non-administrator cannot record budget even with record grant");
  Denied(()=>worker.RecordProjectEffortForecast(project,Guid.NewGuid(),null,first.Entry.Model.Hash,snapshot,new[]{new TaskRemainingInput(task,120)},"Synthetic denied",true),"non-administrator cannot record forecast");
  admin.EditProjectTask(project,task,1,plan with{EstimateMinutes=150});
  try{admin.RecordProjectBudgetRevision(project,Guid.NewGuid(),first.Entry.Model.Hash,snapshot,150,"EstimateReview","Synthetic stale",true);throw new Exception("FAIL: stale actual evidence");}
  catch(InvalidOperationException){check(admin.ReadProjectBudgetHistory(project).Budgets.Count==1,"actual transaction rechecks current plan evidence");}
  var nextSnapshot=ProjectBudgetReview.SnapshotHash(project,admin.ReadProjectEffort(project));
  var second=admin.RecordProjectBudgetRevision(project,Guid.NewGuid(),first.Entry.Model.Hash,nextSnapshot,150,"EstimateReview","Synthetic revision",true);
  var forecast=admin.RecordProjectEffortForecast(project,Guid.NewGuid(),null,second.Entry.Model.Hash,nextSnapshot,new[]{new TaskRemainingInput(task,160)},"Synthetic forecast",true);
  check(forecast.Entry.Model.TotalForecastMinutes==160&&forecast.Entry.Model.VarianceFromFirstMinutes==40&&forecast.Entry.Model.VarianceFromCurrentMinutes==10,"actual service forecasts from current task evidence");
  check(admin.RecordProjectBudgetRevision(project,op,null,snapshot,120,"FirstRecorded","Synthetic baseline",true).Replayed,"actual service exact historical retry");
  var saved=admin.ReadProjectBudgetWorkspace(project);
  check(saved.CanRecord&&saved.ReviewedSnapshotHash==nextSnapshot&&saved.History.Budgets.Count==2&&saved.History.Forecasts.Single().Hash==forecast.Entry.Hash,"budget workspace refresh reconciles revision and forecast");
  VerifyRecoveryCandidate(path,check);
  VerifyRecoveryWorkflow(admin,path,check);
  worker.Logout();Denied(()=>worker.ReadProjectBudgetHistory(project),"budget read rejects signed-out actor");
  Denied(()=>worker.ReadProjectBudgetWorkspace(project),"budget workspace rejects signed-out actor");
  admin.Logout();Denied(()=>admin.RecordProjectBudgetRevision(project,op,null,snapshot,120,"FirstRecorded","Synthetic baseline",true),"budget replay requires current authorized actor");
 }
 static void VerifyRecoveryCandidate(string path,Action<bool,string> check)
 {
  using var source=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=path,Mode=SqliteOpenMode.ReadOnly,ForeignKeys=true,Pooling=false}.ToString());source.Open();
  EffortBudgetVerification Verify(SqliteConnection db)
  {
   using var t=db.BeginTransaction(deferred:true);return EffortBudgetStore.VerifyDatabase(db,t);
  }
  var original=Verify(source);
  check(original.Projects==1&&original.Revisions==2&&original.Forecasts==1&&original.Digest.Length==64,"candidate budget verifier reconciles complete project and audit census");
  using(var copy=new SqliteConnection("Data Source=:memory:;Foreign Keys=True;Pooling=False"))
  {
   copy.Open();source.BackupDatabase(copy);
   check(Verify(copy)==original,"candidate SQLite backup preserves budget history witness");
  }
  foreach(var (label,sql) in new[]{
   ("budget metadata","DROP TRIGGER effort_budget_no_update;UPDATE effort_budget_revisions SET recorded_utc='2000-01-01T00:00:00.0000000+00:00' WHERE revision=1"),
   ("forecast snapshot","DROP TRIGGER effort_forecast_no_update;UPDATE effort_forecast_records SET snapshot_bytes=X'7B7D'"),
   ("orphan audit","INSERT INTO audit_events(actor_id,action,entity,entity_id) SELECT actor_id,'RecordEffortForecast','Project',project_id FROM effort_budget_revisions LIMIT 1"),
   ("wrong audit entity","INSERT INTO audit_events(actor_id,action,entity,entity_id) SELECT actor_id,'RecordEffortForecast','Firm',project_id FROM effort_budget_revisions LIMIT 1"),
   ("missing history","DROP TRIGGER effort_forecast_no_delete;DELETE FROM effort_forecast_records")})
  {
   using var copy=new SqliteConnection("Data Source=:memory:;Foreign Keys=True;Pooling=False");copy.Open();source.BackupDatabase(copy);
   using(var q=copy.CreateCommand()){q.CommandText=sql;q.ExecuteNonQuery();}
   try{_=Verify(copy);throw new Exception("FAIL: budget witness "+label);}
   catch(InvalidOperationException){check(true,"candidate budget verifier rejects "+label);}
   catch(ArgumentException){check(true,"candidate budget verifier rejects "+label);}
  }
  check(Verify(source)==original,"candidate budget verification failures leave source unchanged");
 }

 static void VerifyRecoveryWorkflow(LocalPractice admin,string path,Action<bool,string> check)
 {
  var root=Path.GetDirectoryName(path)!;
  var recovery=Path.Combine(root,"budget-qualified-recovery.db");
  var restored=Path.Combine(root,"budget-qualified-isolated.db");
  var damaged=Path.Combine(root,"budget-damaged-recovery.db");
  var refused=Path.Combine(root,"budget-refused-restore.db");
  EffortBudgetVerification Verify(string file)
  {
   using var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=file,Mode=SqliteOpenMode.ReadOnly,ForeignKeys=true,Pooling=false}.ToString());db.Open();
   using var t=db.BeginTransaction(deferred:true);EffortBudgetMigrationCandidate.VerifyLayout(db,t);return EffortBudgetStore.VerifyDatabase(db,t);
  }
  var original=Verify(path);var saved=admin.CreateRecoveryCopy(recovery);
  check(Verify(recovery)==original,"actual recovery workflow preserves candidate budget witness after isolation and vacuum");
  var copy=admin.RestoreRecoveryCopyToNewFile(recovery,saved.Sha256,restored);
  check(copy.Sha256==saved.Sha256&&Verify(restored)==original,"actual isolated restore preserves candidate budget history");
  File.Copy(recovery,damaged);
  using(var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=damaged,ForeignKeys=true,Pooling=false}.ToString()))
  {
   db.Open();using var q=db.CreateCommand();q.CommandText="DROP TRIGGER effort_forecast_no_update;UPDATE effort_forecast_records SET reason='Synthetic tamper'";q.ExecuteNonQuery();
  }
  var damagedHash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(damaged))).ToLowerInvariant();
  try{admin.RestoreRecoveryCopyToNewFile(damaged,damagedHash,refused);throw new Exception("FAIL: damaged budget restore");}
  catch(InvalidOperationException){check(!File.Exists(refused),"actual isolated restore refuses corrupted candidate even with recomputed file hash");}
  check(Verify(path)==original,"candidate recovery operations preserve active budget history");
 }

}
