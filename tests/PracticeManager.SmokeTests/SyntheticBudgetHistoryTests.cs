using Microsoft.Data.Sqlite;
using PracticeManager.Core;
internal static class SyntheticBudgetHistoryTests
{
 public static void Run(Action<bool,string> check)
 {
  using var db=new SqliteConnection("Data Source=:memory:;Foreign Keys=True;Pooling=False");db.Open();
  void Sql(string sql){using var q=db.CreateCommand();q.CommandText=sql;q.ExecuteNonQuery();}
  Sql(@"CREATE TABLE projects(id INTEGER PRIMARY KEY);CREATE TABLE users(id INTEGER PRIMARY KEY);
   CREATE TABLE audit_events(id INTEGER PRIMARY KEY,actor_id INTEGER REFERENCES users(id),action TEXT NOT NULL,entity TEXT NOT NULL,entity_id INTEGER);
   CREATE TABLE synthetic_effort_source(project_id INTEGER PRIMARY KEY REFERENCES projects(id),snapshot BLOB NOT NULL);
   INSERT INTO projects VALUES(1),(2);INSERT INTO users VALUES(1),(2)");
  using(var stream=typeof(SyntheticBudgetHistoryTests).Assembly.GetManifestResourceStream("SyntheticBudgetHistory.sql")!)
  using(var reader=new StreamReader(stream))Sql(reader.ReadToEnd());
  var service=new SyntheticBudgetHistory(db);
  var effort=new ProjectEffortReview("SYNTHETIC",new[]{new TaskEffort(1,"Design","Work","InProgress",120,60,15,0,0){Revision=2}}){ProjectId=1};
  service.SetSyntheticSnapshot(effort);var hash=ProjectBudgetReview.SnapshotHash(1,effort);
  var operation=Guid.NewGuid();
  var first=service.AppendBudget(1,operation,1,null,hash,120,"FirstRecorded","Synthetic first",true);
  check(!first.Replayed&&first.Entry.Model.Minutes==120&&service.Verify(1)==(1,0),"native candidate persists first budget and audit");
  var fop=Guid.NewGuid();
  var forecast=service.AppendForecast(1,fop,1,null,first.Entry.Model.Hash,hash,new[]{new TaskRemainingInput(1,75)},"Synthetic forecast",true);
  check(!forecast.Replayed&&forecast.Entry.Model.TotalForecastMinutes==150&&service.Verify(1)==(1,1),"native candidate persists complete forecast and audit");
  var second=service.AppendBudget(1,Guid.NewGuid(),1,first.Entry.Model.Hash,hash,160,"ManagementReview","Synthetic revision",true);
  var retry=service.AppendBudget(1,operation,1,null,hash,120,"FirstRecorded","Synthetic first",true);
  check(retry.Replayed&&retry.Entry==first.Entry&&service.Verify(1)==(2,1),"exact budget retry returns historical record without new audit");
  var fretry=service.AppendForecast(1,fop,1,null,first.Entry.Model.Hash,hash,new[]{new TaskRemainingInput(1,75)},"Synthetic forecast",true);
  check(fretry.Replayed&&fretry.Entry.Hash==forecast.Entry.Hash&&service.Verify(1)==(2,1),"exact forecast retry retains original budget revision");
  void Invalid(Action a,string label){try{a();}catch(ArgumentException){check(true,label);return;}throw new Exception("FAIL: "+label);}
  Invalid(()=>service.AppendBudget(1,operation,1,null,hash,121,"FirstRecorded","Synthetic first",true),"changed budget retry refused");
  Invalid(()=>service.AppendForecast(1,fop,1,null,first.Entry.Model.Hash,hash,new[]{new TaskRemainingInput(1,76)},"Synthetic forecast",true),"changed forecast retry refused");
  Invalid(()=>service.AppendForecast(1,operation,1,forecast.Entry.Hash,second.Entry.Model.Hash,hash,new[]{new TaskRemainingInput(1,75)},"Synthetic forecast",true),"cross-kind operation replay refused");
  Invalid(()=>service.AppendForecast(1,Guid.NewGuid(),1,forecast.Entry.Hash,first.Entry.Model.Hash,hash,new[]{new TaskRemainingInput(1,75)},"Stale",true),"new forecast must bind latest budget");
  Invalid(()=>service.AppendForecast(1,Guid.NewGuid(),1,null,second.Entry.Model.Hash,hash,new[]{new TaskRemainingInput(1,75)},"Stale",true),"stale forecast predecessor refused");
  var changed=effort with{Tasks=new[]{effort.Tasks.Single() with{ApprovedMinutes=70}}};
  service.SetSyntheticSnapshot(changed);
  try{service.AppendBudget(1,Guid.NewGuid(),1,second.Entry.Model.Hash,hash,180,"ManagementReview","Stale snapshot",true);throw new Exception("FAIL: stale snapshot");}
  catch(InvalidOperationException){check(service.Verify(1)==(2,1),"changed effort snapshot refuses new history");}
  var changedHash=ProjectBudgetReview.SnapshotHash(1,changed);
  Sql("CREATE TRIGGER synthetic_forecast_failure BEFORE INSERT ON effort_forecast_records BEGIN SELECT RAISE(ABORT,'synthetic failure'); END");
  try{service.AppendForecast(1,Guid.NewGuid(),1,forecast.Entry.Hash,second.Entry.Model.Hash,changedHash,new[]{new TaskRemainingInput(1,75)},"Synthetic rollback",true);throw new Exception("FAIL: forecast rollback");}
  catch(SqliteException e) when(e.Message.Contains("synthetic failure",StringComparison.Ordinal)){check(service.Verify(1)==(2,1),"native forecast insertion failure rolls back linked audit");}
  Sql("DROP TRIGGER synthetic_forecast_failure");
  var next=service.AppendForecast(1,Guid.NewGuid(),1,forecast.Entry.Hash,second.Entry.Model.Hash,changedHash,new[]{new TaskRemainingInput(1,75)},"Synthetic updated forecast",true);
  check(next.Entry.Model.TotalForecastMinutes==160&&next.Entry.Previous==forecast.Entry.Hash&&service.Verify(1)==(2,2),"native forecast chain retains updated snapshot and budget");
  Sql("DROP TRIGGER effort_forecast_no_update;UPDATE effort_forecast_records SET reason='Tampered' WHERE sequence=2");
  try{service.Verify(1);throw new Exception("FAIL: forecast tamper");}
  catch(InvalidOperationException){check(true,"native forecast envelope detects changed history");}
 }
}
