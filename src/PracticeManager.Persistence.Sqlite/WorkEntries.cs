using Microsoft.Data.Sqlite;
using PracticeManager.Core;
namespace PracticeManager.Persistence;
public sealed partial class LocalPractice
{
 public AttendanceSession? ReadMyOpenAttendance()
 {
  using var c=Open();using var t=c.BeginTransaction();var employee=OwnEmployee(c,t);
  using var q=Command(c,t,"SELECT id,clock_in FROM employee_attendance WHERE employee_id=$e AND clock_out IS NULL",("$e",employee));using var r=q.ExecuteReader();return r.Read()?new(r.GetInt64(0),DateTimeOffset.FromUnixTimeSeconds(r.GetInt64(1)),null):null;
 }
 public string ClockIn()
 {
  using var c=Open();using var t=c.BeginTransaction(deferred:false);var employee=OwnEmployee(c,t);var actor=Require(c,t,"Work.Own");
  if(Convert.ToInt64(Scalar(c,t,"SELECT count(*) FROM employee_attendance WHERE employee_id=$e AND clock_out IS NULL",("$e",employee)))!=0)return "AlreadyClockedIn";
  var now=DateTimeOffset.UtcNow.ToUnixTimeSeconds();
  var last=Scalar(c,t,"SELECT max(clock_out) FROM employee_attendance WHERE employee_id=$e",("$e",employee));
  if(last is long previous&&now<previous)return "ClockReviewRequired";
  Exec(c,t,"INSERT INTO employee_attendance(employee_id,clock_in,actor_id) VALUES($e,$u,$a)",("$e",employee),("$u",now),("$a",actor));Audit(c,t,actor,"ClockIn","Attendance",Last(c,t));t.Commit();return "ClockedIn";
 }
 public string ClockOut(long expectedSession)
 {
  using var c=Open();using var t=c.BeginTransaction(deferred:false);var employee=OwnEmployee(c,t);var actor=Require(c,t,"Work.Own");
  var start=Scalar(c,t,"SELECT clock_in FROM employee_attendance WHERE id=$i AND employee_id=$e AND clock_out IS NULL",("$i",expectedSession),("$e",employee));
  if(start is not long begin)return "SessionUnavailable";
  var now=DateTimeOffset.UtcNow.ToUnixTimeSeconds();if(now<begin)return "ClockReviewRequired";
  if(Exec(c,t,"UPDATE employee_attendance SET clock_out=$u WHERE id=$i AND employee_id=$e AND clock_out IS NULL",("$u",now),("$i",expectedSession),("$e",employee))!=1)return "SessionUnavailable";
  Audit(c,t,actor,"ClockOut","Attendance",expectedSession);t.Commit();return "ClockedOut";
 }
 public IReadOnlyList<AttendanceSession> ReadMyAttendance(DateOnly start,DateOnly end)
 {
  using var c=Open();using var t=c.BeginTransaction();var employee=OwnEmployee(c,t);DateRange(start,end,366);
  // Explicit UTC date range; include sessions which overlap it.
  var a=new DateTimeOffset(start.ToDateTime(TimeOnly.MinValue),TimeSpan.Zero).ToUnixTimeSeconds();var b=new DateTimeOffset(end.ToDateTime(TimeOnly.MaxValue),TimeSpan.Zero).ToUnixTimeSeconds();
  using var q=Command(c,t,"SELECT id,clock_in,clock_out FROM employee_attendance WHERE employee_id=$e AND clock_in<=$b AND (clock_out IS NULL OR clock_out>=$a) ORDER BY clock_in DESC,id DESC LIMIT 501",("$e",employee),("$a",a),("$b",b));using var r=q.ExecuteReader();var rows=new List<AttendanceSession>();while(r.Read()){if(rows.Count==500)throw new ArgumentException("Narrow attendance date range.");rows.Add(new(r.GetInt64(0),DateTimeOffset.FromUnixTimeSeconds(r.GetInt64(1)),r.IsDBNull(2)?null:DateTimeOffset.FromUnixTimeSeconds(r.GetInt64(2))));}return rows.AsReadOnly();
 }
 public static IReadOnlyList<string> ExpenseCategories{get;}=Array.AsReadOnly(new[]{"Travel","Meals","Supplies","Permit","Other"});
 public long SubmitWork(WorkSubmissionInput input)
 {
  using var c=Open();using var t=c.BeginTransaction(deferred:false);var employee=OwnEmployee(c,t);var actor=Require(c,t,"Work.Own");
  if(input.OperationKey==Guid.Empty||input.Date==DateOnly.MinValue||input.Date>DateOnly.FromDateTime(DateTime.Today))throw new ArgumentException("Valid operation and work date required.");
  long? cents=null;
  if(input.Kind=="Time"){if(input.Minutes is not (>=1 and <=1440)||input.Amount!=null||input.Category!=null)throw new ArgumentException("Enter 1–1440 minutes for time only.");}
  else if(input.Kind=="Expense"){if(input.Minutes!=null||input.Amount is not decimal amount||amount<=0||amount>9999999999.99m||decimal.Round(amount,2)!=amount||input.Category==null||!ExpenseCategories.Contains(input.Category))throw new ArgumentException("Enter positive USD cents and an expense category.");cents=decimal.ToInt64(amount*100);}
  else throw new ArgumentException("Unknown submission kind.");
  var note=Validation.Text(input.Description,1000,"Work description / expense evidence",true);var date=PlanDate(input.Date);var key=input.OperationKey.ToString("D");
  // Compare the complete normalized intent before returning a prior operation.
  using(var q=Command(c,t,"SELECT id,employee_id,task_id,kind,work_date,minutes,amount_cents,category,description FROM work_submissions WHERE operation_key=$k",("$k",key)))
  using(var r=q.ExecuteReader()){
   if(r.Read()){
    if(r.GetInt64(1)!=employee||r.GetInt64(2)!=input.TaskId||r.GetString(3)!=input.Kind||r.GetString(4)!=date||(r.IsDBNull(5)?(int?)null:r.GetInt32(5))!=input.Minutes||(r.IsDBNull(6)?(long?)null:r.GetInt64(6))!=cents||(r.IsDBNull(7)?null:r.GetString(7))!=input.Category||r.GetString(8)!=note)throw new ArgumentException("Operation conflict.");
    return r.GetInt64(0);
   }
  }
  if(Scalar(c,t,"SELECT id FROM overhead_time WHERE operation_key=$k",("$k",key))!=null)throw new ArgumentException("Operation conflict.");
  if(Convert.ToInt64(Scalar(c,t,@"SELECT count(*) FROM project_tasks w JOIN project_phases h ON h.id=w.phase_id JOIN projects p ON p.id=h.project_id JOIN project_start_authorizations a ON a.project_id=p.id WHERE w.id=$i AND w.employee_id=$e AND w.state IN ('InProgress','Completed') AND NOT EXISTS(SELECT 1 FROM task_cancellations z WHERE z.task_id=w.id) AND p.state='Active' AND EXISTS(SELECT 1 FROM project_operational_state v WHERE v.project_id=p.id AND v.state='Active') AND $d>=w.start_on AND $d<=w.end_on",("$i",input.TaskId),("$e",employee),("$d",date)))!=1)throw new ArgumentException("Started own task, authorized project and date inside task plan required.");
  if(input.Kind=="Time"){
   var existing=Convert.ToInt64(Scalar(c,t,"SELECT coalesce((SELECT minutes FROM combined_daily_time WHERE employee_id=$e AND work_date=$d),0)",("$e",employee),("$d",date)));
   if(existing+input.Minutes!.Value>1440)throw new ArgumentException("Daily time exceeds 24 hours.");
  }
  Exec(c,t,"INSERT INTO work_submissions(operation_key,employee_id,task_id,kind,work_date,minutes,amount_cents,category,description,submitted_by,submitted_utc) VALUES($k,$e,$t,$kind,$d,$m,$c,$cat,$n,$a,$u)",("$k",key),("$e",employee),("$t",input.TaskId),("$kind",input.Kind),("$d",date),("$m",input.Minutes),("$c",cents),("$cat",input.Category),("$n",note),("$a",actor),("$u",UtcNow()));var id=Last(c,t);Audit(c,t,actor,"Submit"+input.Kind,"WorkSubmission",id);t.Commit();return id;
 }
 private const string SubmissionJoin=@" FROM work_submissions s JOIN project_tasks t ON t.id=s.task_id JOIN project_phases h ON h.id=t.phase_id JOIN projects p ON p.id=h.project_id JOIN employees e ON e.id=s.employee_id LEFT JOIN work_reviews r ON r.submission_id=s.id";
 private const string SubmissionColumns="s.id,p.project_no,t.title,s.kind,s.work_date,s.minutes,s.amount_cents,s.category,s.description";
 private static int? ReadMinutes(SqliteDataReader r,int i)=>r.IsDBNull(i)?null:r.GetInt32(i);
 private static decimal? ReadAmount(SqliteDataReader r,int i)=>r.IsDBNull(i)?null:r.GetInt64(i)/100m;
 private static string? ReadCategory(SqliteDataReader r,int i)=>r.IsDBNull(i)?null:r.GetString(i);
 public IReadOnlyList<OwnWorkSubmission> ReadMySubmissions(DateOnly start,DateOnly end)
 {
  using var c=Open();using var t=c.BeginTransaction();var employee=OwnEmployee(c,t);DateRange(start,end,366);
  using var q=Command(c,t,"SELECT "+SubmissionColumns+",coalesce(r.decision,'Submitted')"+SubmissionJoin+" WHERE s.employee_id=$e AND s.work_date BETWEEN $s AND $d ORDER BY s.work_date DESC,s.id DESC LIMIT 501",("$e",employee),("$s",PlanDate(start)),("$d",PlanDate(end)));using var r=q.ExecuteReader();var rows=new List<OwnWorkSubmission>();while(r.Read()){if(rows.Count==500)throw new ArgumentException("Narrow date range.");rows.Add(new(r.GetInt64(0),r.GetString(1),r.GetString(2),r.GetString(3),ReadPlanDate(r.GetString(4)),ReadMinutes(r,5),ReadAmount(r,6),ReadCategory(r,7),r.GetString(8),r.GetString(9)));}return rows.AsReadOnly();
 }
 private long RequireWorkReview(SqliteConnection c,SqliteTransaction t,string grant)
 {
  var actor=Require(c,t,grant);RequireProjectView(c,t);Require(c,t,"Employees.View");return actor;
 }
 public IReadOnlyList<WorkReviewRow> ReadWorkReview(DateOnly start,DateOnly end,bool pendingOnly=true)
 {
  using var c=Open();using var t=c.BeginTransaction();RequireWorkReview(c,t,"Work.Review");DateRange(start,end,366);
  using var q=Command(c,t,"SELECT "+SubmissionColumns+",coalesce(r.decision,'Submitted'),trim(e.first_name||' '||e.last_name)"+SubmissionJoin+" WHERE s.work_date BETWEEN $s AND $d AND ($pending=0 OR r.submission_id IS NULL) ORDER BY s.work_date,s.id LIMIT 501",("$s",PlanDate(start)),("$d",PlanDate(end)),("$pending",pendingOnly?1:0));using var r=q.ExecuteReader();var rows=new List<WorkReviewRow>();while(r.Read()){if(rows.Count==500)throw new ArgumentException("Narrow date range.");rows.Add(new(r.GetInt64(0),r.GetString(10),r.GetString(1),r.GetString(2),r.GetString(3),ReadPlanDate(r.GetString(4)),ReadMinutes(r,5),ReadAmount(r,6),ReadCategory(r,7),r.GetString(8),r.GetString(9)));}return rows.AsReadOnly();
 }
 public string ReviewWork(long id,bool approved,string note,bool confirmOwnerException=false)
 {
  using var c=Open();using var t=c.BeginTransaction(deferred:false);var actor=RequireWorkReview(c,t,"Work.Review");
  var submitter=Scalar(c,t,"SELECT submitted_by FROM work_submissions WHERE id=$i",("$i",id));
  if(submitter is not long owner)return "NotRecorded";
  long? exception=null;if(owner==actor){if(!approved)throw new UnauthorizedAccessException("Self rejection not authorized.");exception=RequireOwnerException(c,t,actor,confirmOwnerException);}
  if(Scalar(c,t,"SELECT decision FROM work_reviews WHERE submission_id=$i",("$i",id)) is string prior)return "Already"+prior;
  var n=Validation.Text(note,1000,"Review evidence",true);var state=approved?"Approved":"Rejected";
  Exec(c,t,"INSERT INTO work_reviews(submission_id,decision,note,reviewed_by,reviewed_utc,owner_exception_event_id) VALUES($i,$s,$n,$a,$u,$x)",("$i",id),("$s",state),("$n",n),("$a",actor),("$u",UtcNow()),("$x",exception));Audit(c,t,actor,exception.HasValue?"OwnerSelfReview"+state:"Review"+state,"WorkSubmission",id);t.Commit();return state;
 }
 public IReadOnlyList<WorkBillingRow> ReadBillingWork(DateOnly start,DateOnly end,bool unclassifiedOnly=true)
 {
  using var c=Open();using var t=c.BeginTransaction();RequireWorkReview(c,t,"Billing.Classify");DateRange(start,end,366);
  using var q=Command(c,t,"SELECT "+SubmissionColumns+",coalesce(b.classification,'Unclassified'),trim(e.first_name||' '||e.last_name)"+SubmissionJoin+" LEFT JOIN work_billing_classifications b ON b.submission_id=s.id WHERE r.decision='Approved' AND s.work_date BETWEEN $s AND $d AND ($pending=0 OR b.submission_id IS NULL) ORDER BY s.work_date,s.id LIMIT 501",("$s",PlanDate(start)),("$d",PlanDate(end)),("$pending",unclassifiedOnly?1:0));using var r=q.ExecuteReader();var rows=new List<WorkBillingRow>();while(r.Read()){if(rows.Count==500)throw new ArgumentException("Narrow date range.");rows.Add(new(r.GetInt64(0),r.GetString(10),r.GetString(1),r.GetString(2),r.GetString(3),ReadPlanDate(r.GetString(4)),ReadMinutes(r,5),ReadAmount(r,6),ReadCategory(r,7),r.GetString(8),r.GetString(9)));}return rows.AsReadOnly();
 }
 public string ClassifyWork(long id,bool billable,string note,bool confirmOwnerException=false)
 {
  using var c=Open();using var t=c.BeginTransaction(deferred:false);var actor=RequireWorkReview(c,t,"Billing.Classify");
  if(Scalar(c,t,"SELECT submitted_by FROM work_submissions WHERE id=$i",("$i",id)) is not long owner)return "NotRecorded";
  long? exception=owner==actor?RequireOwnerException(c,t,actor,confirmOwnerException):null;
  if(!Equals(Scalar(c,t,"SELECT decision FROM work_reviews WHERE submission_id=$i",("$i",id)),"Approved"))return "ApprovalRequired";
  if(Scalar(c,t,"SELECT classification FROM work_billing_classifications WHERE submission_id=$i",("$i",id)) is string prior)return "Already"+prior;
  var n=Validation.Text(note,1000,"Billing classification evidence",true);var state=billable?"Billable":"Nonbillable";
  Exec(c,t,"INSERT INTO work_billing_classifications(submission_id,classification,note,classified_by,classified_utc,owner_exception_event_id) VALUES($i,$s,$n,$a,$u,$x)",("$i",id),("$s",state),("$n",n),("$a",actor),("$u",UtcNow()),("$x",exception));Audit(c,t,actor,exception.HasValue?"OwnerSelfClassify"+state:"Classify"+state,"WorkSubmission",id);t.Commit();return state;
 }
}
