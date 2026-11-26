using Microsoft.Data.Sqlite;
namespace PracticeManager.Persistence;
public sealed record OwnerApprovalPolicy(long? EventId,bool Enabled,bool Eligible,string? Reason);
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
 private static long RequireOwnerException(SqliteConnection c,SqliteTransaction t,long actor,bool explicitlyConfirmed)
 {
  if(!explicitlyConfirmed)throw new UnauthorizedAccessException("Self approval requires explicit owner exception confirmation.");
  if(Scalar(c,t,"SELECT id FROM owner_approval_current WHERE owner_id=$a",("$a",actor)) is not long id)throw new UnauthorizedAccessException("Self approval prohibited: owner exception unavailable.");
  return id;
 }
}
