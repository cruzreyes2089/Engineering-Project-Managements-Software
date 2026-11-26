using Microsoft.Data.Sqlite;
using PracticeManager.Core;
namespace PracticeManager.Persistence;
public sealed partial class LocalPractice
{
 private static bool ProposalConflictCleared(SqliteConnection c,SqliteTransaction t,long id,int revision,string hash)=>Convert.ToInt64(Scalar(c,t,"SELECT count(*) FROM proposal_conflict_current r JOIN jurisdiction_policy_current j ON j.id=r.jurisdiction_event_id WHERE r.proposal_id=$p AND r.revision=$r AND r.snapshot_hash=$h AND r.disposition='Clear'",("$p",id),("$r",revision),("$h",hash)))==1;
 private long RequireProposalCompliance(SqliteConnection c,SqliteTransaction t,bool record)
 {
  var a=Require(c,t,"Compliance.View");Require(c,t,"Engagements.View");Require(c,t,"Clients.View");if(record)Require(c,t,"Compliance.Record");return a;
 }
 public string ReadProposalConflictStatus(long id,int revision)
 {
  using var c=Open();using var t=c.BeginTransaction();RequireProposalCompliance(c,t,false);
  var (_,snapshot)=DraftProposalSnapshot(c,t,id,revision);
  return ProposalConflictCleared(c,t,id,revision,HashProposal(snapshot))?"Clear":Scalar(c,t,"SELECT id FROM proposal_conflict_current WHERE proposal_id=$p",("$p",id)) is long?"Hold / review outdated":"NotReviewed";
 }
 public IReadOnlyList<ConflictReview> ReadProposalConflictHistory(long id)
 {
  using var c=Open();using var t=c.BeginTransaction();RequireProposalCompliance(c,t,false);
  if(Convert.ToInt64(Scalar(c,t,"SELECT count(*) FROM proposals WHERE id=$p",("$p",id)))!=1)throw new ArgumentException("Proposal unavailable.");
  using var q=Command(c,t,@"SELECT id,jurisdiction,restricted_jurisdiction,public_duty_overlap,public_resources,other_conflict,evidence,CASE WHEN restricted_jurisdiction='No' AND public_duty_overlap='No' AND public_resources='No' AND other_conflict='No' AND EXISTS(SELECT 1 FROM jurisdiction_policy_events j WHERE j.id=proposal_conflict_reviews.jurisdiction_event_id AND j.jurisdiction=proposal_conflict_reviews.jurisdiction COLLATE NOCASE AND j.decision='EligibleForScreening') THEN 'Clear' ELSE 'Hold' END,reviewed_utc FROM proposal_conflict_reviews WHERE proposal_id=$p ORDER BY id DESC LIMIT 501",("$p",id));using var r=q.ExecuteReader();var rows=new List<ConflictReview>();while(r.Read()){if(rows.Count==500)throw new ArgumentException("Proposal conflict history exceeds development limit.");rows.Add(new(r.GetInt64(0),r.GetString(1),r.GetString(2),r.GetString(3),r.GetString(4),r.GetString(5),r.GetString(6),r.GetString(7),r.GetString(8)));}return rows.AsReadOnly();
 }
 public ConflictReview RecordProposalConflictReview(long id,int revision,long? expectedReview,ConflictReviewInput input,bool confirmed)
 {
  using var c=Open();using var t=c.BeginTransaction(deferred:false);var actor=RequireProposalCompliance(c,t,true);
  var (client,snapshot)=DraftProposalSnapshot(c,t,id,revision);ActiveClient(c,t,client);
  if(!confirmed)throw new ArgumentException("Explicit review of current facts required.");
  var old=Scalar(c,t,"SELECT id FROM proposal_conflict_current WHERE proposal_id=$p",("$p",id));
  if((old is long prior?(long?)prior:null)!=expectedReview)throw new ArgumentException("Proposal review changed. Refresh.");
  if(new[]{input.RestrictedJurisdiction,input.PublicDutyOverlap,input.PublicResources,input.OtherConflict}.Any(x=>x is not ("No" or "Yes" or "Unknown")))throw new ArgumentException("Answer every screening question.");
  var jurisdiction=JurisdictionName(input.Jurisdiction);var evidence=Validation.Text(input.Evidence,4000,"Review evidence",true);
  Exec(c,t,"INSERT INTO proposal_conflict_reviews(proposal_id,revision,snapshot_hash,jurisdiction,restricted_jurisdiction,public_duty_overlap,public_resources,other_conflict,evidence,reviewed_by,reviewed_utc,jurisdiction_event_id) VALUES($p,$v,$h,$j,$r,$d,$s,$o,$e,$a,$u,$jv)",("$p",id),("$v",revision),("$h",HashProposal(snapshot)),("$j",jurisdiction),("$r",input.RestrictedJurisdiction),("$d",input.PublicDutyOverlap),("$s",input.PublicResources),("$o",input.OtherConflict),("$e",evidence),("$a",actor),("$u",UtcNow()),("$jv",JurisdictionEvent(c,t,jurisdiction)));
  var key=Last(c,t);var state=ProposalConflictCleared(c,t,id,revision,HashProposal(snapshot))?"Clear":"Hold";var now=Convert.ToString(Scalar(c,t,"SELECT reviewed_utc FROM proposal_conflict_reviews WHERE id=$i",("$i",key)))!;
  Audit(c,t,actor,"Conflict"+state,"Proposal",id);t.Commit();return new(key,jurisdiction,input.RestrictedJurisdiction,input.PublicDutyOverlap,input.PublicResources,input.OtherConflict,evidence,state,now);
 }
}
