using PracticeManager.Core;
namespace PracticeManager.Persistence;

// Application-owned, same-volume staging in the requested destination parent.
// No recursive deletion or alteration of existing destination/source ACLs.
internal sealed class RecoveryStaging:IDisposable
{
 readonly string directory;
 readonly string target;
 public string FilePath{get;}
 public RecoveryStaging(string destination)
 {
  target=LocalStoragePath.FilePath(destination,mustExist:false,mustBeNew:true,rejectSidecars:true);
  directory=Path.Combine(Path.GetDirectoryName(target)!,".crc-recovery-stage-"+Guid.NewGuid().ToString("N"));
  if(Directory.Exists(directory)||File.Exists(directory))throw new InvalidOperationException("Recovery staging collision.");
  WorkspaceConfidentiality.EnsureWorkspaceDirectory(directory);
  FilePath=Path.Combine(directory,"copy.partial");
 }
 public void Publish()
 {
  LocalStoragePath.FilePath(FilePath,mustExist:true,rejectSidecars:true);
  LocalStoragePath.FilePath(target,mustExist:false,mustBeNew:true,rejectSidecars:true);
  WorkspaceConfidentiality.ProtectOwnedStagingFile(FilePath);
  File.Move(FilePath,target,false);
  // Do not report success if the destination filesystem did not retain the protected DACL.
  // Leave any already-published file intact for manual review; never silently delete it.
  if(OperatingSystem.IsWindows()&&!WorkspaceConfidentiality.HasRestrictedFileAcl(target))
   throw new InvalidOperationException("Recovery file published but permissions require manual review.");
 }
 public void Dispose()
 {
  LocalStoragePath.DirectoryPath(directory);
  foreach(var name in new[]{"copy.partial","copy.partial-journal","copy.partial-wal","copy.partial-shm"})
  {
   var path=Path.Combine(directory,name);
   FileAttributes a;
   try{a=File.GetAttributes(path);}catch(FileNotFoundException){continue;}
   if((a&(FileAttributes.Directory|FileAttributes.ReparsePoint))!=0)
    throw new InvalidOperationException("Unexpected staging entry; manual cleanup required.");
   File.Delete(path);
  }
  Directory.Delete(directory,false);
 }
}
