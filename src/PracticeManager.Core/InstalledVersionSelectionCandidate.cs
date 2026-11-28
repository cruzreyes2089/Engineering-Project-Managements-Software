using System.Text;
namespace PracticeManager.Core;

// Program selection only. No execution, schema authorization, signature
// verification, automatic rollback, installer registration or database access.
// Caller must independently authorize exact release and upgrade compatibility.
public static class InstalledVersionSelectionCandidate
{
 const string ActiveName="active-version.txt";
 const string IntentName="activation-pending.txt";
 static bool Exists(string path)
 {
  try{_=File.GetAttributes(path);return true;}
  catch(FileNotFoundException){return false;}
  catch(DirectoryNotFoundException){return false;}
 }
 static string Read(InstalledProgramBoundary boundary,string name)
 {
  var path=LocalStoragePath.FilePath(Path.Combine(boundary.ProgramRoot,name),mustExist:true,rejectSidecars:true);
  if(!WorkspaceConfidentiality.HasWorkspaceFileAcl(path))throw new ArgumentException("Program selection permissions require review.");
  using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
  if(stream.Length!=64)throw new ArgumentException("Program selection record invalid.");
  var bytes=new byte[64];stream.ReadExactly(bytes);
  var digest=Encoding.ASCII.GetString(bytes);
  _=boundary.VersionDirectory(digest);
  return digest;
 }
 static void WriteNew(InstalledProgramBoundary boundary,string name,string value)
 {
  var path=LocalStoragePath.FilePath(Path.Combine(boundary.ProgramRoot,name),mustExist:false,mustBeNew:true,rejectSidecars:true);
  using(var stream=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None))
  {stream.Write(Encoding.ASCII.GetBytes(value));stream.Flush(true);}
  if(!WorkspaceConfidentiality.HasWorkspaceFileAcl(path))throw new ArgumentException("Program selection permissions require review.");
 }
 static void NoPending(InstalledProgramBoundary boundary)
 {
  if(Exists(Path.Combine(boundary.ProgramRoot,IntentName)))
   throw new ArgumentException("Interrupted program selection requires review. No automatic rollback.");
 }
 static string? Current(InstalledProgramBoundary boundary)
  =>Exists(Path.Combine(boundary.ProgramRoot,ActiveName))?Read(boundary,ActiveName):null;
 public static string? ReviewCurrent(InstalledProgramBoundary boundary)
 {
  using var lease=boundary.AcquireExistingLease();
  NoPending(boundary);
  var current=Current(boundary);
  if(current!=null)InstalledPayloadVerification.Verify(boundary,current);
  return current;
 }
 public static void SelectReviewedVersion(InstalledProgramBoundary boundary,string? expectedPrevious,string candidate)
 {
  using var lease=boundary.AcquireExistingLease();
  NoPending(boundary);
  var previous=Current(boundary);
  if(previous!=expectedPrevious||candidate==previous)throw new ArgumentException("Program selection changed or is already selected.");
  if(previous!=null)InstalledPayloadVerification.Verify(boundary,previous);
  InstalledPayloadVerification.Verify(boundary,candidate);
  // Fixed pending marker survives a failed or interrupted switch. A second
  // invocation refuses to guess whether the previous switch completed.
  WriteNew(boundary,IntentName,candidate);
  var stagedName="selection-"+Guid.NewGuid().ToString("N")+".txt";
  WriteNew(boundary,stagedName,candidate);
  var active=Path.Combine(boundary.ProgramRoot,ActiveName);
  var staged=Path.Combine(boundary.ProgramRoot,stagedName);
  if(Current(boundary)!=previous||Read(boundary,IntentName)!=candidate)
   throw new ArgumentException("Program selection changed during review.");
  InstalledPayloadVerification.Verify(boundary,candidate);
  if(previous==null)File.Move(staged,active,false);
  else
  {
   var retained=LocalStoragePath.FilePath(Path.Combine(boundary.ProgramRoot,"previous-"+Guid.NewGuid().ToString("N")+".txt"),mustExist:false,mustBeNew:true,rejectSidecars:true);
   File.Replace(staged,active,retained,false);
   if(Read(boundary,Path.GetFileName(retained))!=previous)throw new InvalidOperationException("Prior selection requires review.");
  }
  if(Read(boundary,ActiveName)!=candidate||Read(boundary,IntentName)!=candidate)
   throw new InvalidOperationException("Program selection requires review.");
  InstalledPayloadVerification.Verify(boundary,candidate);
  // Remove only the exact owned pending record after observed success. A
  // crash before this deletion leaves review required, never auto-reversed.
  File.Delete(Path.Combine(boundary.ProgramRoot,IntentName));
 }
}
