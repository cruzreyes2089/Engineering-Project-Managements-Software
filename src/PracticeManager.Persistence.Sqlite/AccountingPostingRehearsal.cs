using Microsoft.Data.Sqlite;
using PracticeManager.Core;
namespace PracticeManager.Persistence;

public sealed record PostingRehearsalStatus(string? HoldReason,int Reserved,int InFlight,int Uncertain,int SimulatedConfirmed,int Cancelled)
{
 public bool Held=>HoldReason!=null;
 public override string ToString()=>"[Offline posting rehearsal status]";
}

// Offline rehearsal journal. No transport, no Manager credentials, no real
// dispatch or posting permission; simulated external IDs cannot become mappings.
public sealed partial class LocalPractice
{
 private sealed record RehearsalRow(long Id,string Marker,string Kind,string Hash,string State,string? Attempt,string? External);
 private static RehearsalRow? ReadRehearsal(SqliteConnection c,SqliteTransaction t,Guid operation)
 {
  using var q=Command(c,t,"SELECT id,marker_key,kind,payload_hash,state,attempt_key,simulated_external_key FROM accounting_posting_rehearsals WHERE source_key=$k",("$k",operation.ToString("D")));
  using var r=q.ExecuteReader();
  return r.Read()?new(r.GetInt64(0),r.GetString(1),r.GetString(2),r.GetString(3),r.GetString(4),r.IsDBNull(5)?null:r.GetString(5),r.IsDBNull(6)?null:r.GetString(6)):null;
 }
 private long RequireRehearsal(SqliteConnection c,SqliteTransaction t)
 {
  var actor=RequireDirectory(c,t);Require(c,t,"Accounting.Posting.Rehearse");return actor;
 }
 private static bool LifecycleHeld(SqliteConnection c,SqliteTransaction t)=>Convert.ToInt64(Scalar(c,t,"SELECT count(*) FROM accounting_lifecycle_hold"))!=0;
 private (long Actor,Guid? Marker,string? Block) RehearsalScope(SqliteConnection c,SqliteTransaction t)
 {
  var actor=RequireRehearsal(c,t);
  if(LifecycleHeld(c,t))return (actor,null,"LifecycleHold");
  var marker=ReadMarker(c,t);var profile=ReadProfile(c,t,actor);
  if(marker==null||profile==null)return (actor,null,"FixtureScopeRequired");
  var context=new DirectoryContext(actor,sessionGeneration,profile,marker.Value);
  if(DirectoryBusiness(c,t,context)!="CRC ACCOUNTING TEST")return (actor,null,"SyntheticDirectoryRequired");
  return (actor,marker,null);
 }
 private static bool MatchesIntent(RehearsalRow row,Guid marker,SyntheticPostingIntent intent,string hash)
  =>row.Marker==marker.ToString("D")&&row.Kind==intent.Kind.ToString()&&row.Hash==hash;
 public bool CanRehearsePosting()=>CanSyncDirectory()&&Can("Accounting.Posting.Rehearse");
 public RehearsalResult ReserveSyntheticPosting(SyntheticPostingIntent intent)
 {
  using var c=Open();using var t=c.BeginTransaction(deferred:false);
  var scope=RehearsalScope(c,t);if(scope.Block!=null)return new(scope.Block);
  var hash=intent.Fingerprint();
  var row=ReadRehearsal(c,t,intent.OperationKey);
  if(row!=null)return MatchesIntent(row,scope.Marker!.Value,intent,hash)?new("AlreadyRecorded",row.State):new("IntentConflict",row.State);
  var now=UtcNow();
  Exec(c,t,"INSERT INTO accounting_posting_rehearsals(source_key,marker_key,kind,payload_hash,state,created_by,updated_by,created_utc,updated_utc) VALUES($s,$m,$k,$h,'Reserved',$a,$a,$u,$u)",
   ("$s",intent.OperationKey.ToString("D")),("$m",scope.Marker!.Value.ToString("D")),("$k",intent.Kind.ToString()),("$h",hash),("$a",scope.Actor),("$u",now));
  Audit(c,t,scope.Actor,"ReserveRehearsal","AccountingPostingRehearsal",Last(c,t));t.Commit();return new("Reserved","Reserved");
 }
 public RehearsalResult BeginSyntheticPosting(SyntheticPostingIntent intent)
 {
  using var c=Open();using var t=c.BeginTransaction(deferred:false);
  var scope=RehearsalScope(c,t);if(scope.Block!=null)return new(scope.Block);
  var hash=intent.Fingerprint();var row=ReadRehearsal(c,t,intent.OperationKey);
  if(row==null)return new("NotReserved");
  if(!MatchesIntent(row,scope.Marker!.Value,intent,hash))return new("IntentConflict",row.State);
  if(row.State!="Reserved")return new("DispatchNotAllowed",row.State);
  if(Convert.ToInt64(Scalar(c,t,"SELECT count(*) FROM accounting_posting_rehearsals WHERE state IN ('InFlight','Uncertain')"))!=0)return new("UnresolvedRehearsal",row.State);
  var attempt=Guid.NewGuid();
  if(Exec(c,t,"UPDATE accounting_posting_rehearsals SET state='InFlight',attempt_key=$x,updated_by=$a,updated_utc=$u WHERE id=$i AND state='Reserved'",("$x",attempt.ToString("D")),("$a",scope.Actor),("$u",UtcNow()),("$i",row.Id))!=1)return new("StateChanged");
  Audit(c,t,scope.Actor,"BeginRehearsal","AccountingPostingRehearsal",row.Id);t.Commit();
  // Only the winning transition returns a capability, never subsequent reads.
  return new("Started","InFlight",attempt);
 }
 public RehearsalResult FinishSyntheticPosting(SyntheticPostingIntent intent,Guid attempt,SimulatedOutcome outcome,Guid? simulatedExternal=null)
 {
  using var c=Open();using var t=c.BeginTransaction(deferred:false);
  var scope=RehearsalScope(c,t);if(scope.Block!=null)return new(scope.Block);
  var hash=intent.Fingerprint();var row=ReadRehearsal(c,t,intent.OperationKey);
  if(row==null)return new("NotReserved");
  if(!MatchesIntent(row,scope.Marker!.Value,intent,hash))return new("IntentConflict",row.State);
  if(row.State!="InFlight"||attempt==Guid.Empty||row.Attempt!=attempt.ToString("D"))return new("DispatchNotAllowed",row.State);
  if(!Enum.IsDefined(outcome)||(outcome==SimulatedOutcome.Confirmed&&(!simulatedExternal.HasValue||simulatedExternal==Guid.Empty))||(outcome==SimulatedOutcome.Uncertain&&simulatedExternal.HasValue))return new("InvalidSimulatedOutcome",row.State);
  var external=simulatedExternal?.ToString("D");
  if(external!=null&&Convert.ToInt64(Scalar(c,t,"SELECT count(*) FROM accounting_posting_rehearsals WHERE simulated_external_key=$x",("$x",external)))!=0)return new("SimulatedExternalConflict",row.State);
  var state=outcome==SimulatedOutcome.Confirmed?"SimulatedConfirmed":"Uncertain";
  if(Exec(c,t,"UPDATE accounting_posting_rehearsals SET state=$s,simulated_external_key=$x,updated_by=$a,updated_utc=$u WHERE id=$i AND state='InFlight' AND attempt_key=$k",("$s",state),("$x",external),("$a",scope.Actor),("$u",UtcNow()),("$i",row.Id),("$k",attempt.ToString("D")))!=1)return new("StateChanged");
  Audit(c,t,scope.Actor,"FinishRehearsal","AccountingPostingRehearsal",row.Id);t.Commit();return new(state,state);
 }
 public RehearsalResult CancelSyntheticPosting(SyntheticPostingIntent intent)
 {
  using var c=Open();using var t=c.BeginTransaction(deferred:false);
  var scope=RehearsalScope(c,t);if(scope.Block!=null)return new(scope.Block);
  var hash=intent.Fingerprint();var row=ReadRehearsal(c,t,intent.OperationKey);
  if(row==null)return new("NotReserved");
  if(!MatchesIntent(row,scope.Marker!.Value,intent,hash))return new("IntentConflict",row.State);
  if(row.State!="Reserved")return new("CancellationNotAllowed",row.State);
  if(Exec(c,t,"UPDATE accounting_posting_rehearsals SET state='Cancelled',updated_by=$a,updated_utc=$u WHERE id=$i AND state='Reserved'",("$a",scope.Actor),("$u",UtcNow()),("$i",row.Id))!=1)return new("StateChanged");
  Audit(c,t,scope.Actor,"CancelRehearsal","AccountingPostingRehearsal",row.Id);t.Commit();return new("Cancelled","Cancelled");
 }
 public RehearsalResult ReadSyntheticPostingState(Guid operation)
 {
  using var c=Open();using var t=c.BeginTransaction();RequireRehearsal(c,t);
  var row=ReadRehearsal(c,t,operation);return row==null?new("NotReserved"):new("Recorded",row.State);
 }
 // Explicit owner-declared clone/restore incident. Sticky and local only.
 // No clear/reset method exists until an independently verified recovery protocol.
 public RehearsalResult DeclareAccountingLifecycleHold(LifecycleHoldReason reason)
 {
  using var c=Open();using var t=c.BeginTransaction(deferred:false);
  var actor=RequireRehearsal(c,t);
  if(!Enum.IsDefined(reason))throw new ArgumentException("Invalid hold reason.");
  if(LifecycleHeld(c,t))return new("LifecycleHold");
  Exec(c,t,"INSERT INTO accounting_lifecycle_hold(id,reason,declared_by,declared_utc) VALUES(1,$r,$a,$u)",("$r",reason.ToString()),("$a",actor),("$u",UtcNow()));
  Audit(c,t,actor,"DeclareLifecycleHold","AccountingLifecycleHold",1);t.Commit();return new("LifecycleHold");
 }
 public PostingRehearsalStatus ReadPostingRehearsalStatus()
 {
  using var c=Open();using var t=c.BeginTransaction();RequireRehearsal(c,t);
  var hold=Scalar(c,t,"SELECT reason FROM accounting_lifecycle_hold WHERE id=1") as string;
  int Count(string state)=>Convert.ToInt32(Scalar(c,t,"SELECT count(*) FROM accounting_posting_rehearsals WHERE state=$s",("$s",state)));
  return new(hold,Count("Reserved"),Count("InFlight"),Count("Uncertain"),Count("SimulatedConfirmed"),Count("Cancelled"));
 }
 public bool IsAccountingLifecycleHeld()
 {
  using var c=Open();using var t=c.BeginTransaction();RequireRehearsal(c,t);return LifecycleHeld(c,t);
 }
}
