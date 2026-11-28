namespace PracticeManager.Core;

// Shared program-only contract for the eventual installer and installed app.
// No production startup, activation pointer changes, deletion or payload copying.
public sealed class InstalledProgramBoundary
{
 public string ProgramRoot{get;}
 public string VersionsRoot=>Path.Combine(ProgramRoot,"versions");
 public string LeasePath=>Path.Combine(ProgramRoot,"program.lock");
 InstalledProgramBoundary(string root){ProgramRoot=root;}
 const string IdentityName="installation-identity.txt";
 const string IdentityPrefix="CRC-INSTALL-V1:";
 static bool Exists(string path)
 {
  try{_=File.GetAttributes(path);return true;}
  catch(FileNotFoundException){return false;}
  catch(DirectoryNotFoundException){return false;}
 }
 // Explicit fresh installation only. Build the private directory layout at a
 // unique sibling and publish it by a no-overwrite directory move. Never
 // adopt an existing installation directory or repair its permissions.
 public static InstalledProgramBoundary InitializeNewInstallation(string localAppData)
 {
  if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException("Windows installation required.");
  var profile=LocalStoragePath.DirectoryPath(localAppData);
  var programs=Path.Combine(profile,"Programs");
  if(!Exists(programs))WorkspaceConfidentiality.EnsureWorkspaceDirectory(programs);
  LocalStoragePath.DirectoryPath(programs);
  var target=Path.Combine(programs,"CRC Practice Manager");
  if(Exists(target))throw new ArgumentException("Existing program directory requires installation review.");
  // A leftover reservation is evidence of an interrupted first installation.
  if(Directory.EnumerateFileSystemEntries(programs,".crc-install-reservation-*",SearchOption.TopDirectoryOnly).Any())
   throw new ArgumentException("Interrupted first installation requires review.");
  var stage=Path.Combine(programs,".crc-install-reservation-"+Guid.NewGuid().ToString("N"));
  if(Exists(stage))throw new ArgumentException("Installation reservation already exists.");
  WorkspaceConfidentiality.EnsureWorkspaceDirectory(stage);
  if(!WorkspaceConfidentiality.HasRestrictedDirectoryAcl(stage)||Directory.EnumerateFileSystemEntries(stage).Any())
   throw new ArgumentException("Installation reservation requires review.");
  var temporary=new InstalledProgramBoundary(stage);
  temporary.ReserveNewLeaseFile();
  WorkspaceConfidentiality.EnsureWorkspaceDirectory(temporary.VersionsRoot);
  WorkspaceConfidentiality.EnsureWorkspaceDirectory(Path.Combine(stage,"manifests"));
  var id=Guid.NewGuid().ToString("N");
  var marker=LocalStoragePath.FilePath(Path.Combine(stage,IdentityName),mustExist:false,mustBeNew:true,rejectSidecars:true);
  using(var stream=new FileStream(marker,FileMode.CreateNew,FileAccess.Write,FileShare.None))
  {stream.Write(System.Text.Encoding.ASCII.GetBytes(IdentityPrefix+id));stream.Flush(true);}
  temporary.VerifyInitializedLayout();
  if(Exists(target))throw new ArgumentException("Program directory appeared; review before retrying.");
  Directory.Move(stage,target);
  var result=ReviewInitialized(localAppData);
  if(result.VerifyInitializedLayout()!=id)throw new InvalidOperationException("Published installation identity requires review.");
  return result;
 }
 public static InstalledProgramBoundary ReviewInitialized(string localAppData)
 {
  var boundary=ReviewExisting(localAppData);
  _=boundary.VerifyInitializedLayout();
  return boundary;
 }
 public string VerifyInitializedLayout()
 {
  VerifyRoot();
  foreach(var dir in new[]{VersionsRoot,Path.Combine(ProgramRoot,"manifests")})
   if(!WorkspaceConfidentiality.HasRestrictedDirectoryAcl(LocalStoragePath.DirectoryPath(dir)))
    throw new ArgumentException("Program storage permissions require review.");
  var marker=LocalStoragePath.FilePath(Path.Combine(ProgramRoot,IdentityName),mustExist:true,rejectSidecars:true);
  if(!WorkspaceConfidentiality.HasWorkspaceFileAcl(marker))throw new ArgumentException("Installation identity permissions require review.");
  using var stream=new FileStream(marker,FileMode.Open,FileAccess.Read,FileShare.Read);
  if(stream.Length!=IdentityPrefix.Length+32)throw new ArgumentException("Installation identity invalid.");
  var bytes=new byte[(int)stream.Length];stream.ReadExactly(bytes);
  var value=System.Text.Encoding.ASCII.GetString(bytes);
  if(!value.StartsWith(IdentityPrefix,StringComparison.Ordinal)||!Guid.TryParseExact(value[IdentityPrefix.Length..],"N",out var id)||id==Guid.Empty||value!=IdentityPrefix+id.ToString("N"))
   throw new ArgumentException("Installation identity invalid.");
  _=LocalStoragePath.FilePath(LeasePath,mustExist:true,rejectSidecars:true);
  return id.ToString("N");
 }
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
