using PracticeManager.Core;
using PracticeManager.Persistence;
internal static class SyntheticAttemptWitnessTests
{
 public static async Task Run(string directory,Action<bool,string> check)
 {
  var root=Path.Combine(directory,"synthetic-witness");Directory.CreateDirectory(root);
  var store=Guid.NewGuid();var marker=Guid.NewGuid();var attempt=Guid.NewGuid();
  var intent=new SyntheticWireIntent(Guid.NewGuid(),marker,RehearsalKind.Receipt,Guid.NewGuid(),50m,new DateOnly(2026,11,25),"USD",Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid());
  var witness=new SyntheticAttemptWitness(root,store,marker);
  check(witness.Record(intent,attempt).Outcome!="RecordedForReview"&&!Directory.EnumerateFileSystemEntries(root).Any(),"witness never auto-initializes missing scope");
  check(witness.InitializeEmptyFixture().Outcome=="InitializedForReview","explicit empty synthetic witness scope initialized");
  check(witness.InitializeEmptyFixture().Outcome=="ScopeNotEmpty","witness cannot overwrite scope");
  var peer=new SyntheticAttemptWitness(root,store,marker);
  var results=await Task.WhenAll(Task.Run(()=>witness.Record(intent,attempt)),Task.Run(()=>peer.Record(intent,attempt)));
  check(results.Count(x=>x.Outcome=="RecordedForReview")==1&&results.All(x=>!x.MaySend&&!x.MayRetry),"exclusive witness create has one winner and grants no authority");
  check(peer.Record(intent,attempt).Outcome=="ExistingAttempt","witness restart retains attempted operation");
  check(peer.Record(intent,Guid.NewGuid()).Outcome=="AttemptConflictOrUnreadable","new attempt key cannot reuse operation");
  check(peer.Record(intent with{CashAccountKey=Guid.NewGuid()},attempt).Outcome=="AttemptConflictOrUnreadable","witness binds receipt bank in full wire fingerprint");
  check(peer.Record(intent with{Amount=51m},attempt).Outcome=="AttemptConflictOrUnreadable","witness binds amount");
  check(peer.Record(intent with{MarkerKey=Guid.NewGuid()},attempt).Outcome=="InvalidAttemptScope","witness rejects changed marker");
  check(new SyntheticAttemptWitness(root,Guid.NewGuid(),marker).Record(intent,attempt).Outcome=="ScopeMismatch","wrong witness store rejected");
  var path=Path.Combine(root,intent.OperationKey.ToString("N")+".attempt");
  var before=File.ReadAllBytes(path);
  check(peer.Record(intent,attempt).Outcome=="ExistingAttempt"&&before.SequenceEqual(File.ReadAllBytes(path)),"repeat check never alters witness bytes");
  // Synthetic interrupted write fixture only; implementation never truncates.
  var partial=intent with{OperationKey=Guid.NewGuid()};
  var partialPath=Path.Combine(root,partial.OperationKey.ToString("N")+".attempt");File.WriteAllText(partialPath,"partial");
  check(peer.Record(partial,Guid.NewGuid()).Outcome=="AttemptConflictOrUnreadable"&&File.ReadAllText(partialPath)=="partial","partial attempt retained and blocks replay");
  // Simulate loss of the CRC database while retained witness survives; new
  // client instance must still see original operation. No real DB touched.
  var afterRestore=new SyntheticAttemptWitness(root,store,marker);
  check(afterRestore.Record(intent,Guid.NewGuid()).Outcome=="AttemptConflictOrUnreadable","retained witness blocks regenerated attempt after local restore");
  var missing=Path.Combine(directory,"missing-witness");
  check(new SyntheticAttemptWitness(missing,store,marker).InitializeEmptyFixture().Outcome=="ScopeUnavailable"&&!Directory.Exists(missing),"missing witness directory never recreated");
  var nonempty=Path.Combine(directory,"nonempty-witness");Directory.CreateDirectory(nonempty);File.WriteAllText(Path.Combine(nonempty,"prior.txt"),"synthetic");
  check(new SyntheticAttemptWitness(nonempty,store,marker).InitializeEmptyFixture().Outcome=="ScopeNotEmpty","existing unrelated directory never enrolled");
  check(!System.Text.Encoding.UTF8.GetString(before).Contains("SalesUnitPrice")&&!System.Text.Encoding.UTF8.GetString(before).Contains(intent.CustomerKey!.Value.ToString("D")),"witness excludes raw payload and customer identity");
 }
}
