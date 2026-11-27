using Microsoft.Data.Sqlite;
using PracticeManager.Core;
namespace PracticeManager.Persistence;
public sealed record OverheadReplacement(long OriginalId,long ReplacementId,string RecordedUtc);
public sealed partial class LocalPractice
{
 public OverheadReplacement? ReadMyOverheadReplacement(long original)
 {
  using var c=Open();using var t=c.BeginTransaction();var employee=OwnEmployee(c,t);
  if(Convert.ToInt64(Scalar(c,t,"SELECT count(*) FROM overhead_time WHERE id=$i AND employee_id=$e",("$i",original),("$e",employee)))!=1)throw new ArgumentException("Own overhead submission unavailable.");
  using var q=Command(c,t,"SELECT id,submitted_utc FROM overhead_time WHERE replaces_submission_id=$i AND employee_id=$e",("$i",original),("$e",employee));using var r=q.ExecuteReader();return r.Read()?new(original,r.GetInt64(0),r.GetString(1)):null;
 }
 public long ReplaceRejectedOverheadTime(long original,OverheadTimeInput input)
 {if(original<=0)throw new ArgumentException("Select original rejected overhead record.");return SubmitOverheadCore(input,original);}
 public static IReadOnlyList<string> OverheadCategories{get;}=Array.AsReadOnly(new[]{"Administration","Marketing","BusinessDevelopment","Proposals","Management","Training","Other"});
 public long SubmitOverheadTime(OverheadTimeInput input)=>SubmitOverheadCore(input,null);
 private long SubmitOverheadCore(OverheadTimeInput input,long? original)
 {
  using var c=Open();using var t=c.BeginTransaction(deferred:false);var employee=OwnEmployee(c,t);var actor=Require(c,t,"Work.Own");
  if(input.OperationKey==Guid.Empty||input.Date.Year<1900||input.Date>DateOnly.FromDateTime(DateTime.Today)||input.Minutes is <1 or >1440||!OverheadCategories.Contains(input.Category))throw new ArgumentException("Valid overhead date, category, operation and 1–1440 minutes required.");
  var date=PlanDate(input.Date);var key=input.OperationKey.ToString("D");var note=Validation.Text(input.Description,1000,"Overhead description",true);
  using(var q=Command(c,t,"SELECT id,employee_id,work_date,minutes,category,description,replaces_submission_id FROM overhead_time WHERE operation_key=$k",("$k",key)))
  using(var r=q.ExecuteReader()){
   if(r.Read()){
    if(r.GetInt64(1)!=employee||r.GetString(2)!=date||r.GetInt32(3)!=input.Minutes||r.GetString(4)!=input.Category||r.GetString(5)!=note||ReadOptionalId(r,6)!=original)throw new ArgumentException("Operation conflict.");
    return r.GetInt64(0);
   }
  }
  if(Scalar(c,t,"SELECT id FROM work_submissions WHERE operation_key=$k",("$k",key))!=null)throw new ArgumentException("Operation conflict.");
  if(original.HasValue&&Convert.ToInt64(Scalar(c,t,@"SELECT count(*) FROM overhead_time s JOIN overhead_reviews r ON r.submission_id=s.id WHERE s.id=$o AND s.employee_id=$e AND r.decision='Rejected' AND NOT EXISTS(SELECT 1 FROM overhead_time n WHERE n.replaces_submission_id=s.id)",("$o",original),("$e",employee)))!=1)throw new ArgumentException("Own rejected overhead without replacement required.");
  var existing=Convert.ToInt64(Scalar(c,t,"SELECT coalesce((SELECT minutes FROM combined_daily_time WHERE employee_id=$e AND work_date=$d),0)",("$e",employee),("$d",date)));
  if(existing+input.Minutes>1440)throw new ArgumentException("Combined project and overhead time exceeds 24 hours.");
  Exec(c,t,"INSERT INTO overhead_time(operation_key,employee_id,work_date,minutes,category,description,submitted_by,submitted_utc,replaces_submission_id) VALUES($k,$e,$d,$m,$c,$n,$a,$u,$o)",("$k",key),("$e",employee),("$d",date),("$m",input.Minutes),("$c",input.Category),("$n",note),("$a",actor),("$u",UtcNow()),("$o",original));
  var id=Last(c,t);Audit(c,t,actor,original.HasValue?"ReplaceRejectedOverheadTime":"SubmitOverheadTime","OverheadTime",id);t.Commit();return id;
 }
 public IReadOnlyList<OwnOverheadTime> ReadMyOverheadTime(DateOnly start,DateOnly end)
 {
  using var c=Open();using var t=c.BeginTransaction();var employee=OwnEmployee(c,t);DateRange(start,end,366);
  using var q=Command(c,t,"SELECT s.id,s.work_date,s.minutes,s.category,s.description,coalesce(r.decision,'Submitted'),s.replaces_submission_id,(SELECT n.id FROM overhead_time n WHERE n.replaces_submission_id=s.id) FROM overhead_time s LEFT JOIN overhead_reviews r ON r.submission_id=s.id WHERE s.employee_id=$e AND s.work_date BETWEEN $s AND $d ORDER BY s.work_date DESC,s.id DESC LIMIT 501",("$e",employee),("$s",PlanDate(start)),("$d",PlanDate(end)));
  using var r=q.ExecuteReader();var rows=new List<OwnOverheadTime>();while(r.Read()){if(rows.Count==500)throw new ArgumentException("Narrow overhead date range.");rows.Add(new(r.GetInt64(0),ReadPlanDate(r.GetString(1)),r.GetInt32(2),r.GetString(3),r.GetString(4),r.GetString(5),ReadOptionalId(r,6),ReadOptionalId(r,7)));}return rows.AsReadOnly();
 }
 public IReadOnlyList<OverheadReviewRow> ReadOverheadReview(DateOnly start,DateOnly end,bool pendingOnly=true)
 {
  using var c=Open();using var t=c.BeginTransaction();RequireWorkReview(c,t,"Work.Review");DateRange(start,end,366);
  using var q=Command(c,t,"SELECT s.id,trim(e.first_name||' '||e.last_name),s.work_date,s.minutes,s.category,s.description,coalesce(r.decision,'Submitted'),s.replaces_submission_id,(SELECT n.id FROM overhead_time n WHERE n.replaces_submission_id=s.id) FROM overhead_time s JOIN employees e ON e.id=s.employee_id LEFT JOIN overhead_reviews r ON r.submission_id=s.id WHERE s.work_date BETWEEN $s AND $d AND ($p=0 OR r.submission_id IS NULL) ORDER BY s.work_date,s.id LIMIT 501",("$s",PlanDate(start)),("$d",PlanDate(end)),("$p",pendingOnly?1:0));
  using var r=q.ExecuteReader();var rows=new List<OverheadReviewRow>();while(r.Read()){if(rows.Count==500)throw new ArgumentException("Narrow overhead review date range.");rows.Add(new(r.GetInt64(0),r.GetString(1),ReadPlanDate(r.GetString(2)),r.GetInt32(3),r.GetString(4),r.GetString(5),r.GetString(6),ReadOptionalId(r,7),ReadOptionalId(r,8)));}return rows.AsReadOnly();
 }
 public string ReviewOverheadTime(long id,bool approved,string note,bool confirmOwnerException=false)
 {
  using var c=Open();using var t=c.BeginTransaction(deferred:false);var actor=RequireWorkReview(c,t,"Work.Review");
  if(Scalar(c,t,"SELECT submitted_by FROM overhead_time WHERE id=$i",("$i",id)) is not long submitter)return "NotRecorded";
  long? exception=null;if(actor==submitter){if(!approved)throw new UnauthorizedAccessException("Self rejection prohibited.");exception=RequireOwnerException(c,t,actor,confirmOwnerException);}
  if(Scalar(c,t,"SELECT decision FROM overhead_reviews WHERE submission_id=$i",("$i",id)) is string prior)return "Already"+prior;
  var n=Validation.Text(note,1000,"Overhead review evidence",true);var state=approved?"Approved":"Rejected";
  Exec(c,t,"INSERT INTO overhead_reviews(submission_id,decision,note,reviewed_by,reviewed_utc,owner_exception_event_id) VALUES($i,$s,$n,$a,$u,$x)",("$i",id),("$s",state),("$n",n),("$a",actor),("$u",UtcNow()),("$x",exception));
  Audit(c,t,actor,exception.HasValue?"OwnerSelfReview"+state:"Review"+state,"OverheadTime",id);t.Commit();return state;
 }
}
