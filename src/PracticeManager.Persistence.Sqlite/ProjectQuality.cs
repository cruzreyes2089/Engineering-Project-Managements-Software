using Microsoft.Data.Sqlite;
using PracticeManager.Core;
namespace PracticeManager.Persistence;
public sealed partial class LocalPractice
{
 private long RequireQuality(SqliteConnection c,SqliteTransaction t,string permission)
 {
  RequireProjectView(c,t);Require(c,t,"QA.View");return Require(c,t,permission);
 }
 public IReadOnlyList<QualityItem> ReadQualityItems(long project)
 {
  using var c=Open();using var t=c.BeginTransaction();RequireQuality(c,t,"QA.View");ProjectRow(c,t,project);
  using var q=Command(c,t,@"SELECT i.id,i.project_id,i.title,i.severity,i.description,CASE WHEN x.item_id IS NULL THEN 'Open' ELSE 'Closed' END,i.recorded_utc,x.resolution FROM project_quality_items i LEFT JOIN project_quality_closures x ON x.item_id=i.id WHERE i.project_id=$p ORDER BY i.id DESC LIMIT 501",("$p",project));using var r=q.ExecuteReader();var rows=new List<QualityItem>();while(r.Read()){if(rows.Count==500)throw new ArgumentException("Quality register exceeds development limit.");rows.Add(new(r.GetInt64(0),r.GetInt64(1),r.GetString(2),r.GetString(3),r.GetString(4),r.GetString(5),r.GetString(6),r.IsDBNull(7)?null:r.GetString(7)));}return rows.AsReadOnly();
 }
 public long CreateQualityItem(long project,string title,string severity,string description)
 {
  using var c=Open();using var t=c.BeginTransaction(deferred:false);var actor=RequireQuality(c,t,"QA.Edit");ProjectRow(c,t,project);
  if(severity is not ("Observation" or "Correction" or "Critical"))throw new ArgumentException("Invalid quality severity.");
  Exec(c,t,"INSERT INTO project_quality_items(project_id,title,severity,description,recorded_by,recorded_utc) VALUES($p,$t,$s,$d,$a,$u)",("$p",project),("$t",Validation.Text(title,200,"Title",true)),("$s",severity),("$d",Validation.Text(description,4000,"Description",true)),("$a",actor),("$u",UtcNow()));var id=Last(c,t);Audit(c,t,actor,"Create","QualityItem",id);t.Commit();return id;
 }
 private static void RequireQualityItem(SqliteConnection c,SqliteTransaction t,long project,long item)
 {
  if(Convert.ToInt64(Scalar(c,t,"SELECT count(*) FROM project_quality_items WHERE id=$i AND project_id=$p",("$i",item),("$p",project)))!=1)throw new ArgumentException("Quality item unavailable in selected project.");
 }
 public IReadOnlyList<QualityNote> ReadQualityNotes(long project,long item)
 {
  using var c=Open();using var t=c.BeginTransaction();RequireQuality(c,t,"QA.View");RequireQualityItem(c,t,project,item);
  using var q=Command(c,t,"SELECT note,recorded_utc FROM project_quality_notes WHERE item_id=$i ORDER BY id",("$i",item));using var r=q.ExecuteReader();var rows=new List<QualityNote>();while(r.Read())rows.Add(new(r.GetString(0),r.GetString(1)));return rows.AsReadOnly();
 }
 public void AddQualityNote(long project,long item,string note)
 {
  using var c=Open();using var t=c.BeginTransaction(deferred:false);var actor=RequireQuality(c,t,"QA.Edit");RequireQualityItem(c,t,project,item);
  if(Convert.ToInt64(Scalar(c,t,"SELECT count(*) FROM project_quality_closures WHERE item_id=$i",("$i",item)))!=0)throw new ArgumentException("Closed item is read-only.");
  Exec(c,t,"INSERT INTO project_quality_notes(item_id,note,recorded_by,recorded_utc) VALUES($i,$n,$a,$u)",("$i",item),("$n",Validation.Text(note,4000,"Note",true)),("$a",actor),("$u",UtcNow()));Audit(c,t,actor,"AddNote","QualityItem",item);t.Commit();
 }
 public string CloseQualityItem(long project,long item,string resolution)
 {
  using var c=Open();using var t=c.BeginTransaction(deferred:false);var actor=RequireQuality(c,t,"QA.Close");RequireQualityItem(c,t,project,item);
  if(Convert.ToInt64(Scalar(c,t,"SELECT count(*) FROM project_quality_closures WHERE item_id=$i",("$i",item)))!=0)return "AlreadyClosed";
  Exec(c,t,"INSERT INTO project_quality_closures(item_id,resolution,closed_by,closed_utc) VALUES($i,$n,$a,$u)",("$i",item),("$n",Validation.Text(resolution,4000,"Resolution evidence",true)),("$a",actor),("$u",UtcNow()));Audit(c,t,actor,"Close","QualityItem",item);t.Commit();return "Closed";
 }
 public IReadOnlyList<ScopeChange> ReadScopeChanges(long project)
 {
  using var c=Open();using var t=c.BeginTransaction();RequireQuality(c,t,"QA.View");ProjectRow(c,t,project);
  using var q=Command(c,t,"SELECT s.id,s.project_id,s.title,s.scope,coalesce(d.decision,'Proposed'),s.recorded_utc,d.evidence FROM project_scope_changes s LEFT JOIN project_scope_decisions d ON d.change_id=s.id WHERE s.project_id=$p ORDER BY s.id DESC LIMIT 501",("$p",project));using var r=q.ExecuteReader();var rows=new List<ScopeChange>();while(r.Read()){if(rows.Count==500)throw new ArgumentException("Scope register exceeds development limit.");rows.Add(new(r.GetInt64(0),r.GetInt64(1),r.GetString(2),r.GetString(3),r.GetString(4),r.GetString(5),r.IsDBNull(6)?null:r.GetString(6)));}return rows.AsReadOnly();
 }
 public long ProposeScopeChange(long project,string title,string scope)
 {
  using var c=Open();using var t=c.BeginTransaction(deferred:false);var actor=RequireQuality(c,t,"QA.Edit");ProjectRow(c,t,project);
  Exec(c,t,"INSERT INTO project_scope_changes(project_id,title,scope,recorded_by,recorded_utc) VALUES($p,$t,$s,$a,$u)",("$p",project),("$t",Validation.Text(title,200,"Title",true)),("$s",Validation.Text(scope,4000,"Scope",true)),("$a",actor),("$u",UtcNow()));var id=Last(c,t);Audit(c,t,actor,"Propose","ScopeChange",id);t.Commit();return id;
 }
 public string DecideScopeChange(long project,long change,bool authorized,string evidence,bool explicitClientReview)
 {
  using var c=Open();using var t=c.BeginTransaction(deferred:false);var actor=RequireQuality(c,t,"Scope.Authorize");var p=ProjectRow(c,t,project);
  if(Convert.ToInt64(Scalar(c,t,"SELECT count(*) FROM project_scope_changes WHERE id=$i AND project_id=$p",("$i",change),("$p",project)))!=1)throw new ArgumentException("Scope change unavailable in selected project.");
  if(Scalar(c,t,"SELECT decision FROM project_scope_decisions WHERE change_id=$i",("$i",change)) is string previous)return "Already"+previous;
  if(authorized){RequireOpenOperations(c,t,project);if(!explicitClientReview||p.State!="Active"||!ConflictCleared(c,t,project))throw new ArgumentException("Active project, current conflict review and explicit client authorization review required.");ActiveClient(c,t,p.Client);}
  var state=authorized?"Authorized":"Rejected";Exec(c,t,"INSERT INTO project_scope_decisions(change_id,decision,evidence,decided_by,decided_utc) VALUES($i,$s,$n,$a,$u)",("$i",change),("$s",state),("$n",Validation.Text(evidence,4000,"Decision evidence",true)),("$a",actor),("$u",UtcNow()));Audit(c,t,actor,"Scope"+state,"ScopeChange",change);t.Commit();return state;
 }
}
