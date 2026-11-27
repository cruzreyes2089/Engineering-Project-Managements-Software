using PracticeManager.Core;
namespace PracticeManager.Persistence;
public sealed partial class LocalPractice
{
 public ProjectEffortReview ReadProjectEffort(long project)
 {
  using var c=Open();using var t=c.BeginTransaction();RequireReport(c,t,"Reports.Operations");var p=ProjectRow(c,t,project);
  using var q=Command(c,t,@"WITH facts AS (
   SELECT s.task_id,
    sum(CASE WHEN z.submission_id IS NULL AND r.decision='Approved' THEN s.minutes ELSE 0 END) approved,
    sum(CASE WHEN z.submission_id IS NULL AND r.submission_id IS NULL THEN s.minutes ELSE 0 END) pending,
    sum(CASE WHEN z.submission_id IS NULL AND r.decision='Rejected' THEN s.minutes ELSE 0 END) rejected,
    sum(CASE WHEN z.submission_id IS NOT NULL THEN s.minutes ELSE 0 END) invalidated
   FROM work_submissions s JOIN project_tasks w ON w.id=s.task_id JOIN project_phases h ON h.id=w.phase_id
   LEFT JOIN work_reviews r ON r.submission_id=s.id LEFT JOIN work_invalidations z ON z.submission_id=s.id
   WHERE h.project_id=$p AND s.kind='Time' GROUP BY s.task_id
  )
  SELECT w.id,h.name,w.title,CASE WHEN x.task_id IS NOT NULL THEN 'Cancelled' WHEN d.task_id IS NOT NULL THEN 'Discontinued' ELSE w.state END,
   w.estimate_minutes,coalesce(f.approved,0),coalesce(f.pending,0),coalesce(f.rejected,0),coalesce(f.invalidated,0)
  FROM project_tasks w JOIN project_phases h ON h.id=w.phase_id LEFT JOIN task_cancellations x ON x.task_id=w.id
  LEFT JOIN task_discontinuations d ON d.task_id=w.id LEFT JOIN facts f ON f.task_id=w.id
  WHERE h.project_id=$p ORDER BY h.start_on,h.id,w.start_on,w.id LIMIT 501",("$p",project));
  using var r=q.ExecuteReader();var rows=new List<TaskEffort>();
  while(r.Read())
  {
   if(rows.Count==500)throw new ArgumentException("Project effort register exceeds 500 tasks.");
   var row=new TaskEffort(r.GetInt64(0),r.GetString(1),r.GetString(2),r.GetString(3),r.GetInt32(4),r.GetInt64(5),r.GetInt64(6),r.GetInt64(7),r.GetInt64(8));
   if(row.State is not ("Planned" or "InProgress" or "Completed" or "Cancelled" or "Discontinued")||row.EstimateMinutes is <1 or >600000||
      row.ApprovedMinutes<0||row.PendingMinutes<0||row.RejectedMinutes<0||row.InvalidatedMinutes<0)throw new ArgumentException("Invalid task effort evidence.");
   rows.Add(row);
  }
  return new(p.Number,rows.AsReadOnly());
 }
}
