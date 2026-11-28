using Microsoft.Data.Sqlite;
using PracticeManager.Core;
namespace PracticeManager.Persistence;
public sealed record JurisdictionPolicyEvent(long Id,string Jurisdiction,string Decision,string Evidence,string RecordedUtc,bool Current);
public sealed partial class LocalPractice
{
 private static long? JurisdictionEvent(SqliteConnection c,SqliteTransaction t,string jurisdiction)
 {
  var v=Scalar(c,t,"SELECT id FROM jurisdiction_policy_current WHERE jurisdiction=$j COLLATE NOCASE",("$j",jurisdiction));return v is long id?id:null;
 }
 private static string JurisdictionName(string name)
 {
  var n=Validation.Text(name,300,"Canonical permitting jurisdiction",true);
  if(n.Any(char.IsControl)||n.Any(ch=>ch>127))throw new ArgumentException("Use the canonical English jurisdiction label with ASCII characters.");
  return n;
 }
 public IReadOnlyList<JurisdictionPolicyEvent> ReadJurisdictionPolicy(bool currentOnly=true)
 {
  using var c=Open();using var t=c.BeginTransaction();Require(c,t,"Compliance.View");
  using var q=Command(c,t,@"SELECT e.id,e.jurisdiction,e.decision,e.evidence,e.recorded_utc,EXISTS(SELECT 1 FROM jurisdiction_policy_current j WHERE j.id=e.id) FROM jurisdiction_policy_events e WHERE $current=0 OR EXISTS(SELECT 1 FROM jurisdiction_policy_current j WHERE j.id=e.id) ORDER BY e.jurisdiction COLLATE NOCASE,e.id DESC LIMIT 501",("$current",currentOnly?1:0));using var r=q.ExecuteReader();var rows=new List<JurisdictionPolicyEvent>();while(r.Read()){if(rows.Count==500)throw new ArgumentException("Jurisdiction policy register exceeds development limit.");rows.Add(new(r.GetInt64(0),r.GetString(1),r.GetString(2),r.GetString(3),r.GetString(4),r.GetInt64(5)==1));}return rows.AsReadOnly();
 }
 public JurisdictionPolicyEvent RecordJurisdictionPolicy(string jurisdiction,long? expectedEvent,string decision,string evidence,bool confirmed)
 {
  using var c=Open();using var t=c.BeginTransaction(deferred:false);var actor=RequireAdministration(c,t);Require(c,t,"Compliance.Policy");Require(c,t,"Compliance.View");
  var name=JurisdictionName(jurisdiction);var note=Validation.Text(evidence,4000,"Policy evidence",true);
  if(!confirmed||decision is not ("EligibleForScreening" or "Restricted" or "Unverified"))throw new ArgumentException("Explicit policy review and valid decision required.");
  if(JurisdictionEvent(c,t,name)!=expectedEvent)throw new ArgumentException("Jurisdiction policy changed. Refresh.");
  Exec(c,t,"INSERT INTO jurisdiction_policy_events(jurisdiction,decision,evidence,recorded_by,recorded_utc) VALUES($j,$d,$n,$a,$u)",("$j",name),("$d",decision),("$n",note),("$a",actor),("$u",UtcNow()));
  var id=Last(c,t);var now=Convert.ToString(Scalar(c,t,"SELECT recorded_utc FROM jurisdiction_policy_events WHERE id=$i",("$i",id)))!;
  Audit(c,t,actor,"Jurisdiction"+decision,"JurisdictionPolicy",id);t.Commit();return new(id,name,decision,note,now,true);
 }
}
