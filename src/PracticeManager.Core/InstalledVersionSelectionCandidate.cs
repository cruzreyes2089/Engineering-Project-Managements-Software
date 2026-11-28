using System.Text;
namespace PracticeManager.Core;

// Program selection only. No execution, schema authorization, signature
// verification, automatic rollback, installer registration or database access.
// Caller must independently authorize exact release and upgrade compatibility.
public static class InstalledVersionSelectionCandidate
{
 const string ActiveName="active-version.txt";
 const string IntentName="activation-pending.txt";
 const string RemovalIntent="uninstall-pending.txt";
 const string InstallIntent="install-pending.txt";
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
  if(Exists(Path.Combine(boundary.ProgramRoot,IntentName))||Exists(Path.Combine(boundary.ProgramRoot,RemovalIntent))||Exists(Path.Combine(boundary.ProgramRoot,InstallIntent))||Exists(Path.Combine(boundary.ProgramRoot,"registration-pending.txt")))
   throw new ArgumentException("Interrupted program selection requires review. No automatic rollback.");
 }
 static string? Current(InstalledProgramBoundary boundary)
  =>Exists(Path.Combine(boundary.ProgramRoot,ActiveName))?Read(boundary,ActiveName):null;
 // Hold the same lease for the entire installed application lifetime.
 // Refuse stale or relocated executables and interrupted install operations.
 public static IDisposable AcquireSelectedLaunchLease(InstalledProgramBoundary boundary,string executable)
 {
  var lease=boundary.AcquireExistingLease();
  try
  {
   NoPending(boundary);
   var current=Current(boundary)??throw new ArgumentException("No active program version.");
   var expected=Path.Combine(boundary.ReviewExistingVersion(current),"CRC.PracticeManager.exe");
   var actual=LocalStoragePath.FilePath(executable,mustExist:true,rejectSidecars:true);
   if(!string.Equals(actual,expected,StringComparison.OrdinalIgnoreCase))
    throw new ArgumentException("This program version is not the selected installed version.");
   InstalledPayloadVerification.Verify(boundary,current);
   return lease;
  }
  catch{lease.Dispose();throw;}
 }
 // Not wired to NSIS while release is held. Registers only the current
 // verified version; an interrupted registration prevents cooperative startup.
 [System.Runtime.Versioning.SupportedOSPlatform("windows")]
 internal static void RegisterReviewedInstallation(InstalledProgramBoundary boundary,string expectedVersion,string expectedUninstallerHash,Microsoft.Win32.RegistryKey parent,string shortcutDirectory)
 {
  using var lease=boundary.AcquireExistingLease();
  NoPending(boundary);
  if(Current(boundary)!=expectedVersion)throw new ArgumentException("Selected program changed.");
  _=boundary.VerifyInitializedLayout();
  InstalledPayloadVerification.Verify(boundary,expectedVersion);
  var receiptPath=LocalStoragePath.FilePath(Path.Combine(boundary.ProgramRoot,"registration-receipt.json"),mustExist:false,mustBeNew:true,rejectSidecars:true);
  using(var existing=parent.OpenSubKey(WindowsRegistrationCandidate.ProductKey,false))
   if(existing!=null)throw new ArgumentException("Existing Windows registration requires review.");
  _=LocalStoragePath.FilePath(Path.Combine(shortcutDirectory,WindowsRegistrationCandidate.ShortcutName),mustExist:false,mustBeNew:true,rejectSidecars:true);
  WriteNew(boundary,"registration-pending.txt",expectedVersion);
  RegisterUnderLease(boundary,expectedVersion,expectedUninstallerHash,parent,shortcutDirectory);
 }
 [System.Runtime.Versioning.SupportedOSPlatform("windows")]
 static void RegisterUnderLease(InstalledProgramBoundary boundary,string expectedVersion,string expectedUninstallerHash,Microsoft.Win32.RegistryKey parent,string shortcutDirectory)
 {
  var receiptPath=LocalStoragePath.FilePath(ReceiptPath(boundary),mustExist:false,mustBeNew:true,rejectSidecars:true);
  var receipt=WindowsRegistrationCandidate.RegisterFresh(boundary,expectedVersion,expectedUninstallerHash,parent,shortcutDirectory);
  var raw=System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(receipt);
  using(var stream=new FileStream(receiptPath,FileMode.CreateNew,FileAccess.Write,FileShare.None))
  {stream.Write(raw);stream.Flush(true);}
  if(!WorkspaceConfidentiality.HasWorkspaceFileAcl(receiptPath))throw new ArgumentException("Registration receipt permissions require review.");
  WindowsRegistrationCandidate.VerifyOwnedRegistration(boundary,parent,shortcutDirectory,receipt);
  if(Read(boundary,"registration-pending.txt")!=expectedVersion||Current(boundary)!=expectedVersion)
   throw new ArgumentException("Registration outcome requires review.");
  File.Delete(Path.Combine(boundary.ProgramRoot,"registration-pending.txt"));
 }
 // Fresh registered installation handoff. The caller supplies independently
 // reviewed payload and helper digests; no executable is invoked here.
 [System.Runtime.Versioning.SupportedOSPlatform("windows")]
 internal static InstalledProgramBoundary InstallFreshRegistered(string localAppData,string candidate,string extractedDirectory,
  byte[] inventoryBytes,string uninstallerSource,string expectedUninstallerHash,Microsoft.Win32.RegistryKey parent,string shortcutDirectory)
 {
  var profile=LocalStoragePath.DirectoryPath(localAppData);
  var source=LocalStoragePath.DirectoryPath(extractedDirectory);
  var helper=LocalStoragePath.FilePath(uninstallerSource,mustExist:true,rejectSidecars:true);
  var root=Path.Combine(profile,"Programs","CRC Practice Manager");
  if(ProjectFolderPolicy.Overlaps(source,root)||ProjectFolderPolicy.Overlaps(helper,root)||ProjectFolderPolicy.Overlaps(helper,source))
   throw new ArgumentException("Use separate reviewed extraction and uninstaller locations.");
  if(expectedUninstallerHash.Length!=64||expectedUninstallerHash.Any(c=>c is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
   throw new ArgumentException("Exact uninstaller digest required.");
  var raw=inventoryBytes.ToArray();
  _=InstalledPayloadVerification.ReviewTree(source,raw,candidate);
  using(var existing=parent.OpenSubKey(WindowsRegistrationCandidate.ProductKey,false))
   if(existing!=null)throw new ArgumentException("Existing Windows registration requires review.");
  _=LocalStoragePath.FilePath(Path.Combine(shortcutDirectory,WindowsRegistrationCandidate.ShortcutName),mustExist:false,mustBeNew:true,rejectSidecars:true);
  using var input=new FileStream(helper,FileMode.Open,FileAccess.Read,FileShare.Read);
  if(input.Length is <=0 or >250L*1024*1024||
     Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(input)).ToLowerInvariant()!=expectedUninstallerHash)
   throw new ArgumentException("Uninstaller content requires review.");
  input.Position=0;
  var boundary=InstalledProgramBoundary.InitializeNewInstallation(profile);
  using var lease=boundary.AcquireExistingLease();
  NoPending(boundary);
  if(Current(boundary)!=null)throw new ArgumentException("Existing selected program requires review.");
  WriteNew(boundary,"registration-pending.txt",candidate);
  var destination=LocalStoragePath.FilePath(Path.Combine(boundary.ProgramRoot,"CRC.Uninstall.exe"),mustExist:false,mustBeNew:true,rejectSidecars:true);
  using(var output=new FileStream(destination,FileMode.CreateNew,FileAccess.Write,FileShare.None))
  {input.CopyTo(output);output.Flush(true);}
  if(!WorkspaceConfidentiality.HasWorkspaceFileAcl(destination))throw new ArgumentException("Uninstaller permissions require review.");
  using(var verified=new FileStream(destination,FileMode.Open,FileAccess.Read,FileShare.Read))
   if(verified.Length!=input.Length||Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(verified)).ToLowerInvariant()!=expectedUninstallerHash)
    throw new ArgumentException("Staged uninstaller content requires review.");
  StageUnderLease(boundary,null,candidate,source,raw);
  RegisterUnderLease(boundary,candidate,expectedUninstallerHash,parent,shortcutDirectory);
  return boundary;
 }
 static string ReceiptPath(InstalledProgramBoundary boundary)=>Path.Combine(boundary.ProgramRoot,"registration-receipt.json");
 static void RequireUnregistered(InstalledProgramBoundary boundary)
 {
  if(Exists(ReceiptPath(boundary)))throw new ArgumentException("Use the registered installation lifecycle.");
 }
 [System.Runtime.Versioning.SupportedOSPlatform("windows")]
 static WindowsRegistrationCandidate.Registration ReadRegistration(InstalledProgramBoundary boundary,string version)
 {
  var path=LocalStoragePath.FilePath(ReceiptPath(boundary),mustExist:true,rejectSidecars:true);
  if(!WorkspaceConfidentiality.HasWorkspaceFileAcl(path))throw new ArgumentException("Registration receipt requires review.");
  using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
  if(stream.Length is <=0 or >16384)throw new ArgumentException("Registration receipt requires review.");
  var bytes=new byte[(int)stream.Length];stream.ReadExactly(bytes);
  var receipt=System.Text.Json.JsonSerializer.Deserialize<WindowsRegistrationCandidate.Registration>(bytes)??throw new ArgumentException("Registration receipt requires review.");
  if(receipt.Version!=version||receipt.Identity!=boundary.VerifyInitializedLayout())throw new ArgumentException("Registration identity changed.");
  return receipt;
 }
 static void ArchiveRegistration(InstalledProgramBoundary boundary)
 {
  var retained=LocalStoragePath.FilePath(Path.Combine(boundary.ProgramRoot,"registration-history-"+Guid.NewGuid().ToString("N")+".json"),mustExist:false,mustBeNew:true,rejectSidecars:true);
  File.Move(ReceiptPath(boundary),retained,false);
 }
 [System.Runtime.Versioning.SupportedOSPlatform("windows")]
 internal static void UpgradeRegisteredInstallation(InstalledProgramBoundary boundary,string expectedPrevious,string candidate,
  string extractedDirectory,byte[] inventoryBytes,Microsoft.Win32.RegistryKey parent,string shortcutDirectory)
 {
  using var lease=boundary.AcquireExistingLease();
  NoPending(boundary);
  if(Current(boundary)!=expectedPrevious)throw new ArgumentException("Selected program changed.");
  var receipt=ReadRegistration(boundary,expectedPrevious);
  WindowsRegistrationCandidate.VerifyOwnedRegistration(boundary,parent,shortcutDirectory,receipt);
  // Validate source before starting the transaction. Keep one outer pending
  // record across code selection, registration replacement and readback.
  _=InstalledPayloadVerification.ReviewTree(LocalStoragePath.DirectoryPath(extractedDirectory),inventoryBytes,candidate);
  WriteNew(boundary,"registration-pending.txt",candidate);
  StageUnderLease(boundary,expectedPrevious,candidate,extractedDirectory,inventoryBytes);
  WindowsRegistrationCandidate.RemoveOwnedRegistration(boundary,parent,shortcutDirectory,receipt);
  ArchiveRegistration(boundary);
  var replacement=WindowsRegistrationCandidate.RegisterFresh(boundary,candidate,receipt.UninstallerHash,parent,shortcutDirectory);
  var path=LocalStoragePath.FilePath(ReceiptPath(boundary),mustExist:false,mustBeNew:true,rejectSidecars:true);
  using(var stream=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None))
  {stream.Write(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(replacement));stream.Flush(true);}
  if(ReadRegistration(boundary,candidate)!=replacement)throw new ArgumentException("Registration receipt changed.");
  WindowsRegistrationCandidate.VerifyOwnedRegistration(boundary,parent,shortcutDirectory,replacement);
  if(Current(boundary)!=candidate||Read(boundary,"registration-pending.txt")!=candidate)throw new ArgumentException("Upgrade registration requires review.");
  File.Delete(Path.Combine(boundary.ProgramRoot,"registration-pending.txt"));
 }
 [System.Runtime.Versioning.SupportedOSPlatform("windows")]
 internal static int RemoveRegisteredInstallation(InstalledProgramBoundary boundary,string expectedActive,Microsoft.Win32.RegistryKey parent,string shortcutDirectory)
 {
  using var lease=boundary.AcquireExistingLease();
  NoPending(boundary);
  if(Current(boundary)!=expectedActive)throw new ArgumentException("Selected program changed.");
  var receipt=ReadRegistration(boundary,expectedActive);
  InstalledPayloadVerification.Verify(boundary,expectedActive);
  WindowsRegistrationCandidate.VerifyOwnedRegistration(boundary,parent,shortcutDirectory,receipt);
  WriteNew(boundary,"registration-pending.txt",expectedActive);
  WindowsRegistrationCandidate.RemoveOwnedRegistration(boundary,parent,shortcutDirectory,receipt);
  ArchiveRegistration(boundary);
  var removed=RemoveUnderLease(boundary,expectedActive);
  if(Current(boundary)!=null||Read(boundary,"registration-pending.txt")!=expectedActive)throw new ArgumentException("Removal registration requires review.");
  File.Delete(Path.Combine(boundary.ProgramRoot,"registration-pending.txt"));
  return removed;
 }
 public static string? ReviewCurrent(InstalledProgramBoundary boundary)
 {
  using var lease=boundary.AcquireExistingLease();
  NoPending(boundary);
  var current=Current(boundary);
  if(current!=null)InstalledPayloadVerification.Verify(boundary,current);
  return current;
 }
 // Unwired code-only uninstall candidate. The future installer must obtain
 // release approval and explicit user confirmation; never touches practice data.
 // Retains previous versions, manifests, lease and selection history.
 public static int RemoveReviewedActiveVersion(InstalledProgramBoundary boundary,string expectedActive)
 {
  using var lease=boundary.AcquireExistingLease();
  NoPending(boundary);
  RequireUnregistered(boundary);
  return RemoveUnderLease(boundary,expectedActive);
 }
 static int RemoveUnderLease(InstalledProgramBoundary boundary,string expectedActive)
 {
  if(Current(boundary)!=expectedActive)throw new ArgumentException("Active program selection changed.");
  var payload=InstalledPayloadVerification.Review(boundary,expectedActive);
  string Resolve(string relative)=>Path.Combine(payload.Directory,relative.Replace('/',Path.DirectorySeparatorChar));
  void CheckDirectory(string path)
  {
   LocalStoragePath.DirectoryPath(path);
   if(!WorkspaceConfidentiality.HasRestrictedDirectoryAcl(path))throw new ArgumentException("Program directory permissions require review.");
  }
  void CheckFile(string relative,(long Bytes,string Hash) expected)
  {
   var path=LocalStoragePath.FilePath(Resolve(relative),mustExist:true,rejectSidecars:true);
   if(!WorkspaceConfidentiality.HasWorkspaceFileAcl(path))throw new ArgumentException("Program file permissions require review.");
   using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
   if(stream.Length!=expected.Bytes||Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream)).ToLowerInvariant()!=expected.Hash)
    throw new ArgumentException("Program file changed; removal requires review.");
  }
  CheckDirectory(payload.Directory);
  foreach(var dir in payload.Subdirectories)CheckDirectory(Resolve(dir));
  foreach(var file in payload.Files)CheckFile(file.Key,file.Value);
  WriteNew(boundary,RemovalIntent,expectedActive);
  if(Current(boundary)!=expectedActive||Read(boundary,RemovalIntent)!=expectedActive)
   throw new ArgumentException("Program removal selection changed.");
  InstalledPayloadVerification.Verify(boundary,expectedActive);
  var retained=LocalStoragePath.FilePath(Path.Combine(boundary.ProgramRoot,"uninstalled-"+Guid.NewGuid().ToString("N")+".txt"),mustExist:false,mustBeNew:true,rejectSidecars:true);
  File.Move(Path.Combine(boundary.ProgramRoot,ActiveName),retained,false);
  if(Read(boundary,Path.GetFileName(retained))!=expectedActive||Current(boundary)!=null)
   throw new InvalidOperationException("Program removal outcome requires review.");
  // No recursive deletion, wildcards, directory adoption or fallback. If a
  // deletion fails, keep the pending record and all remaining files.
  foreach(var file in payload.Files)
  {
   CheckFile(file.Key,file.Value);
   File.Delete(Resolve(file.Key));
  }
  foreach(var dir in payload.Subdirectories){CheckDirectory(Resolve(dir));Directory.Delete(Resolve(dir),false);}
  CheckDirectory(payload.Directory);Directory.Delete(payload.Directory,false);
  if(Read(boundary,RemovalIntent)!=expectedActive||Current(boundary)!=null)
   throw new InvalidOperationException("Program removal outcome requires review.");
  File.Delete(Path.Combine(boundary.ProgramRoot,RemovalIntent));
  return payload.Files.Count;
 }
 // Combined code-only install candidate, not a release authorization.
 // The input is an independently reviewed, isolated extraction directory;
 // this method is not called by desktop or NSIS while the hold is active.
 public static void StageAndSelectReviewedPayload(InstalledProgramBoundary boundary,string? expectedPrevious,
  string candidate,string extractedDirectory,byte[] inventoryBytes)
 {
  using var lease=boundary.AcquireExistingLease();
  NoPending(boundary);
  RequireUnregistered(boundary);
  StageUnderLease(boundary,expectedPrevious,candidate,extractedDirectory,inventoryBytes);
 }
 static void StageUnderLease(InstalledProgramBoundary boundary,string? expectedPrevious,string candidate,string extractedDirectory,byte[] inventoryBytes)
 {
  var previous=Current(boundary);
  if(previous!=expectedPrevious||candidate==previous)throw new ArgumentException("Program selection changed.");
  if(previous!=null)InstalledPayloadVerification.Verify(boundary,previous);
  var target=boundary.VersionDirectory(candidate);
  var source=LocalStoragePath.DirectoryPath(extractedDirectory);
  if(ProjectFolderPolicy.Overlaps(source,boundary.ProgramRoot))
   throw new ArgumentException("Use separate isolated extraction directory.");
  var raw=inventoryBytes.ToArray();
  var payload=InstalledPayloadVerification.ReviewTree(source,raw,candidate);
  var versions=LocalStoragePath.DirectoryPath(boundary.VersionsRoot);
  var manifests=LocalStoragePath.DirectoryPath(Path.Combine(boundary.ProgramRoot,"manifests"));
  if(!WorkspaceConfidentiality.HasRestrictedDirectoryAcl(versions)||!WorkspaceConfidentiality.HasRestrictedDirectoryAcl(manifests))
   throw new ArgumentException("Program storage permissions require review.");
  var manifestPath=LocalStoragePath.FilePath(Path.Combine(manifests,candidate+".json"),mustExist:false,mustBeNew:true,rejectSidecars:true);
  if(Exists(target))throw new ArgumentException("Version already exists; no replacement permitted.");
  WriteNew(boundary,InstallIntent,candidate);
  WorkspaceConfidentiality.EnsureWorkspaceDirectory(target);
  if(!WorkspaceConfidentiality.HasRestrictedDirectoryAcl(target)||Directory.EnumerateFileSystemEntries(target).Any())
   throw new ArgumentException("New version directory requires review.");
  string Resolve(string directory,string name)=>Path.Combine(directory,name.Replace('/',Path.DirectorySeparatorChar));
  foreach(var dir in payload.Subdirectories.Reverse())
  {
   var path=Resolve(target,dir);
   if(Exists(path))throw new ArgumentException("Unexpected staging directory.");
   WorkspaceConfidentiality.EnsureWorkspaceDirectory(path);
   if(!WorkspaceConfidentiality.HasRestrictedDirectoryAcl(path))throw new ArgumentException("Staging permissions require review.");
  }
  foreach(var file in payload.Files)
  {
   var input=LocalStoragePath.FilePath(Resolve(source,file.Key),mustExist:true,rejectSidecars:true);
   var output=LocalStoragePath.FilePath(Resolve(target,file.Key),mustExist:false,mustBeNew:true,rejectSidecars:true);
   if(!WorkspaceConfidentiality.HasRestrictedDirectoryAcl(Path.GetDirectoryName(output)!))throw new ArgumentException("Staging permissions require review.");
   using(var from=new FileStream(input,FileMode.Open,FileAccess.Read,FileShare.Read))
   using(var to=new FileStream(output,FileMode.CreateNew,FileAccess.Write,FileShare.None))
   {
    if(from.Length!=file.Value.Bytes)throw new ArgumentException("Source file changed.");
    var buffer=new byte[65536];long count=0;int n;
    while((n=from.Read(buffer,0,buffer.Length))!=0)
    {
     count+=n;if(count>file.Value.Bytes)throw new ArgumentException("Source file changed.");
     to.Write(buffer,0,n);
    }
    if(count!=file.Value.Bytes)throw new ArgumentException("Source file changed.");
    to.Flush(true);
   }
   if(!WorkspaceConfidentiality.HasWorkspaceFileAcl(output))throw new ArgumentException("Staged file permissions require review.");
  }
  using(var stream=new FileStream(manifestPath,FileMode.CreateNew,FileAccess.Write,FileShare.None))
  {stream.Write(raw);stream.Flush(true);}
  InstalledPayloadVerification.Verify(boundary,candidate);
  if(Read(boundary,InstallIntent)!=candidate)throw new ArgumentException("Installation intent changed.");
  SelectUnderLease(boundary,expectedPrevious,candidate);
  if(Current(boundary)!=candidate||Read(boundary,InstallIntent)!=candidate)throw new InvalidOperationException("Installation requires review.");
  File.Delete(Path.Combine(boundary.ProgramRoot,InstallIntent));
  // A failure at any later step retains evidence; never automatically delete
  // staged files, restore the old pointer, or alter business data.
 }
 public static void SelectReviewedVersion(InstalledProgramBoundary boundary,string? expectedPrevious,string candidate)
 {
  using var lease=boundary.AcquireExistingLease();
  NoPending(boundary);
  RequireUnregistered(boundary);
  SelectUnderLease(boundary,expectedPrevious,candidate);
 }
 static void SelectUnderLease(InstalledProgramBoundary boundary,string? expectedPrevious,string candidate)
 {
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
   // Windows File.Replace may merge the destination DACL with the source
   // DACL. Use two same-directory no-overwrite moves while the exclusive
   // lease and durable pending record forbid every cooperative startup.
   // The two moves are not atomic together; an interruption requires review.
   File.Move(active,retained,false);
   if(Read(boundary,Path.GetFileName(retained))!=previous||Current(boundary)!=null)
    throw new InvalidOperationException("Prior selection requires review.");
   File.Move(staged,active,false);
  }
  if(Read(boundary,ActiveName)!=candidate||Read(boundary,IntentName)!=candidate)
   throw new InvalidOperationException("Program selection requires review.");
  InstalledPayloadVerification.Verify(boundary,candidate);
  // Remove only the exact owned pending record after observed success. A
  // crash before this deletion leaves review required, never auto-reversed.
  File.Delete(Path.Combine(boundary.ProgramRoot,IntentName));
 }
}
