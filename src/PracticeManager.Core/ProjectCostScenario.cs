namespace PracticeManager.Core;

public sealed record ProjectCostEvidence(long ApprovedMinutes,long PendingMinutes,decimal ApprovedKnownLabor,decimal PendingKnownLabor,
 int ApprovedMissingRates,int PendingMissingRates,decimal ApprovedExpenses,decimal PendingExpenses);
public sealed record ProjectCostAssumptions(decimal RevenueExcludingTax,decimal RemainingHourlyCost,decimal FutureUnsubmittedDirectCost);
public sealed record ProjectCostScenario(string ProjectNumber,string ForecastHash,long ForecastMinutes,long RemainingMinutes,
 ProjectCostEvidence Evidence,ProjectCostAssumptions Assumptions,decimal RemainingLabor,decimal? EstimatedDirectCost,
 decimal? ProjectedDirectContribution,decimal? ProjectedDirectContributionPercent);
public static class ProjectCostPlanning
{
 static bool Money(decimal amount)=>amount>=0&&amount<=10000000000m&&decimal.Round(amount,2)==amount;
 public static ProjectCostScenario Calculate(string project,string forecastHash,long forecastMinutes,long remainingMinutes,
  ProjectCostEvidence evidence,ProjectCostAssumptions assumptions)
 {
  if(!Money(assumptions.RevenueExcludingTax)||!Money(assumptions.RemainingHourlyCost)||!Money(assumptions.FutureUnsubmittedDirectCost)
   ||remainingMinutes is <0 or >300000000||evidence.ApprovedMinutes<0||evidence.PendingMinutes<0||
   evidence.ApprovedMissingRates<0||evidence.PendingMissingRates<0||evidence.ApprovedKnownLabor<0||evidence.PendingKnownLabor<0||
   evidence.ApprovedExpenses<0||evidence.PendingExpenses<0||
   (decimal)evidence.ApprovedMinutes+evidence.PendingMinutes+remainingMinutes!=forecastMinutes)
   throw new ArgumentException("Valid explicit USD assumptions and reconciled effort evidence required.");
  var remaining=decimal.Round(remainingMinutes*assumptions.RemainingHourlyCost/60m,2,MidpointRounding.AwayFromZero);
  decimal? cost=evidence.ApprovedMissingRates==0&&evidence.PendingMissingRates==0?
   evidence.ApprovedKnownLabor+evidence.PendingKnownLabor+remaining+evidence.ApprovedExpenses+evidence.PendingExpenses+assumptions.FutureUnsubmittedDirectCost:null;
  decimal? contribution=assumptions.RevenueExcludingTax-cost;
  decimal? percent=assumptions.RevenueExcludingTax>0&&contribution.HasValue?decimal.Round(contribution.Value/assumptions.RevenueExcludingTax*100m,2):null;
  return new(project,forecastHash,forecastMinutes,remainingMinutes,evidence,assumptions,remaining,cost,contribution,percent);
 }
}
