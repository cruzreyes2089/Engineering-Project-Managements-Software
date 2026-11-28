using Microsoft.Data.Sqlite;
using PracticeManager.Core;
namespace PracticeManager.Persistence;
public sealed partial class LocalPractice
{
 // All inputs and current recorded evidence are read in a single transaction.
 // No financial data is persisted, exported, posted or returned to a budget-only viewer.
 public ProjectCostScenario ReadProjectCostScenario(long project,ProjectCostAssumptions assumptions)
 {
  using var c=Open();using var t=c.BeginTransaction(deferred:true);
  RequireEffortBudget(c,t,project,false);Require(c,t,"Reports.Financial");Require(c,t,"Billing.Rates");
  var history=BudgetStore(c).ReadCore(t,project);
  var budget=history.Budgets.LastOrDefault()??throw new ArgumentException("Record an internal labor budget first.");
  var forecast=history.Forecasts.LastOrDefault()??throw new ArgumentException("Record a current effort forecast first.");
  var effort=ReadProjectEffortCore(c,t,project);
  if(forecast.Model.BudgetHash!=budget.Model.Hash||forecast.Model.EffortSnapshotHash!=ProjectBudgetReview.SnapshotHash(project,effort))
   throw new ArgumentException("Saved effort forecast is stale. Record a reviewed current forecast first.");
  long approved=0,pending=0;decimal approvedLabor=0,pendingLabor=0,approvedExpenses=0,pendingExpenses=0;
  int missingApproved=0,missingPending=0,count=0;
  using(var q=Command(c,t,@"SELECT s.kind,s.minutes,s.amount_cents,coalesce(r.decision,'Submitted'),
   (SELECT z.cost_cents FROM employee_rates z WHERE z.employee_id=s.employee_id AND z.effective_on<=s.work_date ORDER BY z.effective_on DESC LIMIT 1)
   FROM work_submissions s JOIN project_tasks w ON w.id=s.task_id JOIN project_phases h ON h.id=w.phase_id
   LEFT JOIN work_reviews r ON r.submission_id=s.id WHERE h.project_id=$p
   AND NOT EXISTS(SELECT 1 FROM work_invalidations z WHERE z.submission_id=s.id)
   AND (r.submission_id IS NULL OR r.decision='Approved') ORDER BY s.id LIMIT 100001",("$p",project)))
  using(var r=q.ExecuteReader())while(r.Read())
  {
   if(++count>100000)throw new ArgumentException("Project cost evidence exceeds review limit.");
   bool isApproved=r.GetString(3)=="Approved";
   switch(r.GetString(0))
   {
    case "Expense":
     var amount=r.GetInt64(2);if(amount<0)throw new InvalidOperationException("Invalid recorded expense.");
     if(isApproved)approvedExpenses+=amount/100m;else pendingExpenses+=amount/100m;
     break;
    case "Time":
     var minutes=r.GetInt64(1);if(minutes is <1 or >1440)throw new InvalidOperationException("Invalid recorded time.");
     if(isApproved)approved+=minutes;else pending+=minutes;
     if(r.IsDBNull(4)){if(isApproved)missingApproved++;else missingPending++;break;}
     var rate=r.GetInt64(4);if(rate is <0 or >100000000)throw new InvalidOperationException("Invalid recorded cost rate.");
     var cost=decimal.Round(minutes*rate/60m,0,MidpointRounding.AwayFromZero)/100m;
     if(isApproved)approvedLabor+=cost;else pendingLabor+=cost;
     break;
    default:throw new InvalidOperationException("Unknown cost evidence.");
   }
  }
  if(approved!=effort.CurrentApprovedMinutes+effort.HistoricalApprovedMinutes||pending!=effort.CurrentPendingMinutes+effort.HistoricalPendingMinutes)
   throw new InvalidOperationException("Recorded labor does not reconcile with effort forecast.");
  return ProjectCostPlanning.Calculate(effort.ProjectNumber,forecast.Hash,forecast.Model.TotalForecastMinutes,forecast.Model.Remaining.Sum(x=>(long)x.Minutes),
   new(approved,pending,approvedLabor,pendingLabor,missingApproved,missingPending,approvedExpenses,pendingExpenses),assumptions);
 }
}
