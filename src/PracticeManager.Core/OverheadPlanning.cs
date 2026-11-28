namespace PracticeManager.Core;

// Only costs not already included in the recorded or forecast labor rates or
// project direct expenses belong in these additional assumptions.
public sealed record FirmOverheadAssumptions(decimal AdditionalLaborBurdenPercent,decimal OtherIndirectCost);
public sealed record FirmOverheadScenario(DateOnly Start,DateOnly End,long ProjectMinutes,long OverheadMinutes,
 long BillableMinutes,long UnclassifiedMinutes,int MissingRateRecords,decimal KnownProjectLabor,decimal KnownOverheadLabor,
 decimal? BurdenedProjectLabor,decimal? BurdenedOverheadLabor,decimal? IndirectPool,
 decimal? AllocationPerProjectHour,decimal? TotalModeledCost,decimal? RecoveryPerBillableHour,
 FirmOverheadAssumptions Assumptions);
public sealed record ProjectOverheadAssumptions(decimal AdditionalLaborBurdenPercent,decimal IndirectCostPerProjectHour);
public sealed record LoadedProjectCostScenario(ProjectCostScenario Direct,ProjectOverheadAssumptions Assumptions,
 decimal? AdditionalLaborBurden,decimal AllocatedIndirectCost,decimal? BurdenedDirectCost,
 decimal? TotalModeledCost,decimal? ContributionAfterAllocation,decimal? ContributionMarginAfterAllocation);
public static class OverheadPlanning
{
 static bool ValidMoney(decimal value)=>value>=0&&value<=10000000000m&&decimal.Round(value,2)==value;
 static bool ValidPercent(decimal value)=>value>=0&&value<=1000m&&decimal.Round(value,2)==value;
 static decimal Round(decimal value)=>decimal.Round(value,2,MidpointRounding.AwayFromZero);
 public static FirmOverheadScenario Firm(FirmLaborCost source,FirmOverheadAssumptions assumptions)
 {
  if(source.End<source.Start||source.End.DayNumber-source.Start.DayNumber>=366||
   !ValidPercent(assumptions.AdditionalLaborBurdenPercent)||!ValidMoney(assumptions.OtherIndirectCost)||
   source.Rows.Count>1000||source.Rows.Select(x=>x.Category).Distinct(StringComparer.Ordinal).Count()!=source.Rows.Count)
   throw new ArgumentException("Valid matching-period overhead assumptions required.");
  long project=0,overhead=0,billable=0,unclassified=0;decimal projectLabor=0,overheadLabor=0;int missing=0;
  foreach(var row in source.Rows)
  {
   if(row.ApprovedMinutes<0||row.MissingRateMinutes<0||row.MissingRateMinutes>row.ApprovedMinutes||
    row.MissingRateRecords<0||row.KnownCost<0||(row.MissingRateRecords==0)!=(row.MissingRateMinutes==0))
    throw new ArgumentException("Invalid labor cost evidence.");
   missing=checked(missing+row.MissingRateRecords);
   if(row.Category.StartsWith("Overhead: ",StringComparison.Ordinal))
   {overhead=checked(overhead+row.ApprovedMinutes);overheadLabor+=row.KnownCost;continue;}
   if(row.Category is not ("Project billable" or "Project nonbillable" or "Project unclassified"))
    throw new ArgumentException("Unknown labor cost classification.");
   project=checked(project+row.ApprovedMinutes);projectLabor+=row.KnownCost;
   if(row.Category=="Project billable")billable=checked(billable+row.ApprovedMinutes);
   if(row.Category=="Project unclassified")unclassified=checked(unclassified+row.ApprovedMinutes);
  }
  decimal? loadedProject=missing==0?projectLabor+Round(projectLabor*assumptions.AdditionalLaborBurdenPercent/100m):null;
  decimal? loadedOverhead=missing==0?overheadLabor+Round(overheadLabor*assumptions.AdditionalLaborBurdenPercent/100m):null;
  decimal? pool=loadedOverhead+assumptions.OtherIndirectCost;
  decimal? total=loadedProject+pool;
  // Retain full decimal precision until the resulting allocation is rounded.
  decimal? allocation=project>0?pool*60m/project:null;
  decimal? recovery=billable>0&&unclassified==0?total*60m/billable:null;
  return new(source.Start,source.End,project,overhead,billable,unclassified,missing,projectLabor,overheadLabor,
   loadedProject,loadedOverhead,pool,allocation,total,recovery,assumptions);
 }
 public static LoadedProjectCostScenario Project(ProjectCostScenario source,ProjectOverheadAssumptions assumptions)
 {
  if(!ValidPercent(assumptions.AdditionalLaborBurdenPercent)||assumptions.IndirectCostPerProjectHour<0||
     assumptions.IndirectCostPerProjectHour>10000000000m||source.ForecastMinutes<0)
   throw new ArgumentException("Valid explicit additional burden and overhead allocation required.");
  decimal? burden=source.EstimatedDirectCost.HasValue?
   Round((source.Evidence.ApprovedKnownLabor+source.Evidence.PendingKnownLabor+source.RemainingLabor)*assumptions.AdditionalLaborBurdenPercent/100m):null;
  var allocation=Round(source.ForecastMinutes*assumptions.IndirectCostPerProjectHour/60m);
  decimal? direct=source.EstimatedDirectCost+burden;
  decimal? total=direct+allocation;
  decimal? contribution=source.Assumptions.RevenueExcludingTax-total;
  decimal? margin=source.Assumptions.RevenueExcludingTax>0&&contribution.HasValue?
   Round(contribution.Value/source.Assumptions.RevenueExcludingTax*100m):null;
  return new(source,assumptions,burden,allocation,direct,total,contribution,margin);
 }
}
