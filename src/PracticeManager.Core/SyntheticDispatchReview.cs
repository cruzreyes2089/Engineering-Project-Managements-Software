namespace PracticeManager.Core;

// Pure classification of simulated transport observations. Deliberately has no
// HTTP client, dispatch callback, storage, retries, or real posting permission.
public sealed record SyntheticDispatchObservation(bool AttemptDurablyRecorded,bool SendEntered,int? StatusCode,
 bool CompleteResponse,Guid? ReturnedExternalKey,bool IndependentReadbackComplete,SyntheticRecoveryAssessment? Recovery)
{
 public override string ToString()=>"[Simulated dispatch observation]";
}
public enum SyntheticDispatchState { BlockedBeforeSend,Uncertain,ConfirmationForReview }
public sealed record SyntheticDispatchAssessment(SyntheticDispatchState State,string Reason)
{
 public bool MayAutomaticallyRetry=>false;
 public bool MayMarkPosted=>false;
}
public static class SyntheticDispatchReview
{
 public static SyntheticDispatchAssessment Evaluate(SyntheticDispatchObservation e)
 {
  static SyntheticDispatchAssessment Uncertain(string reason)=>new(SyntheticDispatchState.Uncertain,reason);
  // Even a purported before-send failure is not authority to recreate an
  // operation or release a durable attempt. Those decisions belong elsewhere.
  if(!e.SendEntered){
   if(e.StatusCode!=null||e.CompleteResponse||e.ReturnedExternalKey!=null||e.IndependentReadbackComplete||e.Recovery!=null)
    return Uncertain("InconsistentObservation");
   return new(SyntheticDispatchState.BlockedBeforeSend,e.AttemptDurablyRecorded?"DurableAttemptRequiresReview":"NoSendObserved");
  }
  if(!e.AttemptDurablyRecorded)return Uncertain("MissingDurableAttempt");
  if(!e.CompleteResponse||e.StatusCode==null)return Uncertain("IncompleteResponse");
  // Do not assume even a 4xx proves no commit; provider transaction semantics
  // and idempotency remain unverified. 201 is necessary but not sufficient.
  if(e.StatusCode!=201)return Uncertain("UnexpectedStatus");
  if(e.ReturnedExternalKey==Guid.Empty)return Uncertain("InvalidResponseIdentity");
  if(!e.IndependentReadbackComplete||e.Recovery?.Finding!=SyntheticRecoveryFinding.MatchedForReview)
   return Uncertain("IndependentReadbackRequired");
  return new(SyntheticDispatchState.ConfirmationForReview,"ManualConfirmationRequired");
 }
}
