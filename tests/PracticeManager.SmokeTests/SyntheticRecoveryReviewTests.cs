using PracticeManager.Core;
internal static class SyntheticRecoveryReviewTests
{
 public static void Run(Action<bool,string> check)
 {
  var op=Guid.NewGuid();var marker=Guid.NewGuid();var key=Guid.NewGuid();
  var intent=new SyntheticPostingIntent(op,RehearsalKind.ExpensePayment,Guid.Empty,Guid.NewGuid(),null,20m,new DateOnly(2026,11,25),"USD",Guid.NewGuid(),"Synthetic vendor");
  var target=new SyntheticRecoveryTarget(op,marker,intent.Kind,SyntheticRecoveryReview.ReferenceFor(op),intent.Fingerprint());
  var match=new SyntheticRecoveryCandidate(key,target.Kind,target.Reference,target.Fingerprint);
  var other=match with{ExternalKey=Guid.NewGuid(),DetailReference="Unrelated synthetic reference"};
  SyntheticRecoverySweep Sweep(params SyntheticRecoveryCandidate[] rows)=>new(marker,true,true,rows);
  var sweep=Sweep(match,other);
  SyntheticRecoveryAssessment Eval(SyntheticRecoverySweep? a=null,SyntheticRecoverySweep? b=null,SyntheticRecoveryTarget? t=null)=>SyntheticRecoveryReview.Evaluate(t??target,a??sweep,b??sweep);
  void Finding(SyntheticRecoveryAssessment actual,SyntheticRecoveryFinding expected,string label)=>check(actual.Finding==expected&&!actual.MayRetry&&!actual.MayReleaseHold,label);
  Finding(Eval(),SyntheticRecoveryFinding.MatchedForReview,"recovery exact detail match never releases hold");
  Finding(Eval(b:Sweep(other,match)),SyntheticRecoveryFinding.MatchedForReview,"recovery stable records may be reordered");
  Finding(Eval(Sweep(),Sweep()),SyntheticRecoveryFinding.NoMatchObserved,"recovery absence never authorizes retry");
  Finding(Eval(Sweep(other),Sweep(other),target with{KnownExternalKey=key}),SyntheticRecoveryFinding.NoMatchObserved,"missing known key cannot authorize retry");
  Finding(Eval(sweep,sweep with{Complete=false}),SyntheticRecoveryFinding.ReviewRequired,"recovery incomplete pagination blocks");
  Finding(Eval(sweep,sweep with{IndependentEvidenceVerified=false}),SyntheticRecoveryFinding.ReviewRequired,"recovery independent evidence required");
  Finding(Eval(sweep,sweep with{MarkerKey=Guid.NewGuid()}),SyntheticRecoveryFinding.Conflict,"recovery marker mismatch blocks");
  Finding(Eval(sweep,Sweep(other)),SyntheticRecoveryFinding.ReviewRequired,"recovery changing sweep blocks");
  Finding(Eval(Sweep(match,match),Sweep(match,match)),SyntheticRecoveryFinding.ReviewRequired,"recovery duplicate external key blocks");
  var duplicate=match with{ExternalKey=Guid.NewGuid()};
  Finding(Eval(Sweep(match,duplicate),Sweep(match,duplicate)),SyntheticRecoveryFinding.Conflict,"recovery duplicate reference blocks");
  var cross=duplicate with{Kind=RehearsalKind.Receipt};
  Finding(Eval(Sweep(match,cross),Sweep(match,cross)),SyntheticRecoveryFinding.Conflict,"recovery cross-kind collision blocks");
  var wrong=match with{Fingerprint=(intent with{Amount=21m}).Fingerprint()};
  Finding(Eval(Sweep(wrong),Sweep(wrong)),SyntheticRecoveryFinding.Conflict,"recovery amount drift blocks");
  wrong=match with{Kind=RehearsalKind.Invoice};
  Finding(Eval(Sweep(wrong),Sweep(wrong)),SyntheticRecoveryFinding.Conflict,"recovery mismatched kind blocks");
  Finding(Eval(t:target with{KnownExternalKey=Guid.NewGuid()}),SyntheticRecoveryFinding.Conflict,"recovery known GUID mismatch blocks");
  Finding(Eval(t:target with{KnownExternalKey=key}),SyntheticRecoveryFinding.MatchedForReview,"recovery known GUID matches");
  wrong=match with{DetailReference="Changed reference"};
  Finding(Eval(Sweep(wrong),Sweep(wrong),target with{KnownExternalKey=key}),SyntheticRecoveryFinding.Conflict,"recovery changed known external reference blocks");
  wrong=match with{DetailReference=null};
  Finding(Eval(Sweep(wrong),Sweep(wrong)),SyntheticRecoveryFinding.ReviewRequired,"recovery missing detail reference cannot be treated as absence");
  wrong=match with{DetailReference=target.Reference.ToLowerInvariant()};
  Finding(Eval(Sweep(wrong),Sweep(wrong)),SyntheticRecoveryFinding.Conflict,"recovery case collision not silently accepted");
  Finding(Eval(t:target with{Reference="New reference"}),SyntheticRecoveryFinding.ReviewRequired,"recovery stable source correlation required");
  Finding(Eval(t:target with{Fingerprint="invalid"}),SyntheticRecoveryFinding.ReviewRequired,"recovery invalid target fingerprint blocked");
  check(!target.ToString().Contains(target.Reference)&&!match.ToString().Contains(key.ToString()),"recovery evidence display redacts identity");
 }
}
