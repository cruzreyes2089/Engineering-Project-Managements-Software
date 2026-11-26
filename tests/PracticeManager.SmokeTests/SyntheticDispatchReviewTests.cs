using PracticeManager.Core;
internal static class SyntheticDispatchReviewTests
{
 public static void Run(Action<bool,string> check)
 {
  var match=new SyntheticRecoveryAssessment(SyntheticRecoveryFinding.MatchedForReview,"Synthetic");
  var good=new SyntheticDispatchObservation(true,true,201,true,null,true,match);
  void Expect(SyntheticDispatchObservation observation,SyntheticDispatchState state,string label)
  {
   var r=SyntheticDispatchReview.Evaluate(observation);
   check(r.State==state&&!r.MayAutomaticallyRetry&&!r.MayMarkPosted,label);
  }
  Expect(good,SyntheticDispatchState.ConfirmationForReview,"201 without response GUID can reach review only after independent match");
  Expect(good with{ReturnedExternalKey=Guid.NewGuid()},SyntheticDispatchState.ConfirmationForReview,"201 with response GUID still review only");
  Expect(good with{ReturnedExternalKey=Guid.Empty},SyntheticDispatchState.Uncertain,"empty response GUID uncertain");
  Expect(good with{AttemptDurablyRecorded=false},SyntheticDispatchState.Uncertain,"send without durable attempt uncertain");
  Expect(good with{StatusCode=null,CompleteResponse=false,IndependentReadbackComplete=false,Recovery=null},SyntheticDispatchState.Uncertain,"timeout after possible commit uncertain");
  Expect(good with{CompleteResponse=false},SyntheticDispatchState.Uncertain,"truncated 201 uncertain");
  foreach(var status in new[]{200,202,204,301,400,401,403,404,409,422,429,500,503})
   Expect(good with{StatusCode=status},SyntheticDispatchState.Uncertain,"unexpected status "+status+" never authorizes retry");
  Expect(good with{IndependentReadbackComplete=false},SyntheticDispatchState.Uncertain,"201 without complete readback uncertain");
  foreach(var finding in new[]{SyntheticRecoveryFinding.Conflict,SyntheticRecoveryFinding.NoMatchObserved,SyntheticRecoveryFinding.ReviewRequired})
   Expect(good with{Recovery=new(finding,"Synthetic")},SyntheticDispatchState.Uncertain,"201 with "+finding+" uncertain");
  Expect(new(false,false,null,false,null,false,null),SyntheticDispatchState.BlockedBeforeSend,"no send observed does not authorize automatic retry");
  Expect(new(true,false,null,false,null,false,null),SyntheticDispatchState.BlockedBeforeSend,"durable attempt without send stays review only");
  Expect(good with{SendEntered=false},SyntheticDispatchState.Uncertain,"contradictory send evidence uncertain");
 }
}
