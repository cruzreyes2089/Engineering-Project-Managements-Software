namespace PracticeManager.Core;

public sealed record WorkloadEmployee(long Id,string Number,string Name,bool Active);
public sealed record StaffWorkloadRow(long EmployeeId,string EmployeeNumber,string EmployeeName,bool Active,
 int EligibleTasks,int HeldTasks,long EligibleMinutes,long HeldMinutes,long WeekendOnlyEstimateMinutes,
 long? ScenarioCapacityMinutes,int? OverCapacityWeekdays)
{
 public decimal EligibleHours=>EligibleMinutes/60m;
 public decimal HeldHours=>HeldMinutes/60m;
 public decimal WeekendOnlyEstimateHours=>WeekendOnlyEstimateMinutes/60m;
 public decimal? ScenarioCapacityHours=>ScenarioCapacityMinutes/60m;
}
public sealed record StaffWorkloadReview(DateOnly Start,DateOnly End,int Weekdays,int? ScenarioMinutesPerWeekday,
 IReadOnlyList<StaffWorkloadRow> Rows);

// A planning scenario, not remaining effort, actual time, payroll, authorization or a work calendar.
public static class StaffWorkload
{
 static bool Weekday(int day)=>DateOnly.FromDayNumber(day).DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);
 public static StaffWorkloadReview Build(DateOnly start,DateOnly end,int? minutesPerWeekday,
  IReadOnlyList<WorkloadEmployee> employees,IReadOnlyList<ProjectTask> tasks)
 {
  if(start==DateOnly.MinValue||end<start||end.DayNumber-start.DayNumber>=366||
     minutesPerWeekday is <1 or >1440||employees.Count>500||tasks.Count>500)
   throw new ArgumentException("Valid range up to 366 days, optional 1–1440 capacity minutes per weekday and bounded register required.");
  if(employees.Select(x=>x.Id).Distinct().Count()!=employees.Count||tasks.Select(x=>x.Id).Distinct().Count()!=tasks.Count)
   throw new ArgumentException("Duplicate planning identity.");
  var byId=employees.ToDictionary(x=>x.Id);
  var open=new Dictionary<long,List<ProjectTask>>();
  foreach(var task in tasks)
  {
   if(!byId.ContainsKey(task.EmployeeId)||task.Start==DateOnly.MinValue||task.End<task.Start||
      task.End.DayNumber-task.Start.DayNumber>=3660||task.EstimateMinutes is <1 or >600000||
      task.State is not ("Planned" or "InProgress" or "Completed" or "Cancelled" or "Discontinued"))
    throw new ArgumentException("Invalid planning register.");
   if(task.State is not ("Planned" or "InProgress")||task.Start>end||task.End<start)continue;
   if(!open.TryGetValue(task.EmployeeId,out var group))open[task.EmployeeId]=group=new();
   group.Add(task);
  }
  var first=start.DayNumber;var last=end.DayNumber;
  int weekdays=Enumerable.Range(first,last-first+1).Count(Weekday);
  var rows=new List<StaffWorkloadRow>();
  foreach(var employee in employees.OrderBy(x=>x.Number,StringComparer.Ordinal).ThenBy(x=>x.Id))
  {
   var group=open.GetValueOrDefault(employee.Id)??new List<ProjectTask>();
   if(!employee.Active&&group.Count==0)continue;
   var daily=new long[last-first+1];long eligible=0,held=0,weekendOnly=0;int eligibleCount=0,heldCount=0;
   foreach(var task in group)
   {
    // Planning eligibility labels reflect current schedule gates only; no independent work clearance.
    bool ready=employee.Active&&task.ProjectState=="Active"&&task.ConflictStatus=="Clear"&&
     task.ScopeState is "Base scope" or "Authorized";
    if(ready)eligibleCount++;else heldCount++;
    var a=task.Start.DayNumber;var b=task.End.DayNumber;
    int workdays=Enumerable.Range(a,b-a+1).Count(Weekday);
    if(workdays==0){weekendOnly+=task.EstimateMinutes;continue;}
    // Allocate integer minutes across the entire original task, remainder to earliest weekdays.
    // This makes disjoint review windows reconcile to the original estimate.
    int perDay=task.EstimateMinutes/workdays,remainder=task.EstimateMinutes%workdays;
    int ordinal=0;
    for(int day=a;day<=b;day++)
    {
     if(!Weekday(day))continue;
     int allocated=perDay+(ordinal++<remainder?1:0);
     if(day<first||day>last)continue;
     if(ready){eligible+=allocated;daily[day-first]+=allocated;}else held+=allocated;
    }
   }
   long? capacity=employee.Active&&minutesPerWeekday.HasValue?(long)weekdays*minutesPerWeekday.Value:null;
   int? overloaded=capacity.HasValue?daily.Count(m=>m>minutesPerWeekday!.Value):null;
   rows.Add(new(employee.Id,employee.Number,employee.Name,employee.Active,eligibleCount,heldCount,eligible,held,weekendOnly,capacity,overloaded));
  }
  return new(start,end,weekdays,minutesPerWeekday,rows.AsReadOnly());
 }
}
