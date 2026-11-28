using System.Globalization;
namespace PracticeManager.Core;

public sealed record TaskEffort(long TaskId,string Phase,string Title,string State,int EstimateMinutes,
 long ApprovedMinutes,long PendingMinutes,long RejectedMinutes,long InvalidatedMinutes)
{
 public int Revision{get;init;}
 public bool Open=>State is "Planned" or "InProgress";
 public bool Historical=>State is "Cancelled" or "Discontinued";
 public long? ApprovedVarianceMinutes=>Historical?null:ApprovedMinutes-EstimateMinutes;
 public decimal EstimateHours=>EstimateMinutes/60m;
 public decimal ApprovedHours=>ApprovedMinutes/60m;
 public decimal PendingHours=>PendingMinutes/60m;
 public decimal RejectedHours=>RejectedMinutes/60m;
 public decimal InvalidatedHours=>InvalidatedMinutes/60m;
}
public sealed record ProjectEffortReview(string ProjectNumber,IReadOnlyList<TaskEffort> Tasks)
{
 public long ProjectId{get;init;}
 // Digest of the underlying project plan and time evidence, not a signature.
 public string? SourceEvidenceHash{get;init;}
 public long CurrentPlanMinutes=>Tasks.Where(t=>!t.Historical).Sum(t=>(long)t.EstimateMinutes);
 public long CurrentApprovedMinutes=>Tasks.Where(t=>!t.Historical).Sum(t=>t.ApprovedMinutes);
 public long CurrentPendingMinutes=>Tasks.Where(t=>!t.Historical).Sum(t=>t.PendingMinutes);
 public long HistoricalApprovedMinutes=>Tasks.Where(t=>t.Historical).Sum(t=>t.ApprovedMinutes);
 public long HistoricalPendingMinutes=>Tasks.Where(t=>t.Historical).Sum(t=>t.PendingMinutes);
 public long RejectedMinutes=>Tasks.Sum(t=>t.RejectedMinutes);
 public long InvalidatedMinutes=>Tasks.Sum(t=>t.InvalidatedMinutes);
 public int OpenTasks=>Tasks.Count(t=>t.Open);
}
public sealed record TaskRemainingInput(long TaskId,int Minutes);
public sealed record ProjectEffortForecast(int MissingOpenTasks,long EnteredRemainingMinutes,long? CurrentForecastMinutes,
 long? CurrentForecastVarianceMinutes,long? TotalProjectForecastMinutes);
public static class ProjectEffort
{
 // Entire current snapshot, not a reconstructed baseline. Explicit ETC excludes all submitted time.
 // Forecast assumes pending submissions are accepted, not that they are already approved.
 public static ProjectEffortForecast Forecast(ProjectEffortReview review,IReadOnlyList<TaskRemainingInput> inputs)
 {
  if(review.Tasks.Count>500||inputs.Count>500||review.Tasks.Select(t=>t.TaskId).Distinct().Count()!=review.Tasks.Count)
   throw new ArgumentException("Invalid task forecast register.");
  var open=review.Tasks.Where(t=>t.Open).Select(t=>t.TaskId).ToHashSet();
  var seen=new HashSet<long>();long remaining=0;
  foreach(var input in inputs)
  {
   if(!open.Contains(input.TaskId)||!seen.Add(input.TaskId)||input.Minutes is <0 or >600000)
    throw new ArgumentException("Enter one remaining estimate (0–600000 minutes) per current open task; no closed or unknown tasks.");
   remaining+=input.Minutes;
  }
  int missing=open.Count-seen.Count;
  long? current=missing==0?review.CurrentApprovedMinutes+review.CurrentPendingMinutes+remaining:null;
  return new(missing,remaining,current,current-review.CurrentPlanMinutes,current+review.HistoricalApprovedMinutes+review.HistoricalPendingMinutes);
 }
 public static IReadOnlyList<TaskRemainingInput> ParseRemaining(string text)
 {
  if(text.Length>40000)throw new ArgumentException("Forecast input exceeds limit.");
  var lines=text.Replace("\r\n","\n").Split('\n',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries);
  if(lines.Length>500)throw new ArgumentException("Forecast input exceeds row limit.");
  var rows=new List<TaskRemainingInput>();
  foreach(var line in lines)
  {
   var p=line.Split(',',StringSplitOptions.TrimEntries);
   if(p.Length!=2||!long.TryParse(p[0],NumberStyles.None,CultureInfo.InvariantCulture,out var id)||id<=0||
      !int.TryParse(p[1],NumberStyles.None,CultureInfo.InvariantCulture,out var minutes)||minutes is <0 or >600000)
    throw new ArgumentException("Enter task ID,remaining minutes on each line.");
   rows.Add(new(id,minutes));
  }
  return rows.AsReadOnly();
 }
}
