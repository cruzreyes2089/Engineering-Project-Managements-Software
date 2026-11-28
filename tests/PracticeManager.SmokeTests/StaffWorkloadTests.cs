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
  var calendar=StaffCapacityCalendar.Parse("EMP-1,120,0,60,60,60,0,0","2026-11-25","EMP-1,2026-11-23,30");
  var adjusted=StaffWorkload.Build(monday,monday.AddDays(6),60,people,tasks,calendar);
  var individual=adjusted.Rows[0];var common=adjusted.Rows[1];
  check(individual.EligibleMinutes==160&&individual.HeldMinutes==150&&individual.WeekendOnlyEstimateMinutes==45,"calendar changes capacity without silently reallocating demand");
  check(individual.ScenarioCapacityMinutes==150&&individual.OverCapacityWeekdays==3&&individual.OverCapacityMinutes==130&&individual.UnavailableAllocationMinutes==66,"individual calendar detects overload and unavailable-day demand");
  check(common.ScenarioCapacityMinutes==240&&common.CapacitySource=="Common weekdays"&&individual.CapacitySource=="Individual week","firm closure applies to both individual and common capacity");
  check(individual.Days.Count==7&&individual.Days.Select(d=>d.Date).SequenceEqual(Enumerable.Range(0,7).Select(i=>monday.AddDays(i))),"daily workload covers inclusive review range");
  var mon=individual.Days[0];var tue=individual.Days[1];var wed=individual.Days[2];
  check(mon.EligibleMinutes==94&&mon.HeldMinutes==90&&mon.CapacityMinutes==30&&mon.ExcessMinutes==64&&mon.CapacityBasis=="Individual date override"&&mon.Status=="Over capacity","daily override explains exact overload");
  check(tue.EligibleMinutes==33&&tue.HeldMinutes==30&&tue.CapacityMinutes==0&&tue.ExcessMinutes==33&&tue.Status=="Demand on unavailable day","daily zero schedule flags demand");
  check(wed.EligibleMinutes==33&&wed.CapacityMinutes==0&&wed.CapacityBasis=="Firm closure"&&wed.ExcessMinutes==33,"daily closure explains overload");
  check(individual.Days.Sum(d=>d.EligibleMinutes)==individual.EligibleMinutes&&individual.Days.Sum(d=>d.HeldMinutes)==individual.HeldMinutes&&individual.Days.Sum(d=>d.CapacityMinutes)==individual.ScenarioCapacityMinutes&&individual.Days.Sum(d=>d.ExcessMinutes)==individual.OverCapacityMinutes,"daily totals reconcile to employee summary");
  check(individual.Days.Count(d=>d.ExcessMinutes is >0)==individual.OverCapacityWeekdays,"daily issue count reconciles to summary");
  check(individual.Days.Skip(5).All(d=>d.EligibleMinutes==0&&d.HeldMinutes==0),"weekend-only estimate remains separate");
  check(adjusted.Rows[2].Days[0].HeldMinutes==60&&adjusted.Rows[2].Days.All(d=>d.CapacityMinutes==null&&d.ExcessMinutes==null&&d.Status=="Unknown capacity"),"inactive employee daily capacity is unknown");
  check(first.Days.Single().CapacityMinutes==null&&first.Days.Single().ExcessMinutes==null,"unspecified daily capacity is not zero");

  var noCommon=StaffWorkload.Build(monday,monday.AddDays(6),null,people,tasks,calendar);
  check(noCommon.Rows[0].ScenarioCapacityMinutes==150&&noCommon.Rows[1].ScenarioCapacityMinutes==null,"individual capacity does not imply another employee schedule");
  void Invalid(Action action,string name){try{action();}catch(ArgumentException){check(true,name);return;}throw new Exception("FAIL: "+name);}
  StaffCapacityCalendar Parse(string w,string h="",string e="")=>StaffCapacityCalendar.Parse(w,h,e);
  void Review(StaffCapacityCalendar cal,int? fallback=60)=>StaffWorkload.Build(monday,monday.AddDays(6),fallback,people,tasks,cal);
  Invalid(()=>Review(Parse("EMP-1,1,1,1,1,1,0,0\nEMP-1,1,1,1,1,1,0,0")),"duplicate employee week rejected");
  Invalid(()=>Review(Parse("EMP-3,1,1,1,1,1,0,0")),"inactive calendar identity rejected");
  Invalid(()=>Review(Parse("UNKNOWN,1,1,1,1,1,0,0")),"unknown calendar identity rejected");
  Invalid(()=>Review(Parse("","2026-11-25","EMP-1,2026-11-25,30")),"firm closure cannot be overridden");
  Invalid(()=>Review(Parse("","","EMP-1,2026-11-23,0"),null),"override without base capacity rejected");
  Invalid(()=>Review(Parse("","2026-11-30")),"out-of-range closure rejected");
  Invalid(()=>Review(Parse("","","EMP-1,2026-11-23,0\nEMP-1,2026-11-23,30")),"duplicate dated override rejected");
  Invalid(()=>Parse("EMP-1,1441,0,0,0,0,0,0"),"invalid capacity input rejected");
  Invalid(()=>Parse("","11/25/2026"),"noncanonical closure date rejected");

  try{StaffWorkload.Build(monday,monday,0,people,tasks);throw new Exception("FAIL: invalid capacity");}catch(ArgumentException){check(true,"invalid scenario capacity rejected");}
  try{StaffWorkload.Build(monday,monday.AddDays(366),60,people,tasks);throw new Exception("FAIL: range");}catch(ArgumentException){check(true,"oversized workload date range rejected");}
  try{StaffWorkload.Build(monday,monday,60,people,tasks.Append(tasks[0]).ToArray());throw new Exception("FAIL: duplicate");}catch(ArgumentException){check(true,"duplicate workload task identity rejected");}
 }
}
