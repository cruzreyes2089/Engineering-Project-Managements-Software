using PracticeManager.Core;
internal static class ProjectCostScenarioTests
{
 public static void Run(Action<bool,string> check)
 {
  var evidence=new ProjectCostEvidence(60,30,35m,17.50m,0,0,20m,10m);
  var assumptions=new ProjectCostAssumptions(1000m,50m,40m);
  var s=ProjectCostPlanning.Calculate("SYNTHETIC","synthetic",150,60,evidence,assumptions);
  check(s.RemainingLabor==50m&&s.EstimatedDirectCost==172.50m&&s.ProjectedDirectContribution==827.50m&&s.ProjectedDirectContributionPercent==82.75m,"direct-cost scenario reconciles all cost components");
  var missing=ProjectCostPlanning.Calculate("SYNTHETIC","synthetic",150,60,evidence with{PendingMissingRates=1},assumptions);
  check(missing.EstimatedDirectCost==null&&missing.ProjectedDirectContribution==null&&missing.ProjectedDirectContributionPercent==null,"missing recorded rates suppress cost and contribution totals");
  var loss=ProjectCostPlanning.Calculate("SYNTHETIC","synthetic",150,60,evidence,assumptions with{RevenueExcludingTax=100m});
  check(loss.ProjectedDirectContribution==-72.50m&&loss.ProjectedDirectContributionPercent==-72.50m,"negative contribution retained");
  var zero=ProjectCostPlanning.Calculate("SYNTHETIC","synthetic",150,60,evidence,assumptions with{RevenueExcludingTax=0});
  check(zero.ProjectedDirectContribution==-172.50m&&zero.ProjectedDirectContributionPercent==null,"zero revenue never yields fictitious margin");
  void Invalid(Action a,string label){try{a();}catch(ArgumentException){check(true,label);return;}throw new Exception("FAIL: "+label);}
  Invalid(()=>ProjectCostPlanning.Calculate("SYNTHETIC","synthetic",151,60,evidence,assumptions),"forecast effort reconciliation required");
  Invalid(()=>ProjectCostPlanning.Calculate("SYNTHETIC","synthetic",150,60,evidence,assumptions with{RemainingHourlyCost=-1}),"negative cost refused");
  Invalid(()=>ProjectCostPlanning.Calculate("SYNTHETIC","synthetic",150,60,evidence,assumptions with{RevenueExcludingTax=1.001m}),"fractional cent input refused");
 }
}
