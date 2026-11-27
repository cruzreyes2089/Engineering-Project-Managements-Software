using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PracticeManager.Core;
using PracticeManager.Persistence;

// Exercises actual SQLite backup/isolated restore and independently retained
// witness files. All records are synthetic; only the existing loopback GET
// fixture is used for setup. No external provider is contacted.
internal static class SyntheticAccountingRestoreTests
{
 public static async Task Run(string directory,string pw,Action<bool,string> check)
 {
  var root=Path.Combine(directory,"accounting-restore-"+Guid.NewGuid().ToString("N"));
  Directory.CreateDirectory(root);
  var path=Path.Combine(root,"source.db");
  var s=new LocalPractice(path);s.Bootstrap("Synthetic accounting recovery firm","admin",pw,"CL");
  check(s.Login("admin",pw),"accounting restore source authenticated");
  var marker=Guid.NewGuid();var customer=Guid.NewGuid();
  string Reply(string req)=>req.StartsWith("GET /api2/text-custom-fields?")?
   JsonSerializer.Serialize(new{business=new{name="CRC ACCOUNTING TEST"},skip=0,pageSize=2,totalRecords=1,textCustomFields=new[]{new{key=marker,name=BusinessMarker.TestName}}}):
   JsonSerializer.Serialize(new{business=new{name="CRC ACCOUNTING TEST"},skip=0,pageSize=20,totalRecords=1,customers=new[]{new{key=customer,name="CRC Test Customer 001",status="Paid"}}});
  await AccountingProbeTests.WithServer(200,"{}",async(url,_)=>{
   check((await s.SaveAccountingProfileAsync(url,"synthetic-key")).Success,"recovery test profile saved");
   check((await s.EnrollOrVerifyMarkerAsync(true)).Success,"recovery test marker enrolled");
   check((await s.SyncCustomerDirectoryAsync()).Outcome=="Synchronized","recovery test directory synchronized");
  },6,responseFor:Reply);
  var retained=Directory.CreateDirectory(Path.Combine(root,"retained-witness")).FullName;
  var store=Guid.NewGuid();var witness=new SyntheticAttemptWitness(retained,store,marker);
  check(witness.InitializeEmptyFixture().Outcome=="InitializedForReview","independent recovery witness initialized");
  var intent=new SyntheticWireIntent(Guid.NewGuid(),marker,RehearsalKind.Receipt,Guid.NewGuid(),50m,new DateOnly(2026,11,25),"USD",customer,Guid.NewGuid(),Guid.NewGuid());
  var wire=SyntheticWireCompiler.Compile(intent);
  check(s.PrepareSyntheticWireDraft(intent).Outcome=="Prepared"&&s.ApproveSyntheticWireDraft(intent.OperationKey,wire.Fingerprint).Outcome=="Approved",
   "recovery exact synthetic draft approved");
  var before=s.CreateRecoveryCopy(Path.Combine(root,"before-attempt.db"));
  var reservation=s.ReserveSyntheticWireAttempt(intent.OperationKey,wire.Fingerprint);
  var attempt=reservation.AttemptKey??throw new Exception("Missing synthetic attempt.");
  check(s.WitnessSyntheticWireAttempt(intent.OperationKey,attempt,witness).State=="Witnessed","recovery intent witnessed before effect");
  // Fake evidence of a commit whose acknowledgement was lost, never a provider call.
  var external=Guid.NewGuid();
  var sweep=new SyntheticRecoverySweep(marker,true,true,new[]{new SyntheticRecoveryCandidate(external,intent.Kind,wire.Reference,wire.Fingerprint)});
  var uncertain=s.AssessSyntheticWireAttempt(intent.OperationKey,attempt,new(true,true,null,false,null,false,null),sweep,sweep);
  check(uncertain.State=="Uncertain"&&!uncertain.MaySend&&!uncertain.MayRetry,"lost acknowledgement remains uncertain with matching synthetic evidence");
  var after=s.CreateRecoveryCopy(Path.Combine(root,"after-attempt.db"));
  string Hash(string file)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))).ToLowerInvariant();
  var witnessPath=Path.Combine(retained,intent.OperationKey.ToString("N")+".attempt");var retainedHash=Hash(witnessPath);
  long Count(string dbPath,string query)
  {
   using var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=dbPath,Mode=SqliteOpenMode.ReadOnly,Pooling=false}.ToString());
   db.Open();using var q=db.CreateCommand();q.CommandText=query;return Convert.ToInt64(q.ExecuteScalar());
  }
  LocalPractice Restore(RecoveryCopyResult backup,string name)
  {
   var restored=s.RestoreRecoveryCopyToNewFile(backup.Path,backup.Sha256,Path.Combine(root,name));
   var copy=new LocalPractice(restored.Path);check(copy.Login("admin",pw),"isolated accounting restore authenticated");
   check(Count(restored.Path,"SELECT count(*) FROM accounting_profiles")==0&&
    Count(restored.Path,"SELECT count(*) FROM accounting_lifecycle_hold WHERE id=1")==1,
    "restored accounting credentials absent and sticky hold present");
   return copy;
  }
  var old=Restore(before,"restored-before.db");
  check(old.ReadSyntheticWireAttempt(intent.OperationKey).Outcome=="NotAttempted","older copy lacks later attempt without proving nonposting");
  var orphan=old.CompareSyntheticAttemptWitness(retained,store);
  check(orphan.Outcome=="WitnessReviewRequired"&&orphan.Expected==0&&orphan.Orphaned==1&&!orphan.MayRetry&&!orphan.MayReleaseHold,
   "external witness detects missing local attempt after older restore");
  check(old.ReserveSyntheticWireAttempt(intent.OperationKey,wire.Fingerprint).Outcome=="LifecycleHold","older restore cannot reserve consumed operation");
  check(old.WitnessSyntheticWireAttempt(intent.OperationKey,attempt,witness).Outcome=="LifecycleHold","older restore cannot resume original attempt");
  check(old.ReviewSyntheticWireAttempt(intent.OperationKey,wire.Fingerprint).Outcome=="LifecycleHold","older restore cannot review away hold");
  var replacement=intent with{OperationKey=Guid.NewGuid()};
  check(old.PrepareSyntheticWireDraft(replacement).Outcome=="LifecycleHold","older restore cannot prepare replacement transaction");
  var recent=Restore(after,"restored-after.db");
  check(recent.ReadSyntheticWireAttempt(intent.OperationKey).State=="Uncertain","recent restore retains uncertainty");
  var comparison=recent.CompareSyntheticAttemptWitness(retained,store);
  check(comparison.Outcome=="WitnessReviewRequired"&&comparison.Exact==1&&comparison.Unresolved==1&&!comparison.MayReleaseHold,
   "matching retained witness cannot clear uncertainty or release hold");
  check(recent.ReserveSyntheticWireAttempt(intent.OperationKey,wire.Fingerprint).Outcome=="LifecycleHold"&&
   recent.AssessSyntheticWireAttempt(intent.OperationKey,attempt,new(true,true,201,true,external,true,null),sweep,sweep).Outcome=="LifecycleHold",
   "restored uncertain transaction cannot be reassessed into success");
  check(recent.ReadSyntheticWireHistory(unresolvedOnly:true).Items.Single().AssessmentEvidence=="Uncertain","restored assessment history preserves original lost acknowledgement");
  check(witness.Record(intent,Guid.NewGuid()).Outcome=="AttemptConflictOrUnreadable","independent witness refuses regenerated attempt key");
  check(Hash(witnessPath)==retainedHash&&Hash(before.Path)==before.Sha256&&Hash(after.Path)==after.Sha256,"restore leaves original backups and witness bytes unchanged");
  check(Count(path,"SELECT count(*) FROM accounting_profiles")==1&&Count(path,"SELECT count(*) FROM accounting_lifecycle_hold")==0&&
   s.ReadSyntheticWireAttempt(intent.OperationKey).State=="Uncertain","isolated recovery never alters source accounting state");
  old.Logout();recent.Logout();s.Logout();
 }
}
