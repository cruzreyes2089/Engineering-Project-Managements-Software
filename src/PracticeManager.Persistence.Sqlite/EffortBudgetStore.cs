using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PracticeManager.Core;

namespace PracticeManager.Persistence;

public sealed record EffortBudgetEntry(BudgetRevision Model,string Operation,long Actor,string Utc,long AuditId);
public sealed record EffortForecastEntry(BudgetForecastReview Model,int Sequence,string? Previous,string Hash,string Reason,string Operation,long Actor,string Utc,long AuditId);
public sealed record EffortBudgetHistory(IReadOnlyList<EffortBudgetEntry> Budgets,IReadOnlyList<EffortForecastEntry> Forecasts);
public sealed record EffortBudgetWorkspace(ProjectEffortReview Effort,string? ReviewedSnapshotHash,EffortBudgetHistory History,bool CanRecord);

// Shared transactional persistence candidate. The owning service supplies an
// authorization callback and current evidence from this same database transaction.
internal sealed record EffortBudgetVerification(int Projects,int Revisions,int Forecasts,string Digest);
internal sealed class EffortBudgetStore(SqliteConnection db,Func<SqliteTransaction,long,bool,long> authorize,
 Func<SqliteTransaction,long,ProjectEffortReview> current)
{
 static readonly JsonSerializerOptions Json=new(){IgnoreReadOnlyProperties=true};
 static byte[] Encode<T>(T value)=>JsonSerializer.SerializeToUtf8Bytes(value,Json);
 // Do not use the snapshot serializer here: IgnoreReadOnlyProperties would
 // omit the get-only properties of the anonymous envelope, hashing "{}".
 static string Hash(object value)=>Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value))).ToLowerInvariant();
 static string Key(Guid operation)=>operation==Guid.Empty?throw new ArgumentException("Operation identity required."):operation.ToString("N");
 static bool IsKey(string value)=>Guid.TryParseExact(value,"N",out var id)&&id!=Guid.Empty&&id.ToString("N")==value;
 static string Utc()=>DateTimeOffset.UtcNow.ToString("O",CultureInfo.InvariantCulture);
 static void CheckMeta(string operation,long actor,string utc,long audit)
 {
  if(!IsKey(operation)||actor<=0||audit<=0||!DateTimeOffset.TryParseExact(utc,"O",CultureInfo.InvariantCulture,DateTimeStyles.None,out var time)||time.Offset!=TimeSpan.Zero)
   throw new InvalidOperationException("Invalid budget history metadata.");
 }
 static string BudgetMetadataHash(BudgetRevision model,string operation,long actor,string utc,long audit)
  =>Hash(new{Format="CRC-EffortBudgetMetadata-v1",model.Hash,Operation=operation,Actor=actor,Utc=utc,AuditId=audit});
 static void Reason(string reason)
 {
  if(string.IsNullOrWhiteSpace(reason)||reason.Length>1000||reason!=reason.Trim()||reason.Any(char.IsControl))
   throw new ArgumentException("Explicit concise review reason required.");
 }
 SqliteCommand Q(SqliteTransaction t,string sql,params (string Name,object? Value)[] args)
 {
  var q=db.CreateCommand();q.Transaction=t;q.CommandText=sql;
  foreach(var (name,value) in args)q.Parameters.AddWithValue(name,value??DBNull.Value);
  return q;
 }
 long Count(SqliteTransaction t,string sql,params (string,object?)[] args)
 {using var q=Q(t,sql,args);return Convert.ToInt64(q.ExecuteScalar());}
 void Exec(SqliteTransaction t,string sql,params (string,object?)[] args)
 {using var q=Q(t,sql,args);q.ExecuteNonQuery();}
 internal static byte[] SnapshotBytes(ProjectEffortReview r)=>Encode(r with{Tasks=Array.AsReadOnly(r.Tasks.OrderBy(x=>x.TaskId).ToArray())});
 internal static ProjectEffortReview DecodeSnapshot(long project,byte[] bytes,string expected)
 {
  if(bytes.Length is <1 or >2097152)throw new InvalidOperationException("Invalid snapshot byte length.");
  var value=JsonSerializer.Deserialize<ProjectEffortReview>(bytes,Json)??throw new InvalidOperationException("Missing snapshot.");
  if(ProjectBudgetReview.SnapshotHash(project,value)!=expected||!SnapshotBytes(value).SequenceEqual(bytes))
   throw new InvalidOperationException("Snapshot bytes or digest mismatch.");
  return value;
 }
 static string? Optional(SqliteDataReader r,int i)=>r.IsDBNull(i)?null:r.GetString(i);
 internal static string ForecastHash(BudgetForecastReview model,int sequence,string? prior,string reason,string operation,long actor,string utc,long audit)
  =>Hash(new{Format="CRC-EffortForecastRecord-v1",model.Hash,Sequence=sequence,Previous=prior,Reason=reason,Operation=operation,Actor=actor,Utc=utc,AuditId=audit});
 long Audit(SqliteTransaction t,long project,long actor,string action)
 {
  using var q=Q(t,"INSERT INTO audit_events(actor_id,action,entity,entity_id) VALUES($a,$x,'Project',$p) RETURNING id",("$a",actor),("$x",action),("$p",project));
  return Convert.ToInt64(q.ExecuteScalar());
 }
 void AuditCount(SqliteTransaction t,long project,string action,int expected)
 {
  if(Count(t,"SELECT count(*) FROM audit_events WHERE action=$a AND entity='Project' AND entity_id=$p",("$a",action),("$p",project))!=expected)
   throw new InvalidOperationException("Unpaired budget history audit.");
 }
 IReadOnlyList<EffortBudgetEntry> Budgets(SqliteTransaction t,long project)
 {
  var result=new List<EffortBudgetEntry>();
  using(var q=Q(t,@"SELECT b.revision,b.operation_key,b.previous_hash,b.record_hash,b.minutes,b.kind,b.reason,b.snapshot_hash,b.snapshot_bytes,
   b.actor_id,b.recorded_utc,b.audit_id,a.id,b.metadata_hash FROM effort_budget_revisions b LEFT JOIN audit_events a ON a.id=b.audit_id
   AND a.actor_id=b.actor_id AND a.action='RecordEffortBudgetRevision' AND a.entity='Project' AND a.entity_id=b.project_id
   WHERE b.project_id=$p ORDER BY b.revision",("$p",project)))
  using(var r=q.ExecuteReader())while(r.Read())
  {
   if(result.Count==500||r.IsDBNull(12))throw new InvalidOperationException("Budget history incomplete.");
   var operation=r.GetString(1);var actor=r.GetInt64(9);var utc=r.GetString(10);var audit=r.GetInt64(11);
   CheckMeta(operation,actor,utc,audit);
   var snapshot=DecodeSnapshot(project,(byte[])r.GetValue(8),r.GetString(7));
   var model=ProjectBudgetReview.PrepareRevision(project,result.Select(x=>x.Model).ToArray(),Optional(r,2),snapshot,r.GetString(7),r.GetInt64(4),r.GetString(5),r.GetString(6),true);
   if(model.Revision!=r.GetInt32(0)||model.Hash!=r.GetString(3)||BudgetMetadataHash(model,operation,actor,utc,audit)!=r.GetString(13))throw new InvalidOperationException("Budget chain digest mismatch.");
   result.Add(new(model,operation,actor,utc,audit));
  }
  AuditCount(t,project,"RecordEffortBudgetRevision",result.Count);
  return result.AsReadOnly();
 }
 IReadOnlyList<EffortForecastEntry> Forecasts(SqliteTransaction t,long project,IReadOnlyList<EffortBudgetEntry> budgets)
 {
  var result=new List<EffortForecastEntry>();var models=budgets.Select(x=>x.Model).ToArray();
  using(var q=Q(t,@"SELECT f.sequence,f.operation_key,f.previous_hash,f.record_hash,f.budget_hash,f.snapshot_hash,f.snapshot_bytes,f.remaining_bytes,
   f.first_budget_minutes,f.current_budget_minutes,f.approved_minutes,f.pending_minutes,f.remaining_minutes,f.total_minutes,f.reason,f.actor_id,f.recorded_utc,f.audit_id,a.id
   FROM effort_forecast_records f LEFT JOIN audit_events a ON a.id=f.audit_id AND a.actor_id=f.actor_id
   AND a.action='RecordEffortForecast' AND a.entity='Project' AND a.entity_id=f.project_id
   WHERE f.project_id=$p ORDER BY f.sequence",("$p",project)))
  using(var r=q.ExecuteReader())while(r.Read())
  {
   if(result.Count==500||r.IsDBNull(18))throw new InvalidOperationException("Forecast history incomplete.");
   int sequence=r.GetInt32(0);var operation=r.GetString(1);var prior=Optional(r,2);
   var snapshot=DecodeSnapshot(project,(byte[])r.GetValue(6),r.GetString(5));
   var bytes=(byte[])r.GetValue(7);
   var remaining=JsonSerializer.Deserialize<TaskRemainingInput[]>(bytes,Json)??throw new InvalidOperationException("Missing forecast input.");
   if(!Encode(remaining.OrderBy(x=>x.TaskId).ToArray()).SequenceEqual(bytes))throw new InvalidOperationException("Noncanonical forecast input.");
   var budgetIndex=Array.FindIndex(models,x=>x.Hash==r.GetString(4));
   if(budgetIndex<0)throw new InvalidOperationException("Forecast budget missing.");
   var model=ProjectBudgetReview.PrepareForecast(project,models.Take(budgetIndex+1).ToArray(),r.GetString(4),snapshot,r.GetString(5),remaining);
   var reason=r.GetString(14);var actor=r.GetInt64(15);var utc=r.GetString(16);var audit=r.GetInt64(17);
   CheckMeta(operation,actor,utc,audit);Reason(reason);
   var hash=ForecastHash(model,sequence,prior,reason,operation,actor,utc,audit);
   if(sequence!=result.Count+1||prior!=result.LastOrDefault()?.Hash||hash!=r.GetString(3)||
      model.FirstBudgetMinutes!=r.GetInt64(8)||model.CurrentBudgetMinutes!=r.GetInt64(9)||
      snapshot.CurrentApprovedMinutes+snapshot.HistoricalApprovedMinutes!=r.GetInt64(10)||
      snapshot.CurrentPendingMinutes+snapshot.HistoricalPendingMinutes!=r.GetInt64(11)||
      remaining.Sum(x=>(long)x.Minutes)!=r.GetInt64(12)||model.TotalForecastMinutes!=r.GetInt64(13))
    throw new InvalidOperationException("Forecast chain or arithmetic mismatch.");
   result.Add(new(model,sequence,prior,hash,reason,operation,actor,utc,audit));
  }
  AuditCount(t,project,"RecordEffortForecast",result.Count);
  return result.AsReadOnly();
 }
 void OtherOperation(SqliteTransaction t,string key,long project,bool budget)
 {
  if(Count(t,"SELECT (SELECT count(*) FROM effort_budget_revisions WHERE operation_key=$k AND (project_id!=$p OR $b=0))+(SELECT count(*) FROM effort_forecast_records WHERE operation_key=$k AND (project_id!=$p OR $b=1))",("$k",key),("$p",project),("$b",budget?1:0))!=0)
   throw new ArgumentException("Operation already belongs to another record.");
 }
 ProjectEffortReview Current(SqliteTransaction t,long project,string reviewedHash)
 {
  var snapshot=current(t,project);
  if(ProjectBudgetReview.SnapshotHash(project,snapshot)!=reviewedHash)throw new InvalidOperationException("Current project effort evidence changed.");
  return DecodeSnapshot(project,SnapshotBytes(snapshot),reviewedHash);
 }
 public (EffortBudgetEntry Entry,bool Replayed) AppendBudget(long project,Guid operation,string? expected,string snapshotHash,long minutes,string kind,string reason,bool confirmed)
 {
  if(!confirmed)throw new ArgumentException("Explicit confirmation required.");
  var key=Key(operation);Reason(reason);
  using var t=db.BeginTransaction(deferred:false);
  var actor=authorize(t,project,true);if(actor<=0)throw new UnauthorizedAccessException();
  OtherOperation(t,key,project,true);
  var prior=Budgets(t,project);_=Forecasts(t,project,prior);
  if(prior.SingleOrDefault(x=>x.Operation==key) is EffortBudgetEntry old)
  {
   if(old.Actor!=actor||old.Model.PreviousHash!=expected||old.Model.EffortSnapshotHash!=snapshotHash||old.Model.Minutes!=minutes||old.Model.Kind!=kind||old.Model.Reason!=reason)
    throw new ArgumentException("Changed budget replay.");
   t.Commit();return(old,true);
  }
  var snapshot=Current(t,project,snapshotHash);
  var model=ProjectBudgetReview.PrepareRevision(project,prior.Select(x=>x.Model).ToArray(),expected,snapshot,snapshotHash,minutes,kind,reason,true);
  var utc=Utc();var audit=Audit(t,project,actor,"RecordEffortBudgetRevision");
  Exec(t,@"INSERT INTO effort_budget_revisions(project_id,revision,operation_key,previous_hash,record_hash,minutes,kind,reason,snapshot_hash,snapshot_bytes,actor_id,recorded_utc,audit_id,metadata_hash) VALUES($p,$r,$o,$prev,$h,$m,$k,$n,$s,$bytes,$a,$u,$audit,$meta)",
   ("$p",project),("$r",model.Revision),("$o",key),("$prev",expected),("$h",model.Hash),("$m",minutes),("$k",kind),("$n",reason),("$s",snapshotHash),("$bytes",SnapshotBytes(snapshot)),("$a",actor),("$u",utc),("$audit",audit),("$meta",BudgetMetadataHash(model,key,actor,utc,audit)));
  var read=Budgets(t,project)[^1];
  if(read.Model!=model||read.Operation!=key||read.Actor!=actor||read.Utc!=utc||read.AuditId!=audit)throw new InvalidOperationException("Budget read-back failed.");
  t.Commit();return(read,false);
 }
 public (EffortForecastEntry Entry,bool Replayed) AppendForecast(long project,Guid operation,string? expectedForecast,string expectedBudget,string snapshotHash,IReadOnlyList<TaskRemainingInput> remaining,string reason,bool confirmed)
 {
  if(!confirmed)throw new ArgumentException("Explicit confirmation required.");
  var key=Key(operation);Reason(reason);
  var ordered=remaining.OrderBy(x=>x.TaskId).ToArray();
  using var t=db.BeginTransaction(deferred:false);
  var actor=authorize(t,project,true);if(actor<=0)throw new UnauthorizedAccessException();
  OtherOperation(t,key,project,false);
  var budgets=Budgets(t,project);var history=Forecasts(t,project,budgets);
  if(history.SingleOrDefault(x=>x.Operation==key) is EffortForecastEntry old)
  {
   if(old.Actor!=actor||old.Previous!=expectedForecast||old.Model.BudgetHash!=expectedBudget||old.Model.EffortSnapshotHash!=snapshotHash||old.Reason!=reason||!old.Model.Remaining.SequenceEqual(ordered))
    throw new ArgumentException("Changed forecast replay.");
   t.Commit();return(old,true);
  }
  if(history.Count>=500||history.LastOrDefault()?.Hash!=expectedForecast)throw new ArgumentException("Stale forecast predecessor.");
  var snapshot=Current(t,project,snapshotHash);
  var model=ProjectBudgetReview.PrepareForecast(project,budgets.Select(x=>x.Model).ToArray(),expectedBudget,snapshot,snapshotHash,ordered);
  int sequence=history.Count+1;var utc=Utc();var audit=Audit(t,project,actor,"RecordEffortForecast");
  var hash=ForecastHash(model,sequence,expectedForecast,reason,key,actor,utc,audit);
  Exec(t,@"INSERT INTO effort_forecast_records(project_id,sequence,operation_key,previous_hash,record_hash,budget_hash,snapshot_hash,snapshot_bytes,remaining_bytes,first_budget_minutes,current_budget_minutes,approved_minutes,pending_minutes,remaining_minutes,total_minutes,reason,actor_id,recorded_utc,audit_id) VALUES($p,$seq,$o,$prev,$h,$b,$s,$bytes,$remaining,$first,$current,$approved,$pending,$m,$total,$reason,$a,$u,$audit)",
   ("$p",project),("$seq",sequence),("$o",key),("$prev",expectedForecast),("$h",hash),("$b",expectedBudget),("$s",snapshotHash),("$bytes",SnapshotBytes(snapshot)),("$remaining",Encode(ordered)),
   ("$first",model.FirstBudgetMinutes),("$current",model.CurrentBudgetMinutes),("$approved",snapshot.CurrentApprovedMinutes+snapshot.HistoricalApprovedMinutes),("$pending",snapshot.CurrentPendingMinutes+snapshot.HistoricalPendingMinutes),
   ("$m",ordered.Sum(x=>(long)x.Minutes)),("$total",model.TotalForecastMinutes),("$reason",reason),("$a",actor),("$u",utc),("$audit",audit));
  var read=Forecasts(t,project,budgets)[^1];
  if(read.Hash!=hash||read.Model.Hash!=model.Hash||read.Operation!=key||read.Actor!=actor||read.Sequence!=sequence||read.Previous!=expectedForecast||read.Reason!=reason||read.Utc!=utc||read.AuditId!=audit)throw new InvalidOperationException("Forecast read-back failed.");
  t.Commit();return(read,false);
 }
 internal EffortBudgetHistory ReadCore(SqliteTransaction t,long project)
 {
  var b=Budgets(t,project);var f=Forecasts(t,project,b);
  return new(b,f);
 }
 public EffortBudgetHistory Read(long project)
 {
  using var t=db.BeginTransaction(deferred:true);
  if(authorize(t,project,false)<=0)throw new UnauthorizedAccessException();
  return ReadCore(t,project);
 }
 public EffortBudgetWorkspace Review(long project)
 {
  using var t=db.BeginTransaction(deferred:true);
  var actor=authorize(t,project,false);if(actor<=0)throw new UnauthorizedAccessException();
  var history=ReadCore(t,project);var effort=current(t,project);
  var digest=effort.Tasks.Count==0?null:ProjectBudgetReview.SnapshotHash(project,effort);
  bool canRecord=false;
  if(digest!=null)
   try{canRecord=authorize(t,project,true)==actor;}
   catch(UnauthorizedAccessException){}
   catch(ArgumentException){} // Closed project or unavailable planning state.
  return new(effort,digest,history,canRecord);
 }

 // Internal recovery qualification only. Caller owns the read transaction.
 internal static EffortBudgetVerification VerifyDatabase(SqliteConnection db,SqliteTransaction t)
 {
  var store=new EffortBudgetStore(db,(_,_,_)=>throw new UnauthorizedAccessException(),(_,_)=>throw new InvalidOperationException());
  if(store.Count(t,"PRAGMA user_version") is not (48 or 49))throw new ArgumentException("Budget verification requires candidate schema 48 or 49.");
  const string actions="('RecordEffortBudgetRevision','RecordEffortForecast')";
  if(store.Count(t,"SELECT count(*) FROM audit_events a WHERE a.action IN "+actions+" AND (a.entity!='Project' OR a.entity_id IS NULL OR NOT EXISTS(SELECT 1 FROM projects p WHERE p.id=a.entity_id))")!=0)
   throw new InvalidOperationException("Invalid budget audit project.");
  if(store.Count(t,"SELECT EXISTS(SELECT operation_key FROM (SELECT operation_key FROM effort_budget_revisions UNION ALL SELECT operation_key FROM effort_forecast_records) GROUP BY operation_key HAVING count(*)>1)")!=0||
     store.Count(t,"SELECT EXISTS(SELECT audit_id FROM (SELECT audit_id FROM effort_budget_revisions UNION ALL SELECT audit_id FROM effort_forecast_records) GROUP BY audit_id HAVING count(*)>1)")!=0)
   throw new InvalidOperationException("Duplicate global budget identity.");
  var revisions=store.Count(t,"SELECT count(*) FROM effort_budget_revisions");
  var forecasts=store.Count(t,"SELECT count(*) FROM effort_forecast_records");
  if(revisions+forecasts>10000)throw new ArgumentException("Budget verification exceeds supported row limit.");
  var projects=new List<long>();
  using(var q=store.Q(t,"SELECT project_id FROM effort_budget_revisions UNION SELECT project_id FROM effort_forecast_records UNION SELECT entity_id FROM audit_events WHERE action IN "+actions+" ORDER BY 1"))
  using(var r=q.ExecuteReader())while(r.Read())
  {
   if(projects.Count==500||r.IsDBNull(0))throw new ArgumentException("Budget verification exceeds supported project limit.");
   projects.Add(r.GetInt64(0));
  }
  var evidence=new List<object>();int checkedRevisions=0,checkedForecasts=0;
  foreach(var project in projects)
  {
   if(project<=0||store.Count(t,"SELECT count(*) FROM projects WHERE id=$p",("$p",project))!=1)
    throw new InvalidOperationException("Budget history project missing.");
   var history=store.ReadCore(t,project);
   checkedRevisions+=history.Budgets.Count;checkedForecasts+=history.Forecasts.Count;
   evidence.Add(new{ProjectId=project,Budgets=history.Budgets.Select(x=>BudgetMetadataHash(x.Model,x.Operation,x.Actor,x.Utc,x.AuditId)).ToArray(),
    Forecasts=history.Forecasts.Select(x=>x.Hash).ToArray()});
  }
  if(checkedRevisions!=revisions||checkedForecasts!=forecasts||store.Count(t,"SELECT count(*) FROM audit_events WHERE action IN "+actions)!=revisions+forecasts)
   throw new InvalidOperationException("Budget history and audit census mismatch.");
  return new(projects.Count,checkedRevisions,checkedForecasts,Hash(new{Format="CRC-EffortBudgetDatabaseWitness-v1",Projects=evidence}));
 }

}
