using PracticeManager.Core;
using Microsoft.Data.Sqlite;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
   w.estimate_minutes,coalesce(f.approved,0),coalesce(f.pending,0),coalesce(f.rejected,0),coalesce(f.invalidated,0),w.revision
  FROM project_tasks w JOIN project_phases h ON h.id=w.phase_id LEFT JOIN task_cancellations x ON x.task_id=w.id
  LEFT JOIN task_discontinuations d ON d.task_id=w.id LEFT JOIN facts f ON f.task_id=w.id
  WHERE h.project_id=$p ORDER BY h.start_on,h.id,w.start_on,w.id LIMIT 501",("$p",project));
  using var r=q.ExecuteReader();var rows=new List<TaskEffort>();
  while(r.Read())
  {
   if(rows.Count==500)throw new ArgumentException("Project effort register exceeds 500 tasks.");
   var row=new TaskEffort(r.GetInt64(0),r.GetString(1),r.GetString(2),r.GetString(3),r.GetInt32(4),r.GetInt64(5),r.GetInt64(6),r.GetInt64(7),r.GetInt64(8)){Revision=r.GetInt32(9)};
   if(row.State is not ("Planned" or "InProgress" or "Completed" or "Cancelled" or "Discontinued")||row.EstimateMinutes is <1 or >600000||
      row.ApprovedMinutes<0||row.PendingMinutes<0||row.RejectedMinutes<0||row.InvalidatedMinutes<0)throw new ArgumentException("Invalid task effort evidence.");
   rows.Add(row);
  }
  r.Close();
  return new ProjectEffortReview(p.Number,rows.AsReadOnly()){ProjectId=project,SourceEvidenceHash=EffortEvidenceHash(c,t,project)};
 }
 // Exact project-local plan and raw time evidence, in the same read transaction as totals.
 // No raw notes, credentials, expense values, rates or other-project records are returned.
 static string EffortEvidenceHash(SqliteConnection c,SqliteTransaction t,long project)
 {
  using var h=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
  h.AppendData("CRC-ProjectEffortEvidence-v1\n"u8);
  var total=0;
  long bytes=0;
  foreach(var sql in new[]{
   "SELECT h.* FROM project_phases h WHERE h.project_id=$p ORDER BY h.id",
   "SELECT w.* FROM project_tasks w JOIN project_phases h ON h.id=w.phase_id WHERE h.project_id=$p ORDER BY w.id",
   "SELECT z.* FROM task_cancellations z JOIN project_tasks w ON w.id=z.task_id JOIN project_phases h ON h.id=w.phase_id WHERE h.project_id=$p ORDER BY z.task_id",
   "SELECT z.* FROM task_discontinuations z JOIN project_tasks w ON w.id=z.task_id JOIN project_phases h ON h.id=w.phase_id WHERE h.project_id=$p ORDER BY z.task_id",
   "SELECT s.* FROM work_submissions s JOIN project_tasks w ON w.id=s.task_id JOIN project_phases h ON h.id=w.phase_id WHERE h.project_id=$p AND s.kind='Time' ORDER BY s.id",
   "SELECT r.* FROM work_reviews r JOIN work_submissions s ON s.id=r.submission_id JOIN project_tasks w ON w.id=s.task_id JOIN project_phases h ON h.id=w.phase_id WHERE h.project_id=$p AND s.kind='Time' ORDER BY r.submission_id",
   "SELECT z.* FROM work_invalidations z JOIN work_submissions s ON s.id=z.submission_id JOIN project_tasks w ON w.id=s.task_id JOIN project_phases h ON h.id=w.phase_id WHERE h.project_id=$p AND s.kind='Time' ORDER BY z.submission_id"})
  {
   h.AppendData(Encoding.UTF8.GetBytes(sql+"\n"));
   using var q=Command(c,t,sql,("$p",project));using var r=q.ExecuteReader();
   while(r.Read())
   {
    if(++total>100000)throw new ArgumentException("Project effort evidence exceeds the review limit.");
    var fields=new object[r.FieldCount];r.GetValues(fields);
    var payload=JsonSerializer.SerializeToUtf8Bytes(fields.Select(v=>new{Type=v.GetType().FullName,Value=v}).ToArray());
    bytes+=payload.Length;
    if(bytes>128L*1024*1024)throw new ArgumentException("Project effort evidence exceeds the review byte limit.");
    h.AppendData(payload);h.AppendData("\n"u8);
    CryptographicOperations.ZeroMemory(payload);
   }
  }
  return Convert.ToHexString(h.GetHashAndReset()).ToLowerInvariant();
 }

}
