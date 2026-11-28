using Microsoft.Data.Sqlite;
namespace PracticeManager.Persistence;
public sealed partial class LocalPractice
{
 // Explicit caller-selected fixture scope, not a bound or authenticated store.
 // Does not create, modify, repair, enroll or delete any file or database row.
 public WitnessComparison CompareSyntheticAttemptWitness(string directory,Guid storeKey)
 {
  using var c=Open();using var t=c.BeginTransaction();RequireWireAttempt(c,t);
  var marker=ReadMarker(c,t);
  if(marker==null)return new("MarkerNotEnrolled",0,0,0,0,0,0);
  var expected=new List<WitnessExpectation>();
  using(var q=Command(c,t,@"SELECT d.id,d.source_key,d.marker_key,d.kind,d.reference,d.intent_json,d.wire_json,d.payload_hash,d.state,a.attempt_key,a.state FROM accounting_wire_attempts a JOIN accounting_wire_drafts d ON d.id=a.draft_id ORDER BY a.id LIMIT 501"))
  using(var r=q.ExecuteReader()){
   while(r.Read()){
    if(expected.Count==500)return new("WitnessInventoryLimit",501,0,0,0,0,0);
    var row=new WireDraftRow(r.GetInt64(0),r.GetString(1),r.GetString(2),r.GetString(3),r.GetString(4),r.GetString(5),r.GetString(6),r.GetString(7),r.GetString(8));
    var p=ReconstructWireDraft(row);
    if(p==null||p.Intent.MarkerKey!=marker||!Guid.TryParseExact(r.GetString(9),"D",out var key)||key==Guid.Empty)return new("StoredAttemptScopeInvalid",0,0,0,0,0,0);
    expected.Add(new(p.Intent,key,r.GetString(10)));
   }
  }
  // A zero-record/empty scope can match, but never conveys authority or absence
  // of external activity. This does not inspect the earlier rehearsal journal.
  return new SyntheticAttemptWitness(directory,storeKey,marker.Value).Compare(expected);
 }
}
