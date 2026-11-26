using Microsoft.Data.Sqlite;
namespace PracticeManager.Persistence;
public sealed record OwnerApprovalPolicy(long? EventId,bool Enabled,bool Eligible,string? Reason);
public sealed record OwnerPolicyEvent(long Id,string Owner,string Change,string Reason,string Actor,string RecordedUtc);
public sealed record OwnerDecisionEvidence(string Kind,long RecordId,string Decision,string Actor,string Justification,string RecordedUtc,long? PolicyEventId,string EvidenceStatus);
public sealed partial class LocalPractice
{
 private static long? FoundingOwner(SqliteConnection c,SqliteTransaction t)
 {
  var value=Scalar(c,t,"SELECT CASE WHEN count(*)=1 THEN max(actor_id) END FROM audit_events WHERE action='Bootstrap' AND entity='Firm' AND entity_id=1");
  return value is long id?id:null;
 }
 private static OwnerApprovalPolicy OwnerPolicy(SqliteConnection c,SqliteTransaction t,long actor)
 {
  var eligible=Convert.ToInt64(Scalar(c,t,"SELECT count(*) FROM owner_approval_eligible WHERE owner_id=$a",("$a",actor)))==1;
  using var q=Command(c,t,"SELECT id,enabled,reason,owner_id FROM owner_approval_events ORDER BY id DESC LIMIT 1");using var r=q.ExecuteReader();
  return r.Read()?new(r.GetInt64(0),eligible&&r.GetInt64(1)==1&&r.GetInt64(3)==actor,eligible,r.GetString(2)):new(null,false,eligible,null);
 }
 public OwnerApprovalPolicy ReadOwnerApprovalPolicy()
 {
  using var c=Open();using var t=c.BeginTransaction();var actor=RequireAdministration(c,t);return OwnerPolicy(c,t,actor);
 }
 public OwnerApprovalPolicy SetOwnerApprovalPolicy(bool enabled,long? expectedEvent,string justification)
 {
  using var c=Open();using var t=c.BeginTransaction(deferred:false);var actor=RequireAdministration(c,t);
  if(FoundingOwner(c,t)!=actor)throw new UnauthorizedAccessException("Verified founding owner required.");
  var current=OwnerPolicy(c,t,actor);
  if(current.EventId!=expectedEvent)throw new ArgumentException("Policy changed. Refresh.");
  if(enabled&&!current.Eligible)throw new UnauthorizedAccessException("Another approval account exists or founding owner is unavailable.");
  var reason=Validation.Text(justification,1000,"Owner policy justification",true);
  Exec(c,t,"INSERT INTO owner_approval_events(owner_id,enabled,reason,actor_id,recorded_utc) VALUES($a,$e,$n,$a,$u)",("$a",actor),("$e",enabled?1:0),("$n",reason),("$u",UtcNow()));
  Audit(c,t,actor,enabled?"OwnerExceptionEnabled":"OwnerExceptionDisabled","Firm",1);var next=OwnerPolicy(c,t,actor);t.Commit();return next;
 }
 public IReadOnlyList<OwnerPolicyEvent> ReadOwnerPolicyHistory()
 {
  using var c=Open();using var t=c.BeginTransaction();RequireAdministration(c,t);
  using var q=Command(c,t,"SELECT e.id,o.username,e.enabled,e.reason,coalesce(a.username,'System'),e.recorded_utc FROM owner_approval_events e JOIN users o ON o.id=e.owner_id LEFT JOIN users a ON a.id=e.actor_id ORDER BY e.id DESC LIMIT 501");
  using var r=q.ExecuteReader();var rows=new List<OwnerPolicyEvent>();while(r.Read()){if(rows.Count==500)throw new ArgumentException("Owner policy history exceeds development limit.");rows.Add(new(r.GetInt64(0),r.GetString(1),r.GetInt64(2)==1?"Enabled":"Disabled / suspended",r.GetString(3),r.GetString(4),r.GetString(5)));}return rows.AsReadOnly();
 }
 public IReadOnlyList<OwnerDecisionEvidence> ReadOwnerDecisionEvidence()
 {
  using var c=Open();using var t=c.BeginTransaction();RequireAdministration(c,t);RequireWorkReview(c,t,"Work.Review");RequireWorkReview(c,t,"Billing.Classify");RequireInvoices(c,t,"Billing.Invoices.View");
  using var q=Command(c,t,@"SELECT x.kind,x.record_id,x.decision,u.username,x.note,x.recorded_utc,x.policy_event FROM (
   SELECT 'Work review' kind,r.submission_id record_id,r.decision decision,r.reviewed_by actor,r.note note,r.reviewed_utc recorded_utc,r.owner_exception_event_id policy_event FROM work_reviews r JOIN work_submissions s ON s.id=r.submission_id WHERE r.reviewed_by=s.submitted_by
   UNION ALL SELECT 'Billing classification',b.submission_id,b.classification,b.classified_by,b.note,b.classified_utc,b.owner_exception_event_id FROM work_billing_classifications b JOIN work_submissions s ON s.id=b.submission_id WHERE b.classified_by=s.submitted_by
   UNION ALL SELECT 'Invoice approval',x.draft_id,x.decision,x.actor_id,x.note,x.decided_utc,x.owner_exception_event_id FROM invoice_draft_decisions x JOIN invoice_drafts d ON d.id=x.draft_id WHERE x.actor_id=d.created_by AND x.decision='Approved'
  ) x JOIN users u ON u.id=x.actor ORDER BY x.recorded_utc DESC,x.kind,x.record_id DESC LIMIT 501");
  using var r=q.ExecuteReader();var rows=new List<OwnerDecisionEvidence>();while(r.Read()){if(rows.Count==500)throw new ArgumentException("Self-decision history exceeds development limit.");long? policy=r.IsDBNull(6)?null:r.GetInt64(6);rows.Add(new(r.GetString(0),r.GetInt64(1),r.GetString(2),r.GetString(3),r.GetString(4),r.GetString(5),policy,policy.HasValue?"Recorded under exception":"Unmarked historical self-decision — review required"));}return rows.AsReadOnly();
 }
 private static long RequireOwnerException(SqliteConnection c,SqliteTransaction t,long actor,bool explicitlyConfirmed)
 {
  if(!explicitlyConfirmed)throw new UnauthorizedAccessException("Self approval requires explicit owner exception confirmation.");
  if(Scalar(c,t,"SELECT id FROM owner_approval_current WHERE owner_id=$a",("$a",actor)) is not long id)throw new UnauthorizedAccessException("Self approval prohibited: owner exception unavailable.");
  return id;
 }
}
