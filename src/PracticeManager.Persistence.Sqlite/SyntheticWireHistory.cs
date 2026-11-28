using Microsoft.Data.Sqlite;
namespace PracticeManager.Persistence;

// Read-only history; no attempt keys, raw payloads, provider identifiers or
// credentials. Keyset pagination uses immutable local row IDs, not offsets.
public sealed record WireHistoryItem(string Reference,string Kind,string DraftState,string AttemptState,string Reason,string CreatedUtc,string? AttemptUpdatedUtc,string AssessmentEvidence="NotRecorded",int? ObservedStatus=null,string? RecoveryFinding=null);
public sealed record WireHistoryPage(IReadOnlyList<WireHistoryItem> Items,long? NextBeforeId)
{
 public override string ToString()=>$"Synthetic history: {Items.Count} rows";
}
public sealed partial class LocalPractice
{
 public WireHistoryPage ReadSyntheticWireHistory(bool unresolvedOnly=false,long? beforeId=null,int pageSize=50)
 {
  if(pageSize<1||pageSize>100||beforeId<=0)throw new ArgumentOutOfRangeException(nameof(pageSize));
  using var c=Open();using var t=c.BeginTransaction();RequireWireAttempt(c,t);
  using var q=Command(c,t,@"SELECT d.id,d.reference,d.kind,d.state,a.state,a.reason,d.created_utc,a.updated_utc,e.assessment_state,e.status_code,e.recovery_finding
FROM accounting_wire_drafts d LEFT JOIN accounting_wire_attempts a ON a.draft_id=d.id LEFT JOIN accounting_wire_assessments e ON e.attempt_id=a.id AND e.source_hash=d.payload_hash
WHERE ($before IS NULL OR d.id<$before)
AND ($unresolved=0 OR (a.id IS NOT NULL AND a.state!='ReviewedSimulation'))
ORDER BY d.id DESC LIMIT $limit",("$before",beforeId),("$unresolved",unresolvedOnly?1:0),("$limit",pageSize+1));
  using var r=q.ExecuteReader();var items=new List<WireHistoryItem>();long last=0;bool more=false;
  while(r.Read()){
   if(items.Count==pageSize){more=true;break;}
   last=r.GetInt64(0);items.Add(new(r.GetString(1),r.GetString(2),r.GetString(3),r.IsDBNull(4)?"NotAttempted":r.GetString(4),r.IsDBNull(5)?"":r.GetString(5),r.GetString(6),r.IsDBNull(7)?null:r.GetString(7),r.IsDBNull(8)?"NotRecorded":r.GetString(8),r.IsDBNull(9)?null:r.GetInt32(9),r.IsDBNull(10)?null:r.GetString(10)));
  }
  return new(items.AsReadOnly(),more?last:null);
 }
}
