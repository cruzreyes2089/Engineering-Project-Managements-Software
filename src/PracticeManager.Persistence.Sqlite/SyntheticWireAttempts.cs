using Microsoft.Data.Sqlite;
using PracticeManager.Core;
namespace PracticeManager.Persistence;

// Synthetic dispatch journal: no HTTP transport or authority to send real money.
// One attempt per immutable draft, never reset or reissued after a restart.
public sealed record WireAttemptResult(string Outcome,string? State=null,Guid? AttemptKey=null)
{
 public bool MaySend=>false;
 public bool MayRetry=>false;
 public override string ToString()=>$"Synthetic attempt: {Outcome}, {State}";
}
public sealed partial class LocalPractice
{
 private sealed record WireAttemptRow(long Id,string Key,string State);
 private static WireAttemptRow? ReadAttempt(SqliteConnection c,SqliteTransaction t,long draftId)
 {
  using var q=Command(c,t,"SELECT id,attempt_key,state FROM accounting_wire_attempts WHERE draft_id=$d",("$d",draftId));
  using var r=q.ExecuteReader();return r.Read()?new(r.GetInt64(0),r.GetString(1),r.GetString(2)):null;
 }
 private static bool HasUnresolvedWireAttempt(SqliteConnection c,SqliteTransaction t)=>Convert.ToInt64(Scalar(c,t,"SELECT count(*) FROM accounting_wire_attempts WHERE state!='ReviewedSimulation'"))!=0;
 private void RequireWireAttempt(SqliteConnection c,SqliteTransaction t)
 {
  RequireRehearsal(c,t);Require(c,t,"Accounting.WireDraft.Prepare");Require(c,t,"Accounting.WireDraft.Rehearse");
 }
 private static void ChangeAttempt(SqliteConnection c,SqliteTransaction t,WireAttemptRow a,long actor,string state,string reason)
 {
  if(Exec(c,t,"UPDATE accounting_wire_attempts SET state=$s,reason=$r,updated_by=$a,updated_utc=$u WHERE id=$i AND state=$old",("$s",state),("$r",reason),("$a",actor),("$u",UtcNow()),("$i",a.Id),("$old",a.State))!=1)throw new InvalidOperationException("Attempt changed.");
  Audit(c,t,actor,"WireAttempt"+state,"AccountingWireAttempt",a.Id);
 }
 public WireAttemptResult ReserveSyntheticWireAttempt(Guid operation,string expectedFingerprint)
 {
  using var c=Open();using var t=c.BeginTransaction(deferred:false);
  RequireWireAttempt(c,t);var scope=RehearsalScope(c,t);if(scope.Block!=null)return new(scope.Block);
  var d=ReadWireDraftRow(c,t,operation);if(d==null)return new("NotRecorded");
  var a=ReadAttempt(c,t,d.Id);if(a!=null)return new("AttemptAlreadyRecorded",a.State);
  var p=ReconstructWireDraft(d);if(p==null||p.Fingerprint!=expectedFingerprint||p.Intent.MarkerKey!=scope.Marker)return new("IntentConflict");
  if(!WireApprovalValid(c,t,d))return new("ApprovalRequired");
  if(HasUnresolvedWireAttempt(c,t)||Convert.ToInt64(Scalar(c,t,"SELECT count(*) FROM accounting_posting_rehearsals WHERE state IN ('InFlight','Uncertain')"))!=0)return new("UnresolvedRehearsal");
  var key=Guid.NewGuid();var now=UtcNow();
  Exec(c,t,"INSERT INTO accounting_wire_attempts(draft_id,attempt_key,state,reason,created_by,created_utc,updated_by,updated_utc) VALUES($d,$k,'Reserved','OfflineReservation',$a,$u,$a,$u)",("$d",d.Id),("$k",key.ToString("D")),("$a",scope.Actor),("$u",now));
  Audit(c,t,scope.Actor,"ReserveWireAttempt","AccountingWireAttempt",Last(c,t));t.Commit();
  return new("ReservedForSimulation","Reserved",key);
 }
 public WireAttemptResult WitnessSyntheticWireAttempt(Guid operation,Guid attempt,SyntheticAttemptWitness witness)
 {
  using var c=Open();using var t=c.BeginTransaction(deferred:false);
  RequireWireAttempt(c,t);var scope=RehearsalScope(c,t);if(scope.Block!=null)return new(scope.Block);
  var d=ReadWireDraftRow(c,t,operation);if(d==null)return new("NotRecorded");
  var a=ReadAttempt(c,t,d.Id);
  if(a==null||a.Key!=attempt.ToString("D")||a.State!="Reserved")return new("AttemptNotAvailable",a?.State);
  var p=ReconstructWireDraft(d);
  if(p==null||p.Intent.MarkerKey!=scope.Marker||!WireApprovalValid(c,t,d))return new("ApprovalOrPayloadInvalid",a.State);
  // Existing witness evidence is not permission to resume. If commit fails
  // after file creation, Reserved remains unresolved and the file is retained.
  var result=witness.Record(p.Intent,attempt);
  var state=result.Outcome=="RecordedForReview"?"Witnessed":"Uncertain";
  ChangeAttempt(c,t,a,scope.Actor,state,state=="Witnessed"?"NewWitnessRecorded":"WitnessUnavailableOrExisting");
  t.Commit();return new(state,state);
 }
 public WireAttemptResult AssessSyntheticWireAttempt(Guid operation,Guid attempt,SyntheticDispatchObservation observation)
 {
  using var c=Open();using var t=c.BeginTransaction(deferred:false);
  RequireWireAttempt(c,t);var scope=RehearsalScope(c,t);if(scope.Block!=null)return new(scope.Block);
  var d=ReadWireDraftRow(c,t,operation);if(d==null)return new("NotRecorded");
  var a=ReadAttempt(c,t,d.Id);
  if(a==null||a.Key!=attempt.ToString("D")||a.State!="Witnessed")return new("AttemptNotAvailable",a?.State);
  var p=ReconstructWireDraft(d);if(p==null||p.Intent.MarkerKey!=scope.Marker)return new("IntentConflict");
  var result=SyntheticDispatchReview.Evaluate(observation);
  var state=result.State==SyntheticDispatchState.ConfirmationForReview?"ConfirmationForReview":"Uncertain";
  ChangeAttempt(c,t,a,scope.Actor,state,result.Reason);t.Commit();return new(state,state);
 }
 public WireAttemptResult ReviewSyntheticWireAttempt(Guid operation,string expectedFingerprint)
 {
  using var c=Open();using var t=c.BeginTransaction(deferred:false);
  RequireWireAttempt(c,t);Require(c,t,"Accounting.WireDraft.Approve");
  var scope=RehearsalScope(c,t);if(scope.Block!=null)return new(scope.Block);
  var d=ReadWireDraftRow(c,t,operation);if(d==null)return new("NotRecorded");
  var a=ReadAttempt(c,t,d.Id);if(a==null||a.State!="ConfirmationForReview")return new("ManualRecoveryRequired",a?.State);
  var p=ReconstructWireDraft(d);if(p==null||p.Fingerprint!=expectedFingerprint||p.Intent.MarkerKey!=scope.Marker||!WireApprovalValid(c,t,d))return new("ApprovalOrPayloadInvalid");
  ChangeAttempt(c,t,a,scope.Actor,"ReviewedSimulation","OfflineSimulationOnly");t.Commit();return new("ReviewedSimulation","ReviewedSimulation");
 }
 public WireAttemptResult ReadSyntheticWireAttempt(Guid operation)
 {
  using var c=Open();using var t=c.BeginTransaction();RequireWireAttempt(c,t);
  var d=ReadWireDraftRow(c,t,operation);if(d==null)return new("NotRecorded");
  var a=ReadAttempt(c,t,d.Id);return a==null?new("NotAttempted"):new("Recorded",a.State);
 }
}
