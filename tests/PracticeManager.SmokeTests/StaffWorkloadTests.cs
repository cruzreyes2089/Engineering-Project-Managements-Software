using PracticeManager.Core;
internal static class StaffWorkloadTests
{
 public static void Run(Action<bool,string> check)
 {
  var monday=new DateOnly(2026,11,23);
  var people=new[]{new WorkloadEmployee(1,"EMP-1","Synthetic A",true),new WorkloadEmployee(2,"EMP-2","Synthetic B",true),new WorkloadEmployee(3,"EMP-3","Inactive",false)};
  ProjectTask Task(long id,long employee,DateOnly a,DateOnly b,int minutes,string state="Planned",string project="Active",string scope="Base scope",string conflict="Clear")
   =>new(id,1,"P-1",project,1,"Design",employee,"Synthetic","Task",a,b,minutes,state,1,null,scope,conflict);
  var tasks=new[]{
   Task(1,1,monday,monday.AddDays(2),100),
   Task(2,1,monday,monday,60),
   Task(3,1,monday,monday.AddDays(2),90,scope:"Proposed"),
   Task(4,1,monday.AddDays(5),monday.AddDays(6),45),
   Task(5,1,monday,monday,200,state:"Completed"),
   Task(6,1,monday,monday,200,state:"Cancelled"),
   Task(7,1,monday,monday,200,state:"Discontinued"),
   Task(8,3,monday,monday,60),
   Task(9,1,monday,monday,40,project:"OperationalClosed"),
   Task(10,1,monday,monday,20,conflict:"Hold")
  };
  var all=StaffWorkload.Build(monday,monday.AddDays(6),60,people,tasks);
  var a=all.Rows[0];var b=all.Rows[1];var inactive=all.Rows[2];
  check(all.Weekdays==5&&a.EligibleMinutes==160&&a.HeldMinutes==150&&a.WeekendOnlyEstimateMinutes==45,"workload separates eligible held and weekend-only estimates");
  check(a.EligibleTasks==3&&a.HeldTasks==3&&a.ScenarioCapacityMinutes==300&&a.OverCapacityWeekdays==1,"workload overlapping eligible tasks and daily capacity");
  check(b.EligibleMinutes==0&&b.ScenarioCapacityMinutes==300&&b.OverCapacityWeekdays==0,"active employee with no assignments remains visible");
  check(inactive.HeldMinutes==60&&inactive.ScenarioCapacityMinutes==null&&inactive.OverCapacityWeekdays==null,"inactive assigned staff shown without capacity");
  var first=StaffWorkload.Build(monday,monday,null,people,tasks).Rows[0];
  var rest=StaffWorkload.Build(monday.AddDays(1),monday.AddDays(6),null,people,tasks).Rows[0];
  check(first.EligibleMinutes==94&&rest.EligibleMinutes==66&&first.EligibleMinutes+rest.EligibleMinutes==a.EligibleMinutes,"weekday remainder allocation reconciles disjoint windows");
  check(first.ScenarioCapacityMinutes==null&&first.OverCapacityWeekdays==null,"no default capacity");
  try{StaffWorkload.Build(monday,monday,0,people,tasks);throw new Exception("FAIL: invalid capacity");}catch(ArgumentException){check(true,"invalid scenario capacity rejected");}
  try{StaffWorkload.Build(monday,monday.AddDays(366),60,people,tasks);throw new Exception("FAIL: range");}catch(ArgumentException){check(true,"oversized workload date range rejected");}
  try{StaffWorkload.Build(monday,monday,60,people,tasks.Append(tasks[0]).ToArray());throw new Exception("FAIL: duplicate");}catch(ArgumentException){check(true,"duplicate workload task identity rejected");}
 }
}
