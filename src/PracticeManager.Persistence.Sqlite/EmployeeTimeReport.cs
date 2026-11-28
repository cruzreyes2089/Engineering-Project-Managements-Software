using PracticeManager.Core;
namespace PracticeManager.Persistence;
public sealed partial class LocalPractice
{
 public bool CanReadEmployeeTimeReport()=>Can("Reports.Operations")&&Can("Work.Review")&&Can("Employees.View")&&Can("Projects.View")&&Can("Clients.View");
 public EmployeeTimeReport ReadEmployeeTimeReport(EmployeeTimeFilter filter)
 {
  using var c=Open();using var t=c.BeginTransaction(deferred:true);
  RequireWorkReview(c,t,"Work.Review");Require(c,t,"Reports.Operations");
  DateRange(filter.Start,filter.End,366);
  if(filter.Status is not ("All" or "Approved" or "Submitted" or "Rejected" or "Invalidated"))
   throw new ArgumentException("Select a valid time review status.");
  if(filter.EmployeeId.HasValue&&Scalar(c,t,"SELECT id FROM employees WHERE id=$i",("$i",filter.EmployeeId))==null)
   throw new ArgumentException("Employee unavailable.");
  if(filter.ProjectId.HasValue)ProjectRow(c,t,filter.ProjectId.Value);
  // Project and overhead IDs occupy distinct namespaces. No compensation,
  // billing classification, expense amounts, attendance or free-text notes.
  using var q=Command(c,t,@"WITH time_rows AS (
   SELECT 'Project' AS source,s.id AS submission_id,s.employee_id,e.employee_no,trim(e.first_name||' '||e.last_name) AS employee,
   p.id AS project_id,p.project_no,t.title AS task_or_category,s.work_date,s.minutes,
   CASE WHEN EXISTS(SELECT 1 FROM work_invalidations z WHERE z.submission_id=s.id) THEN 'Invalidated' ELSE coalesce(r.decision,'Submitted') END AS status
   FROM work_submissions s JOIN project_tasks t ON t.id=s.task_id JOIN project_phases h ON h.id=t.phase_id
   JOIN projects p ON p.id=h.project_id JOIN employees e ON e.id=s.employee_id LEFT JOIN work_reviews r ON r.submission_id=s.id
   WHERE s.kind='Time' AND s.work_date BETWEEN $start AND $end AND ($employee IS NULL OR s.employee_id=$employee) AND ($project IS NULL OR p.id=$project)
   UNION ALL
   SELECT 'Overhead',s.id,s.employee_id,e.employee_no,trim(e.first_name||' '||e.last_name),NULL,'',s.category,s.work_date,s.minutes,
   CASE WHEN EXISTS(SELECT 1 FROM overhead_invalidations z WHERE z.submission_id=s.id) THEN 'Invalidated' ELSE coalesce(r.decision,'Submitted') END
   FROM overhead_time s JOIN employees e ON e.id=s.employee_id LEFT JOIN overhead_reviews r ON r.submission_id=s.id
   WHERE $project IS NULL AND s.work_date BETWEEN $start AND $end AND ($employee IS NULL OR s.employee_id=$employee)
  ) SELECT * FROM time_rows WHERE $status='All' OR status=$status
  ORDER BY employee,employee_id,work_date,source,submission_id LIMIT 5001",
  ("$start",PlanDate(filter.Start)),("$end",PlanDate(filter.End)),("$employee",filter.EmployeeId),("$project",filter.ProjectId),("$status",filter.Status));
  using var r=q.ExecuteReader();var rows=new List<EmployeeTimeDetail>();long approved=0,pending=0,rejected=0,invalidated=0;
  while(r.Read())
  {
   if(rows.Count==5000)throw new ArgumentException("Narrow employee time report filters; results exceed 5000 records.");
   var m=r.GetInt32(9);if(m is <1 or >1440)throw new InvalidOperationException("Invalid time evidence.");
   var state=r.GetString(10);
   switch(state){case "Approved":approved+=m;break;case "Submitted":pending+=m;break;case "Rejected":rejected+=m;break;case "Invalidated":invalidated+=m;break;default:throw new InvalidOperationException("Unknown time review status.");}
   rows.Add(new(r.GetString(0),r.GetInt64(1),r.GetInt64(2),r.GetString(3),r.GetString(4),r.IsDBNull(5)?null:r.GetInt64(5),r.GetString(6),r.GetString(7),ReadPlanDate(r.GetString(8)),m,state));
  }
  return new(filter,rows.AsReadOnly(),approved,pending,rejected,invalidated);
 }
}
