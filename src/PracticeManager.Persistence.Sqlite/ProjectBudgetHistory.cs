using Microsoft.Data.Sqlite;
using PracticeManager.Core;
namespace PracticeManager.Persistence;

public sealed partial class LocalPractice
{
 // Candidate schema 48 is not activated by the schema-47 constructor. Existing
 // installations receive no grants or tables. Only the disposable integration fixture
 // may provision the candidate for testing until migration/recovery qualification.
 long RequireEffortBudget(SqliteConnection c,SqliteTransaction t,long project,bool write)
 {
  var actor=RequireReport(c,t,"Reports.Operations");Require(c,t,"Projects.Budget.View");
  if(Convert.ToInt64(Scalar(c,t,"PRAGMA user_version"))!=48)throw new UnauthorizedAccessException("Budget history is not enabled.");
  ProjectRow(c,t,project);
  if(write)
  {
   RequireAdministration(c,t);RequirePlan(c,t);Require(c,t,"Projects.Budget.Record");
   RequireOpenOperations(c,t,project);
  }
  return actor;
 }
 EffortBudgetStore BudgetStore(SqliteConnection c)=>new(c,(t,p,w)=>RequireEffortBudget(c,t,p,w),(t,p)=>ReadProjectEffortCore(c,t,p));
 public EffortBudgetWorkspace ReadProjectBudgetWorkspace(long project)
 {
  using var c=Open();return BudgetStore(c).Review(project);
 }
 public EffortBudgetHistory ReadProjectBudgetHistory(long project)
 {
  using var c=Open();return BudgetStore(c).Read(project);
 }
 public (EffortBudgetEntry Entry,bool Replayed) RecordProjectBudgetRevision(long project,Guid operation,string? expected,string reviewedSnapshotHash,long minutes,string kind,string reason,bool confirmed)
 {
  using var c=Open();return BudgetStore(c).AppendBudget(project,operation,expected,reviewedSnapshotHash,minutes,kind,reason,confirmed);
 }
 public (EffortForecastEntry Entry,bool Replayed) RecordProjectEffortForecast(long project,Guid operation,string? expectedForecast,string expectedBudget,string reviewedSnapshotHash,IReadOnlyList<TaskRemainingInput> remaining,string reason,bool confirmed)
 {
  using var c=Open();return BudgetStore(c).AppendForecast(project,operation,expectedForecast,expectedBudget,reviewedSnapshotHash,remaining,reason,confirmed);
 }
}
