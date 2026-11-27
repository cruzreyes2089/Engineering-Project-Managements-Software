using System.Text.Json;
using Microsoft.Data.Sqlite;
using PracticeManager.Core;
using PracticeManager.Persistence;

// Same compiled store as the application-facing candidate, with a synthetic actor
// and synthetic current source in this test fixture only.
internal sealed class SyntheticBudgetHistory(SqliteConnection db)
{
 EffortBudgetStore Store(long actor)=>new(db,(_,_,_)=>actor,Current);
 ProjectEffortReview Current(SqliteTransaction t,long project)
 {
  using var q=db.CreateCommand();q.Transaction=t;q.CommandText="SELECT snapshot FROM synthetic_effort_source WHERE project_id=$p";q.Parameters.AddWithValue("$p",project);
  if(q.ExecuteScalar() is not byte[] bytes)throw new ArgumentException("Synthetic effort missing.");
  return JsonSerializer.Deserialize<ProjectEffortReview>(bytes)??throw new InvalidOperationException("Synthetic effort missing.");
 }
 internal static string ForecastHash(BudgetForecastReview model,int sequence,string? prior,string reason,string operation,long actor,string utc,long audit)
  =>EffortBudgetStore.ForecastHash(model,sequence,prior,reason,operation,actor,utc,audit);
 public (EffortBudgetEntry Entry,bool Replayed) AppendBudget(long project,Guid operation,long actor,string? expected,string snapshotHash,long minutes,string kind,string reason,bool confirmed)
  =>Store(actor).AppendBudget(project,operation,expected,snapshotHash,minutes,kind,reason,confirmed);
 public (EffortForecastEntry Entry,bool Replayed) AppendForecast(long project,Guid operation,long actor,string? expectedForecast,string expectedBudget,string snapshotHash,IReadOnlyList<TaskRemainingInput> remaining,string reason,bool confirmed)
  =>Store(actor).AppendForecast(project,operation,expectedForecast,expectedBudget,snapshotHash,remaining,reason,confirmed);
 public (int Budgets,int Forecasts) Verify(long project)
 {
  var history=Store(1).Read(project);return(history.Budgets.Count,history.Forecasts.Count);
 }
 public void SetSyntheticSnapshot(ProjectEffortReview snapshot)
 {
  _=ProjectBudgetReview.SnapshotHash(snapshot.ProjectId,snapshot);
  using var t=db.BeginTransaction(deferred:false);using var q=db.CreateCommand();q.Transaction=t;
  q.CommandText="INSERT INTO synthetic_effort_source(project_id,snapshot) VALUES($p,$b) ON CONFLICT(project_id) DO UPDATE SET snapshot=excluded.snapshot";
  q.Parameters.AddWithValue("$p",snapshot.ProjectId);q.Parameters.AddWithValue("$b",EffortBudgetStore.SnapshotBytes(snapshot));q.ExecuteNonQuery();t.Commit();
 }
}
