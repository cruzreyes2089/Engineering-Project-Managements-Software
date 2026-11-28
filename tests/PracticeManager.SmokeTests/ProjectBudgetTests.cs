using PracticeManager.Core;
internal static class ProjectBudgetTests
{
 public static void Run(Action<bool,string> check)
 {
  const long id=42;
  var open=new TaskEffort(1,"Design","Open","InProgress",120,60,15,0,0){Revision=2};
  var stopped=new TaskEffort(2,"Design","Stopped","Discontinued",40,20,0,0,0){Revision=3};
  var effort=new ProjectEffortReview("SYNTHETIC",new[]{open,stopped}){ProjectId=id};
  var hash=ProjectBudgetReview.SnapshotHash(id,effort);
  var reversed=effort with{Tasks=effort.Tasks.Reverse().ToArray()};
  check(hash==ProjectBudgetReview.SnapshotHash(id,reversed),"budget effort digest stable by task ID");
  var empty=Array.Empty<BudgetRevision>();
  var first=ProjectBudgetReview.PrepareRevision(id,empty,null,effort,hash,120,"FirstRecorded","Reviewed initial internal budget",true);
  var revised=ProjectBudgetReview.PrepareRevision(id,new[]{first},first.Hash,effort,hash,150,"ScopeReview","Reviewed additional budget",true);
  var history=new[]{first,revised};ProjectBudgetReview.ValidateHistory(id,history);
  check(first.Revision==1&&first.PreviousHash==null&&first.Minutes==120&&revised.Revision==2&&revised.PreviousHash==first.Hash&&revised.Minutes==150,"budget revision preserves first recorded baseline");
  var remaining=new[]{new TaskRemainingInput(1,80)};
  var forecast=ProjectBudgetReview.PrepareForecast(id,history,revised.Hash,effort,hash,remaining);
  check(forecast.TotalForecastMinutes==175&&forecast.VarianceFromCurrentMinutes==25&&forecast.VarianceFromFirstMinutes==55,"total forecast includes historical effort and compares both budget revisions");
  remaining[0]=new(1,0);
  check(forecast.Remaining.Single().Minutes==80,"forecast captures independent immutable input list");
  void Invalid(Action action,string name){try{action();}catch(ArgumentException){check(true,name);return;}throw new Exception("FAIL: "+name);}
  Invalid(()=>ProjectBudgetReview.PrepareRevision(id,history,first.Hash,effort,hash,200,"ScopeReview","Stale",true),"stale revision refused");
  Invalid(()=>ProjectBudgetReview.PrepareRevision(id,history,revised.Hash,effort,hash,150,"EstimateReview","No change",true),"unchanged budget refused");
  Invalid(()=>ProjectBudgetReview.PrepareRevision(id,empty,null,effort,hash,120,"FirstRecorded","Unconfirmed",false),"explicit budget confirmation required");
  Invalid(()=>ProjectBudgetReview.PrepareRevision(id,empty,null,effort,hash,120,"ScopeReview","Wrong category",true),"initial budget category required");
  Invalid(()=>ProjectBudgetReview.PrepareRevision(id,history,revised.Hash,effort,hash,200,"FirstRecorded","Rewrite original",true),"cannot reset baseline");
  Invalid(()=>ProjectBudgetReview.ValidateHistory(id,new[]{first,revised with{Minutes=151}}),"tampered budget revision hash rejected");
  Invalid(()=>ProjectBudgetReview.ValidateHistory(id,new[]{revised}),"truncated leading budget revision rejected");
  Invalid(()=>ProjectBudgetReview.ValidateHistory(id+1,history),"cross-project history rejected");
  var changed=effort with{Tasks=new[]{open with{Revision=3},stopped}};
  Invalid(()=>ProjectBudgetReview.PrepareForecast(id,history,revised.Hash,changed,hash,new[]{new TaskRemainingInput(1,80)}),"changed plan revision invalidates reviewed forecast input");
  Invalid(()=>ProjectBudgetReview.PrepareForecast(id,history,first.Hash,effort,hash,new[]{new TaskRemainingInput(1,80)}),"stale budget forecast refused");
  Invalid(()=>ProjectBudgetReview.PrepareForecast(id,history,revised.Hash,effort,hash,Array.Empty<TaskRemainingInput>()),"incomplete forecast refused for budget comparison");
  Invalid(()=>ProjectBudgetReview.SnapshotHash(id+1,effort),"cross-project effort identity refused");
 }
}
