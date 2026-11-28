using PracticeManager.Core;
namespace PracticeManager.Persistence;

// One consistent local read transaction; no provider request, credential
// disclosure, financial ledger totals, mutation or posting authority.
public sealed record AccountingSafetyOverview(PostingRehearsalStatus Rehearsals,WireDraftStatus Drafts,WireAttemptStatus Attempts,
 bool ProfilePresent,bool MarkerPresent,string Status)
{
 public int Unresolved=>Rehearsals.InFlight+Rehearsals.Uncertain+Attempts.Reserved+Attempts.Witnessed+Attempts.Uncertain+Attempts.ConfirmationForReview;
 public bool MayPost=>false;
 public bool MayRetry=>false;
 public bool MayReleaseHold=>false;
}
public sealed partial class LocalPractice
{
 public AccountingSafetyOverview ReadAccountingSafetyOverview()
 {
  using var c=Open();using var t=c.BeginTransaction();
  RequireWireAttempt(c,t);Require(c,t,"Reports.Financial");
  var actor=RequireRehearsal(c,t);
  var hold=Scalar(c,t,"SELECT reason FROM accounting_lifecycle_hold WHERE id=1") as string;
  int R(string state)=>Convert.ToInt32(Scalar(c,t,"SELECT count(*) FROM accounting_posting_rehearsals WHERE state=$s",("$s",state)));
  int D(string state)=>Convert.ToInt32(Scalar(c,t,"SELECT count(*) FROM accounting_wire_drafts WHERE state=$s",("$s",state)));
  int A(string state)=>Convert.ToInt32(Scalar(c,t,"SELECT count(*) FROM accounting_wire_attempts WHERE state=$s",("$s",state)));
  var r=new PostingRehearsalStatus(hold,R("Reserved"),R("InFlight"),R("Uncertain"),R("SimulatedConfirmed"),R("Cancelled"));
  var d=new WireDraftStatus(D("Draft"),D("Approved"),D("Cancelled"));
  var a=new WireAttemptStatus(A("Reserved"),A("Witnessed"),A("Uncertain"),A("ConfirmationForReview"),A("ReviewedSimulation"));
  var profile=ReadProfile(c,t,actor)!=null;var marker=ReadMarker(c,t)!=null;
  var status=r.Held?"LifecycleHold":r.InFlight+r.Uncertain+a.Reserved+a.Witnessed+a.Uncertain+a.ConfirmationForReview>0?
   "UnresolvedSimulation":!profile||!marker?"FixtureNotConfigured":"OfflineReviewOnly";
  return new(r,d,a,profile,marker,status);
 }
}
