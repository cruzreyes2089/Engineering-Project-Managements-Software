using System.Globalization;
using Microsoft.Data.Sqlite;
using PracticeManager.Core;
namespace PracticeManager.Persistence;
public sealed record TaskCancellation(long TaskId,int PlanRevision,string Reason,long ActorId,string RecordedUtc);
public sealed record TaskDiscontinuation(long TaskId,int TaskRevision,DateOnly StoppedOn,string Reason,long ActorId,string RecordedUtc);
public sealed partial class LocalPractice
{
 public TaskDiscontinuation? ReadTaskDiscontinuation(long project,long task)=>throw new NotImplementedException("Started-task discontinuation history not implemented.");
 public TaskDiscontinuation DiscontinueStartedTask(long project,long task,int expectedRevision,string reason,bool confirmed)=>throw new NotImplementedException("Started-task discontinuation not implemented.");
 public TaskCancellation? ReadTaskCancellation(long project,long task)
 {
  using var c=Open();using var t=c.BeginTransaction();RequirePlan(c,t);ProjectRow(c,t,project);
  if(Convert.ToInt64(Scalar(c,t,"SELECT count(*) FROM project_tasks w JOIN project_phases h ON h.id=w.phase_id WHERE w.id=$i AND h.project_id=$p",("$i",task),("$p",project)))!=1)throw new ArgumentException("Task unavailable in selected project.");
  using var q=Command(c,t,"SELECT task_id,plan_revision,reason,actor_id,recorded_utc FROM task_cancellations WHERE task_id=$i",("$i",task));using var r=q.ExecuteReader();
  return r.Read()?new(r.GetInt64(0),r.GetInt32(1),r.GetString(2),r.GetInt64(3),r.GetString(4)):null;
 }
 public TaskCancellation CancelPlannedTask(long project,long task,int expectedRevision,string reason,bool confirmed)
 {
  using var c=Open();using var t=c.BeginTransaction(deferred:false);var actor=RequirePlan(c,t);ProjectRow(c,t,project);RequireOpenOperations(c,t,project);
  if(!confirmed)throw new ArgumentException("Explicit task cancellation confirmation required.");
  var note=Validation.Text(reason,1000,"Cancellation reason",true);
  if(Convert.ToInt64(Scalar(c,t,@"SELECT count(*) FROM project_tasks w JOIN project_phases h ON h.id=w.phase_id WHERE w.id=$i AND h.project_id=$p AND w.revision=$r AND w.state='Planned' AND NOT EXISTS(SELECT 1 FROM task_cancellations z WHERE z.task_id=w.id) AND NOT EXISTS(SELECT 1 FROM work_submissions s WHERE s.task_id=w.id)",("$i",task),("$p",project),("$r",expectedRevision)))!=1)throw new ArgumentException("Current unstarted task without work or prior cancellation required.");
  var utc=UtcNow();Exec(c,t,"INSERT INTO task_cancellations(task_id,plan_revision,reason,actor_id,recorded_utc) VALUES($i,$r,$n,$a,$u)",("$i",task),("$r",expectedRevision),("$n",note),("$a",actor),("$u",utc));
  Audit(c,t,actor,"CancelPlannedTask","ProjectTask",task);t.Commit();return new(task,expectedRevision,note,actor,utc);
 }
 private static string PlanDate(DateOnly d)=>d.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
 private static DateOnly ReadPlanDate(string d)=>DateOnly.ParseExact(d,"yyyy-MM-dd",CultureInfo.InvariantCulture);
 private static void DateRange(DateOnly start,DateOnly end,int maxDays)
 {
  if(start==DateOnly.MinValue||end<start||end.DayNumber-start.DayNumber>=maxDays)throw new ArgumentException($"Enter a valid inclusive date range of at most {maxDays} days.");
 }
 private long RequirePlan(SqliteConnection c,SqliteTransaction t)
 {
  var actor=Require(c,t,"Projects.Plan");RequireProjectView(c,t);Require(c,t,"Employees.View");return actor;
 }
 public IReadOnlyList<ProjectPhase> ReadProjectPhases(long project)
 {
  using var c=Open();using var t=c.BeginTransaction();RequireProjectView(c,t);ProjectRow(c,t,project);
  using var q=Command(c,t,"SELECT id,project_id,name,start_on,end_on,revision FROM project_phases WHERE project_id=$p ORDER BY start_on,id",("$p",project));using var r=q.ExecuteReader();var rows=new List<ProjectPhase>();while(r.Read())rows.Add(new(r.GetInt64(0),r.GetInt64(1),r.GetString(2),ReadPlanDate(r.GetString(3)),ReadPlanDate(r.GetString(4)),r.GetInt32(5)));return rows.AsReadOnly();
 }
 public long CreateProjectPhase(long project,string name,DateOnly start,DateOnly end)
 {
  using var c=Open();using var t=c.BeginTransaction(deferred:false);var actor=RequirePlan(c,t);ProjectRow(c,t,project);RequireOpenOperations(c,t,project);DateRange(start,end,3660);var n=Validation.Text(name,120,"Phase",true);
  if(Convert.ToInt64(Scalar(c,t,"SELECT count(*) FROM project_phases WHERE project_id=$p AND name=$n COLLATE NOCASE",("$p",project),("$n",n)))!=0)throw new ArgumentException("Phase already exists.");
  Exec(c,t,"INSERT INTO project_phases(project_id,name,start_on,end_on,revision) VALUES($p,$n,$s,$e,1)",("$p",project),("$n",n),("$s",PlanDate(start)),("$e",PlanDate(end)));var id=Last(c,t);Audit(c,t,actor,"Create","ProjectPhase",id);t.Commit();return id;
 }
 public void EditProjectPhase(long project,long phase,int revision,string name,DateOnly start,DateOnly end)
 {
  using var c=Open();using var t=c.BeginTransaction(deferred:false);var actor=RequirePlan(c,t);ProjectRow(c,t,project);RequireOpenOperations(c,t,project);DateRange(start,end,3660);var n=Validation.Text(name,120,"Phase",true);
  if(Convert.ToInt64(Scalar(c,t,"SELECT count(*) FROM project_phases WHERE project_id=$p AND id!=$i AND name=$n COLLATE NOCASE",("$p",project),("$i",phase),("$n",n)))!=0)throw new ArgumentException("Phase already exists.");
  if(Convert.ToInt64(Scalar(c,t,"SELECT count(*) FROM project_tasks WHERE phase_id=$i AND (start_on<$s OR end_on>$e)",("$i",phase),("$s",PlanDate(start)),("$e",PlanDate(end))))!=0)throw new ArgumentException("Existing tasks must remain inside phase dates.");
  if(Exec(c,t,"UPDATE project_phases SET name=$n,start_on=$s,end_on=$e,revision=revision+1 WHERE id=$i AND project_id=$p AND revision=$r",("$n",n),("$s",PlanDate(start)),("$e",PlanDate(end)),("$i",phase),("$p",project),("$r",revision))!=1)throw new ArgumentException("Phase changed. Refresh.");
  Audit(c,t,actor,"Update","ProjectPhase",phase);t.Commit();
 }
 private static string ValidateTask(SqliteConnection c,SqliteTransaction t,long project,ProjectTaskPlan plan)
 {
  DateRange(plan.Start,plan.End,3660);if(plan.EstimateMinutes is <1 or >600000)throw new ArgumentException("Enter a positive estimate in minutes.");
  if(Convert.ToInt64(Scalar(c,t,"SELECT count(*) FROM project_phases WHERE id=$h AND project_id=$p AND start_on<=$s AND end_on>=$e",("$h",plan.PhaseId),("$p",project),("$s",PlanDate(plan.Start)),("$e",PlanDate(plan.End))))!=1)throw new ArgumentException("Task must be within the selected project phase.");
  if(Convert.ToInt64(Scalar(c,t,"SELECT count(*) FROM employees WHERE id=$i AND active=1",("$i",plan.EmployeeId)))!=1)throw new ArgumentException("Select an active employee.");
  if(plan.ScopeChangeId.HasValue&&Convert.ToInt64(Scalar(c,t,"SELECT count(*) FROM project_scope_changes s LEFT JOIN project_scope_decisions d ON d.change_id=s.id WHERE s.id=$i AND s.project_id=$p AND coalesce(d.decision,'Proposed')!='Rejected'",("$i",plan.ScopeChangeId),("$p",project)))!=1)throw new ArgumentException("Additional scope unavailable or rejected.");
  return Validation.Text(plan.Title,200,"Task title",true);
 }
 public long CreateProjectTask(long project,ProjectTaskPlan plan)
 {
  using var c=Open();using var t=c.BeginTransaction(deferred:false);var actor=RequirePlan(c,t);ProjectRow(c,t,project);RequireOpenOperations(c,t,project);var n=ValidateTask(c,t,project,plan);
  Exec(c,t,"INSERT INTO project_tasks(phase_id,employee_id,title,start_on,end_on,estimate_minutes,state,revision,scope_change_id) VALUES($h,$i,$n,$s,$e,$m,'Planned',1,$change)",("$h",plan.PhaseId),("$i",plan.EmployeeId),("$n",n),("$s",PlanDate(plan.Start)),("$e",PlanDate(plan.End)),("$m",plan.EstimateMinutes),("$change",plan.ScopeChangeId));var id=Last(c,t);Audit(c,t,actor,"Create","ProjectTask",id);t.Commit();return id;
 }
 public void EditProjectTask(long project,long task,int revision,ProjectTaskPlan plan)
 {
  using var c=Open();using var t=c.BeginTransaction(deferred:false);var actor=RequirePlan(c,t);ProjectRow(c,t,project);RequireOpenOperations(c,t,project);var n=ValidateTask(c,t,project,plan);
  if(Exec(c,t,"UPDATE project_tasks SET employee_id=$i,title=$n,start_on=$s,end_on=$e,estimate_minutes=$m,revision=revision+1 WHERE id=$t AND phase_id=$h AND revision=$r AND state='Planned' AND scope_change_id IS $change AND NOT EXISTS(SELECT 1 FROM task_cancellations z WHERE z.task_id=project_tasks.id)",("$i",plan.EmployeeId),("$n",n),("$s",PlanDate(plan.Start)),("$e",PlanDate(plan.End)),("$m",plan.EstimateMinutes),("$t",task),("$h",plan.PhaseId),("$r",revision),("$change",plan.ScopeChangeId))!=1)throw new ArgumentException("Task changed, moved, or already started.");
  Audit(c,t,actor,"Update","ProjectTask",task);t.Commit();
 }
 private const string TaskProjection=@"SELECT t.id,p.id,p.project_no,(SELECT v.state FROM project_operational_state v WHERE v.project_id=p.id),h.id,h.name,e.id,trim(e.first_name||' '||e.last_name),t.title,t.start_on,t.end_on,t.estimate_minutes,CASE WHEN EXISTS(SELECT 1 FROM task_cancellations z WHERE z.task_id=t.id) THEN 'Cancelled' ELSE t.state END,t.revision,t.scope_change_id,CASE WHEN t.scope_change_id IS NULL THEN 'Base scope' ELSE coalesce(d.decision,'Proposed') END,CASE WHEN EXISTS(SELECT 1 FROM project_conflict_cleared cr WHERE cr.project_id=p.id) THEN 'Clear' WHEN EXISTS(SELECT 1 FROM project_conflict_current cr WHERE cr.project_id=p.id) THEN 'Hold' ELSE 'NotReviewed' END FROM project_tasks t JOIN project_phases h ON h.id=t.phase_id JOIN projects p ON p.id=h.project_id JOIN employees e ON e.id=t.employee_id LEFT JOIN project_scope_decisions d ON d.change_id=t.scope_change_id";
 private static IReadOnlyList<ProjectTask> TaskRows(SqliteCommand q)
 {
  using var r=q.ExecuteReader();var rows=new List<ProjectTask>();while(r.Read()){if(rows.Count==500)throw new ArgumentException("More than 500 tasks. Narrow the date range or project selection.");rows.Add(new(r.GetInt64(0),r.GetInt64(1),r.GetString(2),r.GetString(3),r.GetInt64(4),r.GetString(5),r.GetInt64(6),r.GetString(7),r.GetString(8),ReadPlanDate(r.GetString(9)),ReadPlanDate(r.GetString(10)),r.GetInt32(11),r.GetString(12),r.GetInt32(13),r.IsDBNull(14)?null:r.GetInt64(14),r.GetString(15),r.GetString(16)));}return rows.AsReadOnly();
 }
 public IReadOnlyList<TaskScopeOption> ReadTaskScopeOptions(long project)
 {
  using var c=Open();using var t=c.BeginTransaction();RequirePlan(c,t);ProjectRow(c,t,project);
  using var q=Command(c,t,"SELECT s.id,s.title,coalesce(d.decision,'Proposed') FROM project_scope_changes s LEFT JOIN project_scope_decisions d ON d.change_id=s.id WHERE s.project_id=$p ORDER BY s.id",("$p",project));using var r=q.ExecuteReader();var rows=new List<TaskScopeOption>{new(null,"Base scope","Base scope")};while(r.Read())rows.Add(new(r.GetInt64(0),r.GetString(1)+" ("+r.GetString(2)+")",r.GetString(2)));return rows.AsReadOnly();
 }
 public IReadOnlyList<ProjectTask> ReadProjectTasks(long project)
 {
  using var c=Open();using var t=c.BeginTransaction();RequirePlan(c,t);ProjectRow(c,t,project);
  using var q=Command(c,t,TaskProjection+" WHERE p.id=$p ORDER BY t.start_on,t.id LIMIT 501",("$p",project));return TaskRows(q);
 }
 public IReadOnlyList<ProjectTask> ReadStaffSchedule(DateOnly start,DateOnly end,long? employee=null)
 {
  using var c=Open();using var t=c.BeginTransaction();RequirePlan(c,t);DateRange(start,end,366);
  using var q=Command(c,t,TaskProjection+" WHERE t.start_on<=$e AND t.end_on>=$s AND ($i IS NULL OR e.id=$i) ORDER BY t.start_on,t.id LIMIT 501",("$s",PlanDate(start)),("$e",PlanDate(end)),("$i",employee));return TaskRows(q);
 }
 private long OwnEmployee(SqliteConnection c,SqliteTransaction t)
 {
  var actor=Require(c,t,"Work.Own");
  var id=Scalar(c,t,"SELECT id FROM employees WHERE user_id=$i AND active=1",("$i",actor));
  return id is long employee?employee:throw new UnauthorizedAccessException("Active linked employee required.");
 }
 public IReadOnlyList<ProjectTask> ReadMyWork(DateOnly start,DateOnly end)
 {
  using var c=Open();using var t=c.BeginTransaction();var employee=OwnEmployee(c,t);DateRange(start,end,366);
  using var q=Command(c,t,TaskProjection+" WHERE t.employee_id=$i AND t.start_on<=$e AND t.end_on>=$s ORDER BY t.start_on,t.id LIMIT 501",("$i",employee),("$s",PlanDate(start)),("$e",PlanDate(end)));return TaskRows(q);
 }
 public void AdvanceMyTask(long task,int revision,string next)
 {
  using var c=Open();using var t=c.BeginTransaction(deferred:false);var employee=OwnEmployee(c,t);var actor=Require(c,t,"Work.Own");
  if(next=="InProgress"&&Convert.ToInt64(Scalar(c,t,"SELECT count(*) FROM project_tasks w JOIN project_phases h ON h.id=w.phase_id JOIN project_conflict_cleared r ON r.project_id=h.project_id WHERE w.id=$i AND w.employee_id=$e",("$i",task),("$e",employee)))!=1)throw new ArgumentException("Current conflict review required before starting work.");
  var previous=next switch{"InProgress"=>"Planned","Completed"=>"InProgress",_=>throw new ArgumentException("Unsupported task transition.")};
  if(Exec(c,t,@"UPDATE project_tasks SET state=$n,revision=revision+1 WHERE id=$i AND employee_id=$e AND revision=$r AND state=$s AND NOT EXISTS(SELECT 1 FROM task_cancellations z WHERE z.task_id=project_tasks.id) AND EXISTS(SELECT 1 FROM project_phases h JOIN projects p ON p.id=h.project_id JOIN project_start_authorizations a ON a.project_id=p.id WHERE h.id=project_tasks.phase_id AND p.state='Active' AND EXISTS(SELECT 1 FROM project_operational_state v WHERE v.project_id=p.id AND v.state='Active')) AND (scope_change_id IS NULL OR EXISTS(SELECT 1 FROM project_scope_decisions d WHERE d.change_id=project_tasks.scope_change_id AND d.decision='Authorized'))",("$n",next),("$i",task),("$e",employee),("$r",revision),("$s",previous))!=1)throw new ArgumentException("Task unavailable, changed, or project not authorized.");
  Audit(c,t,actor,"Task"+next,"ProjectTask",task);t.Commit();
 }
}
