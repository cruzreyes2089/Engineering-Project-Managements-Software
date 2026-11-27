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
  // Explicit test-only activation on a unique synthetic fixture. No constructor,
  // actual application migration, existing database, recovery copy or real data.
  using(var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=path,ForeignKeys=true,Pooling=false}.ToString()))
  {
   db.Open();using var q=db.CreateCommand();
   using(var stream=typeof(ProjectBudgetServiceTests).Assembly.GetManifestResourceStream("SyntheticBudgetHistory.sql")!)
   using(var reader=new StreamReader(stream))q.CommandText=reader.ReadToEnd();
   q.ExecuteNonQuery();
   q.CommandText="INSERT INTO permissions(code) VALUES('Projects.Budget.View'),('Projects.Budget.Record');INSERT INTO role_permissions SELECT id,'Projects.Budget.View',1 FROM roles WHERE name='Administrator';INSERT INTO role_permissions SELECT id,'Projects.Budget.Record',1 FROM roles WHERE name='Administrator';PRAGMA user_version=48";q.ExecuteNonQuery();
  }
  Denied(()=>worker.ReadProjectBudgetHistory(project),"candidate budget read needs explicit budget grant");
  var op=Guid.NewGuid();var first=admin.RecordProjectBudgetRevision(project,op,null,snapshot,120,"FirstRecorded","Synthetic baseline",true);
  check(!first.Replayed&&first.Entry.Model.Minutes==120&&admin.ReadProjectBudgetHistory(project).Budgets.Count==1,"actual service records first budget in transaction");
  using(var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=path,ForeignKeys=true,Pooling=false}.ToString()))
  {
   db.Open();using var q=db.CreateCommand();q.CommandText="INSERT INTO role_permissions SELECT id,'Projects.Budget.View',1 FROM roles WHERE name='Engineer';INSERT INTO role_permissions SELECT id,'Projects.Budget.Record',1 FROM roles WHERE name='Engineer'";q.ExecuteNonQuery();
  }
  check(worker.ReadProjectBudgetHistory(project).Budgets.Single().Model.Hash==first.Entry.Model.Hash,"budget history read requires assigned permission");
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
  worker.Logout();Denied(()=>worker.ReadProjectBudgetHistory(project),"budget read rejects signed-out actor");
  admin.Logout();Denied(()=>admin.RecordProjectBudgetRevision(project,op,null,snapshot,120,"FirstRecorded","Synthetic baseline",true),"budget replay requires current authorized actor");
 }
}
