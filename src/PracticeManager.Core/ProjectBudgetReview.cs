using System.Security.Cryptography;
using System.Text.Json;
namespace PracticeManager.Core;

// Native pure review model. Neither these records nor their digests establish
// authorization, durable history, independent provenance or a contractual amendment.
public sealed record BudgetRevision(long ProjectId,int Revision,string? PreviousHash,long Minutes,string Reason,
 string Kind,string EffortSnapshotHash,string Hash);
public sealed record BudgetForecastReview(long ProjectId,string BudgetHash,string EffortSnapshotHash,
 IReadOnlyList<TaskRemainingInput> Remaining,long CurrentBudgetMinutes,long FirstBudgetMinutes,
 long TotalForecastMinutes,long VarianceFromCurrentMinutes,long VarianceFromFirstMinutes,string Hash);
public static class ProjectBudgetReview
{
 const long MaxBudget=300_000_000;
 static string Digest(string domain,object value)
 {
  var data=JsonSerializer.SerializeToUtf8Bytes(new{Format=domain,Value=value});
  return Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
 }
 static bool IsHash(string? h)=>h is {Length:64}&&h.All(c=>c is >= '0' and <= '9' or >= 'a' and <= 'f');
 static void ValidBudget(long minutes,string reason)
 {
  if(minutes is <1 or >MaxBudget||string.IsNullOrWhiteSpace(reason)||reason.Length>1000||reason!=reason.Trim()||reason.Any(char.IsControl))
   throw new ArgumentException("Positive reviewed budget and explicit concise reason required.");
 }
 public static string SnapshotHash(long project,ProjectEffortReview effort)
 {
  if(project<=0||effort.ProjectId!=project||string.IsNullOrWhiteSpace(effort.ProjectNumber)||effort.ProjectNumber.Length>200||
     effort.Tasks.Count is <1 or >500||effort.Tasks.Select(t=>t.TaskId).Distinct().Count()!=effort.Tasks.Count)
   throw new ArgumentException("Exact nonempty project effort snapshot required.");
  foreach(var t in effort.Tasks)
   if(t.TaskId<=0||t.Revision<=0||t.EstimateMinutes is <1 or >600000||
      t.State is not ("Planned" or "InProgress" or "Completed" or "Cancelled" or "Discontinued")||
      t.ApprovedMinutes<0||t.PendingMinutes<0||t.RejectedMinutes<0||t.InvalidatedMinutes<0||
      t.Phase.Length>120||t.Title.Length>200)
    throw new ArgumentException("Invalid effort snapshot.");
  return Digest("CRC-ProjectEffortSnapshot-v1",new{ProjectId=project,effort.ProjectNumber,effort.SourceEvidenceHash,Tasks=effort.Tasks.OrderBy(t=>t.TaskId).Select(t=>new
  {t.TaskId,t.Phase,t.Title,t.State,t.Revision,t.EstimateMinutes,t.ApprovedMinutes,t.PendingMinutes,t.RejectedMinutes,t.InvalidatedMinutes}).ToArray()});
 }
 static string RevisionHash(BudgetRevision r)=>Digest("CRC-ProjectBudgetRevision-v1",new
 {r.ProjectId,r.Revision,r.PreviousHash,r.Minutes,r.Reason,r.Kind,r.EffortSnapshotHash});
 public static void ValidateHistory(long project,IReadOnlyList<BudgetRevision> history)
 {
  if(project<=0||history.Count>500)throw new ArgumentException("Invalid budget history.");
  BudgetRevision? prior=null;
  foreach(var r in history)
  {
   ValidBudget(r.Minutes,r.Reason);
   if(r.ProjectId!=project||r.Revision!=(prior?.Revision??0)+1||r.PreviousHash!=prior?.Hash||
      !IsHash(r.EffortSnapshotHash)||!IsHash(r.Hash)||r.Hash!=RevisionHash(r)||
      (prior==null?r.Kind!="FirstRecorded":r.Kind is not ("ScopeReview" or "EstimateReview" or "ManagementReview"))||
      (prior!=null&&r.Minutes==prior.Minutes))
    throw new ArgumentException("Budget history invalid; review original records.");
   prior=r;
  }
 }
 public static BudgetRevision PrepareRevision(long project,IReadOnlyList<BudgetRevision> history,string? expectedLatestHash,
  ProjectEffortReview effort,string reviewedSnapshotHash,long minutes,string kind,string reason,bool confirmed)
 {
  ValidateHistory(project,history);
  var prior=history.LastOrDefault();
  if(!confirmed||history.Count>=500||expectedLatestHash!=prior?.Hash||
     reviewedSnapshotHash!=SnapshotHash(project,effort))
   throw new ArgumentException("Explicit confirmation, current budget revision and exact effort snapshot required.");
  ValidBudget(minutes,reason);
  if(prior==null?kind!="FirstRecorded":kind is not ("ScopeReview" or "EstimateReview" or "ManagementReview"))
   throw new ArgumentException("Select appropriate revision reason category.");
  if(prior!=null&&minutes==prior.Minutes)throw new ArgumentException("No budget change.");
  var next=new BudgetRevision(project,(prior?.Revision??0)+1,prior?.Hash,minutes,reason,kind,reviewedSnapshotHash,"");
  return next with{Hash=RevisionHash(next)};
 }
 public static BudgetForecastReview PrepareForecast(long project,IReadOnlyList<BudgetRevision> history,string expectedLatestHash,
  ProjectEffortReview effort,string reviewedSnapshotHash,IReadOnlyList<TaskRemainingInput> remaining)
 {
  ValidateHistory(project,history);
  if(history.Count==0||history[^1].Hash!=expectedLatestHash||SnapshotHash(project,effort)!=reviewedSnapshotHash)
   throw new ArgumentException("Current reviewed budget and effort snapshot required.");
  var frozen=Array.AsReadOnly(remaining.OrderBy(x=>x.TaskId).ToArray());
  var forecast=ProjectEffort.Forecast(effort,frozen);
  if(!forecast.TotalProjectForecastMinutes.HasValue)throw new ArgumentException("Complete remaining effort assessment required.");
  var total=forecast.TotalProjectForecastMinutes.Value;var first=history[0].Minutes;var current=history[^1].Minutes;
  var hash=Digest("CRC-ProjectBudgetForecast-v1",new{ProjectId=project,BudgetHash=expectedLatestHash,EffortSnapshotHash=reviewedSnapshotHash,Remaining=frozen,First=first,Current=current,Total=total});
  return new(project,expectedLatestHash,reviewedSnapshotHash,frozen,current,first,total,total-current,total-first,hash);
 }
}
