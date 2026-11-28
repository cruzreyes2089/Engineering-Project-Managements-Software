using PracticeManager.Core;
internal static class OverheadPlanningTests
{
 public static void Run(Action<bool,string> check)
 {
  var day=new DateOnly(2026,11,26);
  var source=new FirmLaborCost(day,day,new[]{
   new FirmLaborCostRow("Project billable",480,0,0,280m),
   new FirmLaborCostRow("Project nonbillable",120,0,0,70m),
   new FirmLaborCostRow("Overhead: Administration",120,0,0,70m)});
  var firm=OverheadPlanning.Firm(source,new(20m,116m));
  check(firm.ProjectMinutes==600&&firm.OverheadMinutes==120&&firm.BillableMinutes==480&&firm.BurdenedProjectLabor==420m&&firm.BurdenedOverheadLabor==84m,"firm labor burden separates project and overhead time");
  check(firm.IndirectPool==200m&&firm.AllocationPerProjectHour==20m&&firm.TotalModeledCost==620m&&firm.RecoveryPerBillableHour==77.50m,"firm overhead allocation reconciles pool and billable recovery denominator");
  var unclassified=OverheadPlanning.Firm(source with{Rows=source.Rows.Append(new FirmLaborCostRow("Project unclassified",60,0,0,35m)).ToArray()},new(20m,116m));
  check(unclassified.ProjectMinutes==660&&unclassified.AllocationPerProjectHour.HasValue&&unclassified.RecoveryPerBillableHour==null,"unclassified time blocks billable recovery not all-project allocation");
  var missing=OverheadPlanning.Firm(source with{Rows=new[]{new FirmLaborCostRow("Project billable",60,60,1,0)}},new(0,100));
  check(missing.TotalModeledCost==null&&missing.IndirectPool==null&&missing.AllocationPerProjectHour==null&&missing.RecoveryPerBillableHour==null,"missing rate blocks complete firm overhead conclusions");
  var noProject=OverheadPlanning.Firm(source with{Rows=new[]{source.Rows[2]}},new(0,100));
  check(noProject.IndirectPool==170m&&noProject.AllocationPerProjectHour==null&&noProject.RecoveryPerBillableHour==null,"no project hours never implies zero overhead rate");
  var nonbillable=OverheadPlanning.Firm(source with{Rows=new[]{source.Rows[1],source.Rows[2]}},new(0,100));
  check(nonbillable.AllocationPerProjectHour==85m&&nonbillable.RecoveryPerBillableHour==null,"zero billable hours suppress billable cost recovery");
  var direct=ProjectCostPlanning.Calculate("SYNTHETIC","fixture",150,60,new(60,30,35m,17.50m,0,0,20m,10m),new(1000m,50m,40m));
  var loaded=OverheadPlanning.Project(direct,new(20m,firm.AllocationPerProjectHour!.Value));
  check(loaded.AdditionalLaborBurden==20.50m&&loaded.BurdenedDirectCost==193m&&loaded.AllocatedIndirectCost==50m&&loaded.TotalModeledCost==243m&&loaded.ContributionAfterAllocation==757m&&loaded.ContributionMarginAfterAllocation==75.70m,"project overhead applies labor-only burden and allocation on all forecast hours");
  var directMissing=ProjectCostPlanning.Calculate("SYNTHETIC","fixture",150,60,direct.Evidence with{PendingMissingRates=1},direct.Assumptions);
  var incomplete=OverheadPlanning.Project(directMissing,new(20,20));
  check(incomplete.AllocatedIndirectCost==50m&&incomplete.AdditionalLaborBurden==null&&incomplete.TotalModeledCost==null&&incomplete.ContributionAfterAllocation==null,"missing project rate blocks loaded total and margin");
  var zero=OverheadPlanning.Project(direct,new(0,0));
  check(zero.TotalModeledCost==direct.EstimatedDirectCost&&zero.ContributionAfterAllocation==direct.ProjectedDirectContribution,"explicit zero additional costs preserves direct scenario");
  void Invalid(Action action,string label){try{action();}catch(ArgumentException){check(true,label);return;}throw new Exception("FAIL: "+label);}
  Invalid(()=>OverheadPlanning.Firm(source,new(-1,0)),"negative firm burden refused");
  Invalid(()=>OverheadPlanning.Firm(source,new(0,1.001m)),"fractional cent overhead pool refused");
  Invalid(()=>OverheadPlanning.Project(direct,new(0,-1)),"negative allocation refused");
  Invalid(()=>OverheadPlanning.Firm(source with{Rows=source.Rows.Append(source.Rows[0]).ToArray()},new(0,0)),"duplicate labor category rejected");
 }
}
