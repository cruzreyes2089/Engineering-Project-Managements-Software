namespace PracticeManager.Core;

// Shared program-only contract for the eventual installer and installed app.
// No production startup, activation pointer changes, deletion or payload copying.
public sealed class InstalledProgramBoundary
{
 public string ProgramRoot{get;}
 public string VersionsRoot=>Path.Combine(ProgramRoot,"versions");
 public string LeasePath=>Path.Combine(ProgramRoot,"program.lock");
 InstalledProgramBoundary(string root){ProgramRoot=root;}
 public static InstalledProgramBoundary ReviewExisting(string localAppData)
 {
  if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException("Windows installation required.");
  var profile=LocalStoragePath.DirectoryPath(localAppData);
  var root=LocalStoragePath.DirectoryPath(Path.Combine(profile,"Programs","CRC Practice Manager"));
  if(!WorkspaceConfidentiality.HasRestrictedDirectoryAcl(root))
   throw new ArgumentException("Program directory permissions require review.");
  return new(root);
 }
 // A full manifest digest identifies immutable payload directories, not a
 // caller-provided version path. No directory is created by this method.
 public string VersionDirectory(string inventorySha256)
 {
  if(inventorySha256==null||inventorySha256.Length!=64||
     inventorySha256.Any(c=>c is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
   throw new ArgumentException("Exact lowercase inventory SHA-256 required.");
  VerifyRoot();
  return LocalStoragePath.Normalize(Path.Combine(VersionsRoot,inventorySha256));
 }
 void VerifyRoot()
 {
  LocalStoragePath.DirectoryPath(ProgramRoot);
  if(!WorkspaceConfidentiality.HasRestrictedDirectoryAcl(ProgramRoot))
   throw new ArgumentException("Program directory permissions require review.");
 }
 // Reserve a persistent empty coordination file only during explicit first
 // installation. Never truncate, replace or delete an existing lease file.
 public void ReserveNewLeaseFile()
 {
  VerifyRoot();
  var path=LocalStoragePath.FilePath(LeasePath,mustExist:false,mustBeNew:true,rejectSidecars:true);
  using var stream=new FileStream(path,FileMode.CreateNew,FileAccess.ReadWrite,FileShare.None);
  if(!WorkspaceConfidentiality.HasRestrictedWorkspaceFileHandleAcl(stream))
   throw new ArgumentException("Program coordination file permissions require review.");
  stream.Flush(true);
 }
 public IDisposable AcquireExistingLease()
 {
  VerifyRoot();
  var path=LocalStoragePath.FilePath(LeasePath,mustExist:true,rejectSidecars:true);
  // No FileMode.OpenOrCreate. Both participants must use the same persistent
  // file and retain the exclusive handle until their entire operation ends.
  // Contention or access errors propagate; never fall back to another path.
  var stream=new FileStream(path,FileMode.Open,FileAccess.ReadWrite,FileShare.None);
  try
  {
   if(stream.Length!=0||!WorkspaceConfidentiality.HasRestrictedWorkspaceFileHandleAcl(stream))
    throw new ArgumentException("Program coordination file requires review.");
   VerifyRoot();
   return stream;
  }
  catch{stream.Dispose();throw;}
 }
 // Caller must hold the lease across validation and later activation. This
 // review does not verify payload bytes or permit adopting an unverified version.
 public string ReviewExistingVersion(string inventorySha256)
 {
  var target=VersionDirectory(inventorySha256);
  var versions=LocalStoragePath.DirectoryPath(VersionsRoot);
  var version=LocalStoragePath.DirectoryPath(target);
  if(!WorkspaceConfidentiality.HasRestrictedDirectoryAcl(versions)||
     !WorkspaceConfidentiality.HasRestrictedDirectoryAcl(version))
   throw new ArgumentException("Version directory permissions require review.");
  return version;
 }
}
