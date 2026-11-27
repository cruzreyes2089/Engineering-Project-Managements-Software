using Microsoft.Data.Sqlite;
using PracticeManager.Core;
namespace PracticeManager.Persistence;
public sealed record ConflictReview(long Id,string Jurisdiction,string RestrictedJurisdiction,string PublicDutyOverlap,string PublicResources,string OtherConflict,string Evidence,string Disposition,string ReviewedUtc,long? JurisdictionPolicyEventId=null,long? ReviewerId=null,long PartyEvidenceRevision=0);
public sealed record ConflictReviewInput(string Jurisdiction,string RestrictedJurisdiction,string PublicDutyOverlap,string PublicResources,string OtherConflict,string Evidence);
public sealed partial class LocalPractice
{
 private static bool ConflictCleared(SqliteConnection c,SqliteTransaction t,long project)=>Convert.ToInt64(Scalar(c,t,"SELECT count(*) FROM project_conflict_cleared WHERE project_id=$p",("$p",project)))==1;
 private static ConflictReview? CurrentConflict(SqliteConnection c,SqliteTransaction t,long project)
 {
  using var q=Command(c,t,"SELECT id,jurisdiction,restricted_jurisdiction,public_duty_overlap,public_resources,other_conflict,evidence,disposition,reviewed_utc,jurisdiction_event_id,reviewed_by,party_revision FROM project_conflict_current WHERE project_id=$p",("$p",project));using var r=q.ExecuteReader();
  return r.Read()?new(r.GetInt64(0),r.GetString(1),r.GetString(2),r.GetString(3),r.GetString(4),r.GetString(5),r.GetString(6),r.GetString(7),r.GetString(8),r.IsDBNull(9)?null:r.GetInt64(9),r.GetInt64(10),r.GetInt64(11)):null;
 }
 public string ReadProjectConflictStatus(long project)
 {
  using var c=Open();using var t=c.BeginTransaction();RequireProjectView(c,t);ProjectRow(c,t,project);
  return ConflictCleared(c,t,project)?"Clear":CurrentConflict(c,t,project)==null?"NotReviewed":"Hold";
 }
 public IReadOnlyList<ConflictReview> ReadConflictHistory(long project)
 {
  using var c=Open();using var t=c.BeginTransaction();RequireProjectView(c,t);Require(c,t,"Compliance.View");ProjectRow(c,t,project);
  using var q=Command(c,t,@"SELECT id,jurisdiction,restricted_jurisdiction,public_duty_overlap,public_resources,other_conflict,evidence,CASE WHEN restricted_jurisdiction='No' AND public_duty_overlap='No' AND public_resources='No' AND other_conflict='No' AND EXISTS(SELECT 1 FROM jurisdiction_policy_events j WHERE j.id=project_conflict_reviews.jurisdiction_event_id AND j.jurisdiction=project_conflict_reviews.jurisdiction COLLATE NOCASE AND j.decision='EligibleForScreening') THEN 'Clear' ELSE 'Hold' END,reviewed_utc,jurisdiction_event_id,reviewed_by,party_revision FROM project_conflict_reviews WHERE project_id=$p ORDER BY id DESC LIMIT 501",("$p",project));using var r=q.ExecuteReader();var rows=new List<ConflictReview>();while(r.Read()){if(rows.Count==500)throw new ArgumentException("Conflict history exceeds development limit.");rows.Add(new(r.GetInt64(0),r.GetString(1),r.GetString(2),r.GetString(3),r.GetString(4),r.GetString(5),r.GetString(6),r.GetString(7),r.GetString(8),r.IsDBNull(9)?null:r.GetInt64(9),r.GetInt64(10),r.GetInt64(11)));}return rows.AsReadOnly();
 }
 public ConflictReview RecordConflictReview(long project,long? expectedReview,ConflictReviewInput input,bool confirmed,long? expectedPartyRevision=null)
 {
  using var c=Open();using var t=c.BeginTransaction(deferred:false);var actor=Require(c,t,"Compliance.Record");Require(c,t,"Compliance.View");RequireProjectView(c,t);var p=ProjectRow(c,t,project);
  var partyRevision=PartyRevision(c,t,project);
  if((expectedPartyRevision.HasValue||partyRevision!=0)&&expectedPartyRevision!=partyRevision)throw new ArgumentException("Party evidence changed or has not been reviewed. Refresh.");
  if(!confirmed)throw new ArgumentException("Explicit review of current facts required.");
  if(CurrentConflict(c,t,project)?.Id!=expectedReview)throw new ArgumentException("Conflict review changed. Refresh.");
  if(new[]{input.RestrictedJurisdiction,input.PublicDutyOverlap,input.PublicResources,input.OtherConflict}.Any(x=>x is not ("No" or "Yes" or "Unknown")))throw new ArgumentException("Answer every screening question.");
  var jurisdiction=JurisdictionName(input.Jurisdiction);var evidence=Validation.Text(input.Evidence,4000,"Review evidence",true);
  Exec(c,t,"INSERT INTO project_conflict_reviews(project_id,proposal_hash,jurisdiction,restricted_jurisdiction,public_duty_overlap,public_resources,other_conflict,evidence,reviewed_by,reviewed_utc,jurisdiction_event_id,party_revision) VALUES($p,$h,$j,$r,$d,$s,$o,$e,$a,$u,$jv,$pv)",("$p",project),("$h",p.ProposalHash),("$j",jurisdiction),("$r",input.RestrictedJurisdiction),("$d",input.PublicDutyOverlap),("$s",input.PublicResources),("$o",input.OtherConflict),("$e",evidence),("$a",actor),("$u",UtcNow()),("$jv",JurisdictionEvent(c,t,jurisdiction)),("$pv",partyRevision));
  var next=CurrentConflict(c,t,project)!;Audit(c,t,actor,"Conflict"+(ConflictCleared(c,t,project)?"Clear":"Hold"),"Project",project);t.Commit();return next;
 }
}
