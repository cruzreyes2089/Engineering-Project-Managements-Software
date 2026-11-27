namespace PracticeManager.Core;

public sealed record WorkloadEmployee(long Id,string Number,string Name,bool Active);
public sealed record StaffWorkloadDay(DateOnly Date,long EligibleMinutes,long HeldMinutes,int? CapacityMinutes,string CapacityBasis)
{
 public decimal EligibleHours=>EligibleMinutes/60m;
 public decimal HeldHours=>HeldMinutes/60m;
 public decimal? CapacityHours=>CapacityMinutes/60m;
 public long? ExcessMinutes=>CapacityMinutes.HasValue?Math.Max(0,EligibleMinutes-CapacityMinutes.Value):null;
 public decimal? ExcessHours=>ExcessMinutes/60m;
 public string Status=>!CapacityMinutes.HasValue?"Unknown capacity":
  EligibleMinutes>0&&CapacityMinutes.Value==0?"Demand on unavailable day":
  EligibleMinutes>CapacityMinutes.Value?"Over capacity":CapacityMinutes.Value==0?"Unavailable":"Within capacity";
}
public sealed record StaffWorkloadRow(long EmployeeId,string EmployeeNumber,string EmployeeName,bool Active,
 int EligibleTasks,int HeldTasks,long EligibleMinutes,long HeldMinutes,long WeekendOnlyEstimateMinutes,
 long? ScenarioCapacityMinutes,int? OverCapacityWeekdays)
{
 public decimal EligibleHours=>EligibleMinutes/60m;
 public decimal HeldHours=>HeldMinutes/60m;
 public decimal WeekendOnlyEstimateHours=>WeekendOnlyEstimateMinutes/60m;
 public decimal? ScenarioCapacityHours=>ScenarioCapacityMinutes/60m;
 public string CapacitySource{get;init;}="Not specified";
 public IReadOnlyList<StaffWorkloadDay> Days{get;init;}=Array.Empty<StaffWorkloadDay>();
 public long? UnavailableAllocationMinutes{get;init;}
 public long? OverCapacityMinutes{get;init;}
 public decimal? UnavailableAllocationHours=>UnavailableAllocationMinutes/60m;
 public decimal? OverCapacityHours=>OverCapacityMinutes/60m;
}
public sealed record StaffWorkloadReview(DateOnly Start,DateOnly End,int Weekdays,int? ScenarioMinutesPerWeekday,
 IReadOnlyList<StaffWorkloadRow> Rows);

// A planning scenario, not remaining effort, actual time, payroll, authorization or an approved work calendar.
public static class StaffWorkload
{
 static bool Weekday(int day)=>DateOnly.FromDayNumber(day).DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);
 public static StaffWorkloadReview Build(DateOnly start,DateOnly end,int? minutesPerWeekday,
  IReadOnlyList<WorkloadEmployee> employees,IReadOnlyList<ProjectTask> tasks,StaffCapacityCalendar? calendar=null)
 {
  if(start==DateOnly.MinValue||end<start||end.DayNumber-start.DayNumber>=366||
     minutesPerWeekday is <1 or >1440||employees.Count>500||tasks.Count>500)
   throw new ArgumentException("Valid range up to 366 days, optional 1–1440 capacity minutes per weekday and bounded register required.");
  if(employees.Select(x=>x.Id).Distinct().Count()!=employees.Count||tasks.Select(x=>x.Id).Distinct().Count()!=tasks.Count)
   throw new ArgumentException("Duplicate planning identity.");
  var byId=employees.ToDictionary(x=>x.Id);
  var weeks=new Dictionary<string,EmployeeCapacityWeek>(StringComparer.Ordinal);
  var closures=new HashSet<DateOnly>();
  var exceptions=new Dictionary<(string,DateOnly),int>();
  if(calendar!=null)
  {
   if(calendar.Weeks.Count>500||calendar.Closures.Count>366||calendar.Exceptions.Count>2000)
    throw new ArgumentException("Calendar scenario row limit exceeded.");
   if(employees.Select(x=>x.Number).Distinct(StringComparer.Ordinal).Count()!=employees.Count)
    throw new ArgumentException("Duplicate employee number.");
   var active=employees.Where(x=>x.Active).Select(x=>x.Number).ToHashSet(StringComparer.Ordinal);
   foreach(var week in calendar.Weeks)
   {
    if(!active.Contains(week.EmployeeNumber)||!weeks.TryAdd(week.EmployeeNumber,week)||
       Enum.GetValues<DayOfWeek>().Any(d=>week.Minutes(d) is <0 or >1440))
     throw new ArgumentException("Invalid or duplicate active employee capacity schedule.");
   }
   foreach(var date in calendar.Closures)
    if(date<start||date>end||!closures.Add(date))throw new ArgumentException("Duplicate or out-of-range firm closure.");
   foreach(var item in calendar.Exceptions)
   {
    if(!active.Contains(item.EmployeeNumber)||(!weeks.ContainsKey(item.EmployeeNumber)&&!minutesPerWeekday.HasValue)||
       item.Date<start||item.Date>end||closures.Contains(item.Date)||item.Minutes is <0 or >1440||
       !exceptions.TryAdd((item.EmployeeNumber,item.Date),item.Minutes))
     throw new ArgumentException("Invalid or duplicate calendar exception; firm closures cannot be overridden and a base capacity is required.");
   }
  }
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
   var daily=new long[last-first+1];var dailyHeld=new long[daily.Length];long eligible=0,held=0,weekendOnly=0;int eligibleCount=0,heldCount=0;
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
     if(ready){eligible+=allocated;daily[day-first]+=allocated;}else{held+=allocated;dailyHeld[day-first]+=allocated;}
    }
   }
   var individual=weeks.GetValueOrDefault(employee.Number);
   long? capacity=null,unavailable=null,excess=null;int? overloaded=null;
   string source="Not specified";
   bool known=employee.Active&&(individual!=null||minutesPerWeekday.HasValue);
   if(known)
   {
    capacity=0;unavailable=0;excess=0;overloaded=0;
    source=individual!=null?"Individual week":"Common weekdays";
   }
   var days=new List<StaffWorkloadDay>(daily.Length);
   for(int day=first;day<=last;day++)
   {
    var date=DateOnly.FromDayNumber(day);var demand=daily[day-first];int? available=null;
    string basis=employee.Active?"Not specified":"Inactive employee";
    if(known)
    {
     available=individual!=null?individual.Minutes(date.DayOfWeek):Weekday(day)?minutesPerWeekday!.Value:0;
     basis=source;
     if(closures.Contains(date)){available=0;basis="Firm closure";}
     else if(exceptions.TryGetValue((employee.Number,date),out var change)){available=change;basis="Individual date override";}
     capacity+=available.Value;
     if(available.Value==0)unavailable+=demand;
     if(demand>available.Value){overloaded++;excess+=demand-available.Value;}
    }
    days.Add(new(date,demand,dailyHeld[day-first],available,basis));
   }
   rows.Add(new StaffWorkloadRow(employee.Id,employee.Number,employee.Name,employee.Active,eligibleCount,heldCount,eligible,held,weekendOnly,capacity,overloaded)
   {CapacitySource=source,UnavailableAllocationMinutes=unavailable,OverCapacityMinutes=excess,Days=days.AsReadOnly()});
  }
  return new(start,end,weekdays,minutesPerWeekday,rows.AsReadOnly());
 }
}
