using Microsoft.Data.Sqlite;
using PracticeManager.Core;
using PracticeManager.Persistence;
internal static class BudgetAcceptanceStartupTests
{
 public static void Run(string root,string pw,Action<bool,string> check)
 {
  var local=Path.Combine(root,"budget-acceptance-startup");Directory.CreateDirectory(local);
  var ordinary=DevelopmentWorkspace.Resolve(local,Array.Empty<string>());
  var normal=new LocalPractice(ordinary.DatabasePath);
  var workspace=DevelopmentWorkspace.Resolve(local,new[]{"--budget-acceptance-new"});
  check(workspace.IsBudgetAcceptance&&workspace.IsAcceptance&&workspace.DatabasePath!=ordinary.DatabasePath,"budget acceptance uses distinct workspace");
  var id=workspace.AcceptanceId!;
  var service=LocalPractice.OpenBudgetAcceptance(local,id,true);
  check(!service.IsInitialized(),"new budget acceptance begins uninitialized");
  service.Bootstrap("Synthetic budget acceptance","admin",pw,"CL");
  check(service.Login("admin",pw)&&service.Can("Projects.Budget.View")&&service.Can("Projects.Budget.Record"),"synthetic budget bootstrap atomically creates budget schema and grants");
  var role=service.ReadStaffRoles().Single(x=>x.Name=="Engineer");
  service.SetStaffGrant(role.Id,"Projects.Budget.View",true,false);
  check(service.ReadStaffGrants(role.Id).Single(x=>x.Code=="Projects.Budget.View").Allowed,"budget viewer grant persisted via staff administration");
  service.Logout();
  var reopen=DevelopmentWorkspace.Resolve(local,new[]{"--budget-acceptance-session",id});
  check(reopen.DatabasePath==workspace.DatabasePath&&reopen.IsBudgetAcceptance,"budget acceptance reopens exact marked path");
  var next=LocalPractice.OpenBudgetAcceptance(local,id,false);
  check(next.Login("admin",pw)&&next.Can("Projects.Budget.Record"),"budget acceptance restart validates schema and grants");
  next.SetStaffGrant(role.Id,"Projects.Budget.View",false,true);
  check(!next.ReadStaffGrants(role.Id).Single(x=>x.Code=="Projects.Budget.View").Allowed,"budget viewer grant revocation persisted");
  next.Logout();
  void Invalid(Action a,string label){try{a();}catch(ArgumentException){check(true,label);return;}catch(InvalidOperationException){check(true,label);return;}throw new Exception("FAIL: "+label);}
  Invalid(()=>new LocalPractice(workspace.DatabasePath),"normal constructor refuses schema 48");
  Invalid(()=>LocalPractice.OpenBudgetAcceptance(local,id,true),"new budget startup refuses existing file");
  Invalid(()=>DevelopmentWorkspace.Resolve(local,new[]{"--acceptance-session",id}),"normal acceptance cannot open budget session");
  using(var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=ordinary.DatabasePath,Mode=SqliteOpenMode.ReadOnly,Pooling=false}.ToString()))
  {db.Open();using var q=db.CreateCommand();q.CommandText="PRAGMA user_version";check(Convert.ToInt64(q.ExecuteScalar())==47,"ordinary development database stays schema 47");}
  using(var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=workspace.DatabasePath,Pooling=false}.ToString()))
  {db.Open();using var q=db.CreateCommand();q.CommandText="DROP TRIGGER effort_forecast_no_update";q.ExecuteNonQuery();}
  Invalid(()=>LocalPractice.OpenBudgetAcceptance(local,id,false),"budget startup refuses altered schema layout");
 }
}
