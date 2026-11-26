using System.Text.Json;
using Microsoft.Data.Sqlite;
using PracticeManager.Core;
namespace PracticeManager.Persistence;

// Synthetic full-wire queue only. There is deliberately no begin/send, posted
// state, external-ID mapping, retry, release or promotion from the old journal.
public sealed record WireDraftResult(string Outcome,string? State=null,SyntheticWirePayload? Payload=null,bool ApprovalCurrentlyValid=false)
{
 public bool MaySend=>false;
 public override string ToString()=>$"Synthetic wire draft: {Outcome}, {State}";
}
public sealed partial class LocalPractice
{
 private sealed record WireDraftRow(long Id,string Source,string Marker,string Kind,string Reference,string IntentJson,string WireJson,string Hash,string State);
 private static WireDraftRow? ReadWireDraftRow(SqliteConnection c,SqliteTransaction t,Guid operation)
 {
  using var q=Command(c,t,"SELECT id,source_key,marker_key,kind,reference,intent_json,wire_json,payload_hash,state FROM accounting_wire_drafts WHERE source_key=$k",("$k",operation.ToString("D")));
  using var r=q.ExecuteReader();
  return r.Read()?new(r.GetInt64(0),r.GetString(1),r.GetString(2),r.GetString(3),r.GetString(4),r.GetString(5),r.GetString(6),r.GetString(7),r.GetString(8)):null;
 }
 private static SyntheticWirePayload? ReconstructWireDraft(WireDraftRow row)
 {
  try{
   if(row.IntentJson.Length>8192||row.WireJson.Length>8192)return null;
   var intent=JsonSerializer.Deserialize<SyntheticWireIntent>(row.IntentJson);
   if(intent==null)return null;
   var p=SyntheticWireCompiler.Compile(intent);
   return p.Intent.OperationKey.ToString("D")==row.Source&&p.Intent.MarkerKey.ToString("D")==row.Marker&&p.Intent.Kind.ToString()==row.Kind&&
    p.Reference==row.Reference&&p.Fingerprint==row.Hash&&p.Json==row.WireJson?p:null;
  }catch(JsonException){return null;}catch(ArgumentException){return null;}
 }
 private static bool WireApprovalValid(SqliteConnection c,SqliteTransaction t,WireDraftRow row)
 {
  return row.State=="Approved"&&Convert.ToInt64(Scalar(c,t,"SELECT count(*) FROM accounting_wire_drafts d JOIN users u ON u.id=d.approved_by JOIN role_permissions rp ON rp.role_id=u.role_id WHERE d.id=$i AND u.active=1 AND u.force_password_change=0 AND rp.permission_code='Accounting.WireDraft.Approve' AND rp.allowed=1",("$i",row.Id)))==1;
 }
 public bool CanPrepareWireDraft()=>CanRehearsePosting()&&Can("Accounting.WireDraft.Prepare");
 public bool CanApproveWireDraft()=>CanPrepareWireDraft()&&Can("Accounting.WireDraft.Approve");
 public WireDraftResult PrepareSyntheticWireDraft(SyntheticWireIntent intent)
 {
  using var c=Open();using var t=c.BeginTransaction(deferred:false);
  var scope=RehearsalScope(c,t);Require(c,t,"Accounting.WireDraft.Prepare");
  if(scope.Block!=null)return new(scope.Block);
  if(intent.MarkerKey!=scope.Marker)return new("MarkerMismatch");
  var p=SyntheticWireCompiler.Compile(intent);
  if(ReadRehearsal(c,t,intent.OperationKey)!=null)return new("LegacyOperationConflict");
  var row=ReadWireDraftRow(c,t,intent.OperationKey);
  if(row!=null){
   var stored=ReconstructWireDraft(row);
   if(stored==null)return new("StoredPayloadInvalid",row.State);
   return stored.Fingerprint==p.Fingerprint?new("AlreadyRecorded",row.State):new("IntentConflict",row.State);
  }
  Exec(c,t,"INSERT INTO accounting_wire_drafts(source_key,marker_key,kind,reference,intent_json,wire_json,payload_hash,state,created_by,created_utc) VALUES($s,$m,$k,$r,$j,$w,$h,'Draft',$a,$u)",
   ("$s",intent.OperationKey.ToString("D")),("$m",intent.MarkerKey.ToString("D")),("$k",intent.Kind.ToString()),("$r",p.Reference),("$j",JsonSerializer.Serialize(intent)),("$w",p.Json),("$h",p.Fingerprint),("$a",scope.Actor),("$u",UtcNow()));
  Audit(c,t,scope.Actor,"PrepareWireDraft","AccountingWireDraft",Last(c,t));t.Commit();return new("Prepared","Draft");
 }
 public WireDraftResult ReadSyntheticWireDraft(Guid operation)
 {
  using var c=Open();using var t=c.BeginTransaction();RequireRehearsal(c,t);Require(c,t,"Accounting.WireDraft.Prepare");
  var row=ReadWireDraftRow(c,t,operation);if(row==null)return new("NotRecorded");
  var p=ReconstructWireDraft(row);if(p==null)return new("StoredPayloadInvalid",row.State);
  // Inspection remains possible during holds; validity is not send authority.
  var scope=RehearsalScope(c,t);
  return new("Recorded",row.State,p,scope.Block==null&&scope.Marker==p.Intent.MarkerKey&&WireApprovalValid(c,t,row));
 }
 public WireDraftResult ApproveSyntheticWireDraft(Guid operation,string expectedFingerprint)
 {
  using var c=Open();using var t=c.BeginTransaction(deferred:false);
  var scope=RehearsalScope(c,t);Require(c,t,"Accounting.WireDraft.Prepare");Require(c,t,"Accounting.WireDraft.Approve");
  if(scope.Block!=null)return new(scope.Block);
  var row=ReadWireDraftRow(c,t,operation);if(row==null)return new("NotRecorded");
  if(row.Marker!=scope.Marker!.Value.ToString("D"))return new("MarkerMismatch",row.State);
  var p=ReconstructWireDraft(row);if(p==null)return new("StoredPayloadInvalid",row.State);
  if(p.Fingerprint!=expectedFingerprint)return new("IntentConflict",row.State);
  if(row.State=="Approved")return new(WireApprovalValid(c,t,row)?"AlreadyApproved":"ApprovalInvalid",row.State);
  if(row.State!="Draft")return new("ApprovalNotAllowed",row.State);
  if(Exec(c,t,"UPDATE accounting_wire_drafts SET state='Approved',approved_by=$a,approved_utc=$u WHERE id=$i AND state='Draft'",("$a",scope.Actor),("$u",UtcNow()),("$i",row.Id))!=1)return new("StateChanged");
  Audit(c,t,scope.Actor,"ApproveWireDraft","AccountingWireDraft",row.Id);t.Commit();return new("Approved","Approved");
 }
 public WireDraftResult CancelSyntheticWireDraft(Guid operation,string expectedFingerprint)
 {
  using var c=Open();using var t=c.BeginTransaction(deferred:false);
  var scope=RehearsalScope(c,t);Require(c,t,"Accounting.WireDraft.Prepare");
  if(scope.Block!=null)return new(scope.Block);
  var row=ReadWireDraftRow(c,t,operation);if(row==null)return new("NotRecorded");
  if(row.Marker!=scope.Marker!.Value.ToString("D"))return new("MarkerMismatch",row.State);
  var p=ReconstructWireDraft(row);if(p==null)return new("StoredPayloadInvalid",row.State);
  if(p.Fingerprint!=expectedFingerprint)return new("IntentConflict",row.State);
  if(row.State=="Approved")Require(c,t,"Accounting.WireDraft.Approve");
  if(row.State=="Cancelled")return new("AlreadyCancelled",row.State);
  if(Exec(c,t,"UPDATE accounting_wire_drafts SET state='Cancelled',cancelled_by=$a,cancelled_utc=$u WHERE id=$i AND state IN ('Draft','Approved')",("$a",scope.Actor),("$u",UtcNow()),("$i",row.Id))!=1)return new("StateChanged");
  Audit(c,t,scope.Actor,"CancelWireDraft","AccountingWireDraft",row.Id);t.Commit();return new("Cancelled","Cancelled");
 }
}
