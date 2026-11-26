using System.Text.Json;
using PracticeManager.Core;
using PracticeManager.Persistence;
internal static class SyntheticWireAttemptTests
{
 public static async Task Run(string directory,string pw,Action<bool,string> check)
 {
  var path=Path.Combine(directory,"wire-attempt.db");var s=new LocalPractice(path);s.Bootstrap("Synthetic Attempt Firm","admin",pw,"CL");check(s.Login("admin",pw),"attempt fixture authenticated");
  var marker=Guid.NewGuid();var customer=Guid.NewGuid();var bank=Guid.NewGuid();
  string Reply(string req)=>req.StartsWith("GET /api2/text-custom-fields?")?JsonSerializer.Serialize(new{business=new{name="CRC ACCOUNTING TEST"},skip=0,pageSize=2,totalRecords=1,textCustomFields=new[]{new{key=marker,name=BusinessMarker.TestName}}}):JsonSerializer.Serialize(new{business=new{name="CRC ACCOUNTING TEST"},skip=0,pageSize=20,totalRecords=1,customers=new[]{new{key=customer,name="CRC Test Customer 001",status="Paid"}}});
  await AccountingProbeTests.WithServer(200,"{}",async(url,_)=>{
   check((await s.SaveAccountingProfileAsync(url,"synthetic-key")).Success,"attempt profile saved");
   check((await s.EnrollOrVerifyMarkerAsync(true)).Success,"attempt marker enrolled");
   check((await s.SyncCustomerDirectoryAsync()).Outcome=="Synchronized","attempt directory synchronized");
  },6,responseFor:Reply);
  var intent=new SyntheticWireIntent(Guid.NewGuid(),marker,RehearsalKind.Receipt,Guid.NewGuid(),50m,new DateOnly(2026,11,25),"USD",customer,bank,Guid.NewGuid());
  var hash=SyntheticWireCompiler.Compile(intent).Fingerprint;
  check(s.PrepareSyntheticWireDraft(intent).Outcome=="Prepared"&&s.ReserveSyntheticWireAttempt(intent.OperationKey,hash).Outcome=="ApprovalRequired","unapproved attempt blocked");
  check(s.ApproveSyntheticWireDraft(intent.OperationKey,hash).Outcome=="Approved","attempt exact approval");
  var peer=new LocalPractice(path);check(peer.Login("admin",pw),"attempt peer authenticated");
  var attempts=await Task.WhenAll(Task.Run(()=>s.ReserveSyntheticWireAttempt(intent.OperationKey,hash)),Task.Run(()=>peer.ReserveSyntheticWireAttempt(intent.OperationKey,hash)));
  check(attempts.Count(x=>x.Outcome=="ReservedForSimulation")==1&&attempts.Count(x=>x.Outcome=="AttemptAlreadyRecorded")==1,"one durable attempt under concurrent reservation");
  var key=attempts.Single(x=>x.AttemptKey.HasValue).AttemptKey!.Value;
  check(s.ReadSyntheticWireAttempt(intent.OperationKey).AttemptKey==null&&!s.ReadSyntheticWireDraft(intent.OperationKey).ApprovalCurrentlyValid,"attempt read does not reissue capability");
  check(s.CancelSyntheticWireDraft(intent.OperationKey,hash).Outcome=="AttemptAlreadyRecorded","attempted draft cannot cancel");
  var root=Path.Combine(directory,"wire-attempt-witness");Directory.CreateDirectory(root);var store=Guid.NewGuid();var witness=new SyntheticAttemptWitness(root,store,marker);
  check(witness.InitializeEmptyFixture().Outcome=="InitializedForReview","attempt witness initialized");
  check(s.WitnessSyntheticWireAttempt(intent.OperationKey,Guid.NewGuid(),witness).Outcome=="AttemptNotAvailable","wrong attempt capability blocked");
  check(s.WitnessSyntheticWireAttempt(intent.OperationKey,key,witness).State=="Witnessed","new witness durable transition");
  check(peer.WitnessSyntheticWireAttempt(intent.OperationKey,key,witness).Outcome=="AttemptNotAvailable","witness transition cannot repeat");
  var match=new SyntheticRecoveryAssessment(SyntheticRecoveryFinding.MatchedForReview,"Synthetic");var external=Guid.NewGuid();
  var observed=new SyntheticDispatchObservation(true,true,201,true,external,true,match,external);
  check(s.AssessSyntheticWireAttempt(intent.OperationKey,key,observed).State=="ConfirmationForReview","matching simulation awaits explicit review");
  check(s.ReviewSyntheticWireAttempt(intent.OperationKey,"wrong").Outcome=="ApprovalOrPayloadInvalid","review requires exact fingerprint");
  check(s.ReviewSyntheticWireAttempt(intent.OperationKey,hash).State=="ReviewedSimulation","explicit offline review only");
  check(s.ReserveSyntheticWireAttempt(intent.OperationKey,hash).Outcome=="AttemptAlreadyRecorded","reviewed simulation permanently consumes key");
  var other=intent with{OperationKey=Guid.NewGuid()};var otherHash=SyntheticWireCompiler.Compile(other).Fingerprint;
  check(s.PrepareSyntheticWireDraft(other).Outcome=="Prepared"&&s.ApproveSyntheticWireDraft(other.OperationKey,otherHash).Outcome=="Approved","second draft approved");
  var second=s.ReserveSyntheticWireAttempt(other.OperationKey,otherHash);
  check(second.AttemptKey.HasValue,"next offline simulation reserved");
  check(witness.Record(other,second.AttemptKey!.Value).Outcome=="RecordedForReview","interrupted witness fixture");
  var reopened=new LocalPractice(path);check(reopened.Login("admin",pw)&&reopened.ReadSyntheticWireAttempt(other.OperationKey).State=="Reserved","reservation retained across restart");
  check(reopened.WitnessSyntheticWireAttempt(other.OperationKey,second.AttemptKey.Value,witness).State=="Uncertain","existing witness never resumes attempt");
  check(reopened.ReviewSyntheticWireAttempt(other.OperationKey,otherHash).Outcome=="ManualRecoveryRequired","uncertainty cannot be cleared by simulation review");
  var third=intent with{OperationKey=Guid.NewGuid()};var thirdHash=SyntheticWireCompiler.Compile(third).Fingerprint;
  check(s.PrepareSyntheticWireDraft(third).Outcome=="Prepared"&&s.ApproveSyntheticWireDraft(third.OperationKey,thirdHash).Outcome=="Approved"&&s.ReserveSyntheticWireAttempt(third.OperationKey,thirdHash).Outcome=="UnresolvedRehearsal","uncertainty blocks subsequent dispatch reservations");
  check(s.DeclareAccountingLifecycleHold(LifecycleHoldReason.Restore).Outcome=="LifecycleHold"&&s.ReadSyntheticWireAttempt(other.OperationKey).State=="Uncertain","hold preserves uncertain attempt history");
 }
}
