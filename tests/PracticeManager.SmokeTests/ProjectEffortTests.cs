using PracticeManager.Core;
internal static class ProjectEffortTests
{
 public static void Run(Action<bool,string> check)
 {
  var r=new ProjectEffortReview("SYNTHETIC",new[]{
   new TaskEffort(1,"Design","Open","InProgress",120,60,15,10,20),
   new TaskEffort(2,"Design","Open 2","Planned",60,0,0,0,0),
   new TaskEffort(3,"Design","Complete","Completed",90,100,5,0,0),
   new TaskEffort(4,"Design","Stopped","Discontinued",80,40,10,0,0),
   new TaskEffort(5,"Design","Cancelled","Cancelled",30,0,0,0,0)
  });
  check(r.CurrentPlanMinutes==270&&r.CurrentApprovedMinutes==160&&r.CurrentPendingMinutes==20&&r.HistoricalApprovedMinutes==40&&r.HistoricalPendingMinutes==10,"effort current and historical scope separated");
  check(r.RejectedMinutes==10&&r.InvalidatedMinutes==20&&r.OpenTasks==2,"effort review statuses retained independently");
  var incomplete=ProjectEffort.Forecast(r,ProjectEffort.ParseRemaining("1,90"));
  check(incomplete.MissingOpenTasks==1&&incomplete.EnteredRemainingMinutes==90&&incomplete.CurrentForecastMinutes==null&&incomplete.TotalProjectForecastMinutes==null,"missing remaining effort prevents total forecast");
  var full=ProjectEffort.Forecast(r,ProjectEffort.ParseRemaining("1,90\n2,0"));
  check(full.MissingOpenTasks==0&&full.CurrentForecastMinutes==270&&full.CurrentForecastVarianceMinutes==0&&full.TotalProjectForecastMinutes==320,"explicit zero and pending assumption reconcile forecast");
  void Invalid(Action a,string name){try{a();}catch(ArgumentException){check(true,name);return;}throw new Exception("FAIL: "+name);}
  Invalid(()=>ProjectEffort.Forecast(r,ProjectEffort.ParseRemaining("1,20\n1,30")),"duplicate forecast task rejected");
  Invalid(()=>ProjectEffort.Forecast(r,ProjectEffort.ParseRemaining("3,0")),"completed task remaining input rejected");
  Invalid(()=>ProjectEffort.Forecast(r,ProjectEffort.ParseRemaining("4,0")),"historical task remaining input rejected");
  Invalid(()=>ProjectEffort.Forecast(r,ProjectEffort.ParseRemaining("99,0")),"unknown task remaining input rejected");
  Invalid(()=>ProjectEffort.ParseRemaining("1,-1"),"negative remaining input rejected");
 }
}
