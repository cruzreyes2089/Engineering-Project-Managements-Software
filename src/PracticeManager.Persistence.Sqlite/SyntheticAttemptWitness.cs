using System.Text;
using PracticeManager.Core;
namespace PracticeManager.Persistence;

// Synthetic external attempt witness only. No API, posting permission, replay,
// deletion, reset, or approval wiring. The caller provisions and protects a
// separate retained directory; the filesystem is not an immutable trust anchor.
public sealed record SyntheticWitnessResult(string Outcome)
{
 public bool MaySend=>false;
 public bool MayRetry=>false;
}
public sealed class SyntheticAttemptWitness
{
 private const string ScopeFile="crc-synthetic-witness-scope-v1.txt";
 private readonly string root;
 private readonly Guid store;
 private readonly Guid marker;
 public SyntheticAttemptWitness(string existingDirectory,Guid storeKey,Guid markerKey)
 {
  if(!Path.IsPathFullyQualified(existingDirectory)||storeKey==Guid.Empty||markerKey==Guid.Empty||storeKey==markerKey)
   throw new ArgumentException("Invalid witness scope.");
  root=Path.GetFullPath(existingDirectory);store=storeKey;marker=markerKey;
 }
 private byte[] ScopeBytes()=>Encoding.UTF8.GetBytes("CRC-SYNTHETIC-WITNESS-V1\n"+store.ToString("D")+"\n"+marker.ToString("D")+"\n");
 private static bool Ordinary(string path,bool directory)
 {
  var attributes=File.GetAttributes(path);
  return !attributes.HasFlag(FileAttributes.ReparsePoint)&&attributes.HasFlag(FileAttributes.Directory)==directory;
 }
 private bool RootOrdinary()
 {
  for(DirectoryInfo? current=new(root);current!=null;current=current.Parent)
   if(!Ordinary(current.FullName,true))return false;
  return true;
 }
 private static bool SameFile(string path,byte[] expected)
 {
  if(!Ordinary(path,false))return false;
  using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
  if(stream.Length!=expected.Length)return false;
  var actual=new byte[expected.Length];stream.ReadExactly(actual);
  return actual.AsSpan().SequenceEqual(expected)&&stream.ReadByte()==-1;
 }
 // Explicit test-fixture setup only: never creates the directory, overwrites a
 // scope, or adopts an existing nonempty directory. Not called by recording.
 public SyntheticWitnessResult InitializeEmptyFixture()
 {
  try{
   if(!RootOrdinary())return new("ScopeUnavailable");
   if(Directory.EnumerateFileSystemEntries(root).Any())return new("ScopeNotEmpty");
   using var stream=new FileStream(Path.Combine(root,ScopeFile),FileMode.CreateNew,FileAccess.Write,FileShare.None);
   stream.Write(ScopeBytes());stream.Flush(true);
   return new("InitializedForReview");
  }catch(IOException){return new("ScopeUnavailable");}
   catch(UnauthorizedAccessException){return new("ScopeUnavailable");}
 }
 public SyntheticWitnessResult Record(SyntheticWireIntent intent,Guid attemptKey)
 {
  if(intent.MarkerKey!=marker||attemptKey==Guid.Empty||attemptKey==intent.OperationKey||attemptKey==store||attemptKey==marker)
   return new("InvalidAttemptScope");
  SyntheticWirePayload payload;
  try{payload=SyntheticWireCompiler.Compile(intent);}catch(ArgumentException){return new("InvalidIntent");}
  var name=intent.OperationKey.ToString("N")+".attempt";
  var bytes=Encoding.UTF8.GetBytes(string.Join("\n","CRC-SYNTHETIC-ATTEMPT-V1",store.ToString("D"),marker.ToString("D"),
   intent.OperationKey.ToString("D"),attemptKey.ToString("D"),intent.Kind.ToString(),payload.Reference,payload.Fingerprint,""));
  var path=Path.Combine(root,name);
  try{
   if(!RootOrdinary()||!SameFile(Path.Combine(root,ScopeFile),ScopeBytes()))return new("ScopeMismatch");
   // Exclusive create protects one operation across cooperating processes.
   // If any write/flush fails, retain partial evidence and require review.
   try{
    using var stream=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None);
    stream.Write(bytes);stream.Flush(true);
    return new("RecordedForReview");
   }catch(IOException){
    try{return new(SameFile(path,bytes)?"ExistingAttempt":"AttemptConflictOrUnreadable");}
    catch(IOException){return new("AttemptConflictOrUnreadable");}
    catch(UnauthorizedAccessException){return new("AttemptConflictOrUnreadable");}
   }
  }catch(IOException){return new("ScopeOrAttemptUnavailable");}
   catch(UnauthorizedAccessException){return new("ScopeOrAttemptUnavailable");}
 }
}
