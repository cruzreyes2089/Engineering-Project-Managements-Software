using Microsoft.Data.Sqlite;
using PracticeManager.Core;
namespace PracticeManager.Persistence;
public sealed partial class LocalPractice
{
 public bool CanReadFirmTimeMix()=>Can("Reports.Financial")&&Can("Billing.Classify")&&Can("Employees.View")&&Can("Projects.View")&&Can("Clients.View");
 public FirmTimeMix ReadFirmTimeMix(DateOnly start,DateOnly end)
 {
  using var c=Open();using var t=c.BeginTransaction();RequireWorkReview(c,t,"Billing.Classify");Require(c,t,"Reports.Financial");DateRange(start,end,366);
  using var q=Command(c,t,@"WITH facts AS (
   SELECT s.employee_id,'Project' source,CASE WHEN EXISTS(SELECT 1 FROM work_invalidations z WHERE z.submission_id=s.id) THEN 'Invalidated' ELSE coalesce(r.decision,'Submitted') END decision,coalesce(b.classification,'Unclassified') classification,s.minutes FROM work_submissions s LEFT JOIN work_reviews r ON r.submission_id=s.id LEFT JOIN work_billing_classifications b ON b.submission_id=s.id WHERE s.kind='Time' AND s.work_date BETWEEN $s AND $e
   UNION ALL SELECT s.employee_id,'Overhead',coalesce(r.decision,'Submitted'),'Nonbillable',s.minutes FROM overhead_time s LEFT JOIN overhead_reviews r ON r.submission_id=s.id WHERE s.work_date BETWEEN $s AND $e
  ) SELECT e.id,e.employee_no,trim(e.first_name||' '||e.last_name),e.active,
   sum(CASE WHEN f.source='Project' AND f.decision='Approved' AND f.classification='Billable' THEN f.minutes ELSE 0 END),
   sum(CASE WHEN f.source='Project' AND f.decision='Approved' AND f.classification='Nonbillable' THEN f.minutes ELSE 0 END),
   sum(CASE WHEN f.source='Project' AND f.decision='Approved' AND f.classification='Unclassified' THEN f.minutes ELSE 0 END),
   sum(CASE WHEN f.source='Overhead' AND f.decision='Approved' THEN f.minutes ELSE 0 END),
   sum(CASE WHEN f.source='Project' AND f.decision='Submitted' THEN f.minutes ELSE 0 END),
   sum(CASE WHEN f.source='Overhead' AND f.decision='Submitted' THEN f.minutes ELSE 0 END),
   sum(CASE WHEN f.source='Project' AND f.decision='Rejected' THEN f.minutes ELSE 0 END),
   sum(CASE WHEN f.source='Overhead' AND f.decision='Rejected' THEN f.minutes ELSE 0 END),
   sum(CASE WHEN f.source='Project' AND f.decision='Invalidated' THEN f.minutes ELSE 0 END),
   sum(f.minutes)
   FROM facts f JOIN employees e ON e.id=f.employee_id GROUP BY e.id,e.employee_no,e.first_name,e.last_name,e.active ORDER BY e.last_name,e.first_name,e.id LIMIT 501",("$s",PlanDate(start)),("$e",PlanDate(end)));
  using var r=q.ExecuteReader();var rows=new List<TimeMixRow>();
  while(r.Read()){
   if(rows.Count==500)throw new ArgumentException("Firm time report exceeds employee limit.");
   var row=new TimeMixRow(r.GetInt64(0),r.GetString(1),r.GetString(2),r.GetInt64(3)==1,r.GetInt64(4),r.GetInt64(5),r.GetInt64(6),r.GetInt64(7),r.GetInt64(8),r.GetInt64(9),r.GetInt64(10),r.GetInt64(11),r.GetInt64(12));
   if(row.ApprovedMinutes+row.PendingProjectMinutes+row.PendingOverheadMinutes+row.RejectedProjectMinutes+row.RejectedOverheadMinutes+row.InvalidatedProjectMinutes!=r.GetInt64(13))throw new InvalidOperationException("Time mix does not reconcile.");
   rows.Add(row);
  }
  return new(start,end,rows.AsReadOnly());
 }
}
