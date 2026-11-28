namespace PracticeManager.Core;
public sealed record ProjectPhase(long Id,long ProjectId,string Name,DateOnly Start,DateOnly End,int Revision);
public sealed record ProjectTaskPlan(long PhaseId,long EmployeeId,string Title,DateOnly Start,DateOnly End,int EstimateMinutes,long? ScopeChangeId=null);
public sealed record ProjectTask(long Id,long ProjectId,string ProjectNumber,string ProjectState,long PhaseId,string Phase,long EmployeeId,string Employee,string Title,DateOnly Start,DateOnly End,int EstimateMinutes,string State,int Revision,long? ScopeChangeId=null,string ScopeState="Base scope",string ConflictStatus="NotReviewed",DateOnly? StoppedOn=null,long? ClosureId=null,DateOnly? ClosedOn=null,bool ClosureDateInferred=false)
{
 public decimal EstimatedHours=>EstimateMinutes/60m;
 public bool CanStart=>ProjectState=="Active"&&State=="Planned"&&ConflictStatus=="Clear"&&ScopeState is ("Base scope" or "Authorized");
 public bool CanComplete=>ProjectState=="Active"&&State=="InProgress"&&ScopeState is ("Base scope" or "Authorized");
 public string WorkEntryLabel=>$"{ProjectNumber} / {Phase} / "+( ClosedOn.HasValue?$"{Title} — closed {ClosedOn.Value:yyyy-MM-dd}{(ClosureDateInferred?" (inferred local date)":"")}; historical only{(StoppedOn.HasValue?$", stopped {StoppedOn.Value:yyyy-MM-dd}":"")}":StoppedOn.HasValue?$"{Title} — discontinued {StoppedOn.Value:yyyy-MM-dd} (historical only)":Title);
}
public sealed record TaskScopeOption(long? Id,string Label,string State);

 // Display hours and minutes while storing exact integer minutes.
 public static class WorkDuration
 {
  public static string Format(int minutes)=>FormattableString.Invariant($"{minutes/60}:{minutes%60:00}");
  public static int Parse(string text)
  {
   var parts=text.Trim().Split(':');
   if(parts.Length!=2||parts[0].Length is <1 or >2||parts[1].Length!=2||
      !int.TryParse(parts[0],System.Globalization.NumberStyles.None,System.Globalization.CultureInfo.InvariantCulture,out var h)||
      !int.TryParse(parts[1],System.Globalization.NumberStyles.None,System.Globalization.CultureInfo.InvariantCulture,out var m)||
      h>24||m>59||h*60+m is <1 or >1440)
    throw new ArgumentException("Enter hours and minutes, for example 1:30 for one hour and 30 minutes.");
   return h*60+m;
  }
 }
