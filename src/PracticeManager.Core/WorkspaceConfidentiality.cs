using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
namespace PracticeManager.Core;

// Conservative directory DACL layout check, not an effective-access test,
// encryption check or defense against an administrator or same-user process.
public static class WorkspaceConfidentiality
{
 public static string EnsureWorkspaceDirectory(string path)
 {
  var full=LocalStoragePath.Normalize(path);
  LocalStoragePath.DirectoryPath(Path.GetDirectoryName(full)??throw new ArgumentException("Workspace parent required."));
  // Never alter a pre-existing directory's ACL, including a legacy workspace.
  if(Directory.Exists(full))return LocalStoragePath.DirectoryPath(full);
  if(!OperatingSystem.IsWindows())return LocalStoragePath.DirectoryPath(full,create:true);
  return CreateRestrictedWindowsDirectory(full);
 }
 [SupportedOSPlatform("windows")]
 private static HashSet<SecurityIdentifier> TrustedPrincipals()
 {
  using var identity=WindowsIdentity.GetCurrent();
  var user=identity.User??throw new InvalidOperationException("Current Windows identity unavailable.");
  return new(){user,new(WellKnownSidType.LocalSystemSid,null),new(WellKnownSidType.BuiltinAdministratorsSid,null)};
 }
 [SupportedOSPlatform("windows")]
 private static string CreateRestrictedWindowsDirectory(string full)
 {
  var trusted=TrustedPrincipals();
  using var identity=WindowsIdentity.GetCurrent();
  var security=new DirectorySecurity();
  security.SetOwner(identity.User??throw new InvalidOperationException("Current Windows identity unavailable."));
  security.SetAccessRuleProtection(true,false);
  foreach(var sid in trusted)
   security.AddAccessRule(new FileSystemAccessRule(sid,FileSystemRights.FullControl,
    InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit,PropagationFlags.None,AccessControlType.Allow));
  // Specify security at creation rather than create with broad inherited ACL and tighten later.
  // Existing directory creation is a no-op; the subsequent check fails closed if its DACL differs.
  new DirectoryInfo(full).Create(security);
  LocalStoragePath.DirectoryPath(full);
  if(!HasRestrictedDirectoryAcl(full))throw new ArgumentException("New workspace permissions could not be verified.");
  return full;
 }
 public static bool HasRestrictedDirectoryAcl(string path)
 {
  var full=LocalStoragePath.DirectoryPath(path);
  if(!OperatingSystem.IsWindows())return false;
  return CheckWindowsDirectory(full);
 }
 [SupportedOSPlatform("windows")]
 private static bool CheckWindowsDirectory(string full)
 {
  var trusted=TrustedPrincipals();
  var security=new DirectoryInfo(full).GetAccessControl(AccessControlSections.Access|AccessControlSections.Owner);
  // A null DACL grants broad access; reject it explicitly.
  var raw=new RawSecurityDescriptor(security.GetSecurityDescriptorBinaryForm(),0);
  if(raw.DiscretionaryAcl is null||!security.AreAccessRulesProtected||!security.AreAccessRulesCanonical||
     security.GetOwner(typeof(SecurityIdentifier)) is not SecurityIdentifier owner||!trusted.Contains(owner))return false;
  var seen=new HashSet<SecurityIdentifier>();
  foreach(FileSystemAccessRule rule in security.GetAccessRules(true,true,typeof(SecurityIdentifier)))
  {
   if(rule.IdentityReference is not SecurityIdentifier sid||!trusted.Contains(sid)||!seen.Add(sid)||
      rule.IsInherited||rule.AccessControlType!=AccessControlType.Allow||rule.FileSystemRights!=FileSystemRights.FullControl||
      rule.InheritanceFlags!=(InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit)||
      rule.PropagationFlags!=PropagationFlags.None)return false;
  }
  return seen.SetEquals(trusted);
 }
 // Only for a newly created, application-owned staging file. Never call this
 // on the original recovery source or a pre-existing database.
 public static void ProtectOwnedStagingFile(string path)
 {
  var full=LocalStoragePath.FilePath(path,mustExist:true,rejectSidecars:true);
  if(!OperatingSystem.IsWindows())return;
  ProtectWindowsFile(full);
 }
 [SupportedOSPlatform("windows")]
 static void ProtectWindowsFile(string full)
 {
  if(!HasRestrictedDirectoryAcl(Path.GetDirectoryName(full)!))
   throw new ArgumentException("Recovery staging directory permissions not qualified.");
  using var identity=WindowsIdentity.GetCurrent();
  var security=new FileSecurity();
  security.SetOwner(identity.User??throw new InvalidOperationException("Current Windows identity unavailable."));
  security.SetAccessRuleProtection(true,false);
  foreach(var sid in TrustedPrincipals())
   security.AddAccessRule(new FileSystemAccessRule(sid,FileSystemRights.FullControl,AccessControlType.Allow));
  new FileInfo(full).SetAccessControl(security);
  if(!HasRestrictedFileAcl(full))throw new ArgumentException("Recovery file permissions could not be verified.");
 }
 public static bool HasRestrictedFileAcl(string path)
 {
  var full=LocalStoragePath.FilePath(path,mustExist:true,rejectSidecars:true);
  return OperatingSystem.IsWindows()&&CheckWindowsFile(full);
 }
 // Database and marker files normally inherit the qualified parent DACL.
 // Accept the same exact trusted principals on an inherited or explicit file
 // DACL, without changing the file. Recovery staging still requires explicit ACLs.
 public static bool HasWorkspaceFileAcl(string path)
 {
  var full=LocalStoragePath.FilePath(path,mustExist:true);
  return OperatingSystem.IsWindows()&&HasRestrictedDirectoryAcl(Path.GetDirectoryName(full)!)&&CheckWindowsFile(full,allowInherited:true);
 }
 [SupportedOSPlatform("windows")]
 static bool CheckWindowsFile(string full,bool allowInherited=false)
 {
  return CheckWindowsFileSecurity(new FileInfo(full).GetAccessControl(AccessControlSections.Access|AccessControlSections.Owner),allowInherited);
 }
 // Inspect the same open handle used by the exclusive program lease.
 internal static bool HasRestrictedWorkspaceFileHandleAcl(FileStream stream)
  =>OperatingSystem.IsWindows()&&CheckWindowsFileHandle(stream);
 [SupportedOSPlatform("windows")]
 static bool CheckWindowsFileHandle(FileStream stream)
  =>CheckWindowsFileSecurity(stream.GetAccessControl(),true);
 [SupportedOSPlatform("windows")]
 static bool CheckWindowsFileSecurity(FileSecurity security,bool allowInherited)
 {
  var trusted=TrustedPrincipals();
  var raw=new RawSecurityDescriptor(security.GetSecurityDescriptorBinaryForm(),0);
  if(raw.DiscretionaryAcl is null||(!allowInherited&&!security.AreAccessRulesProtected)||!security.AreAccessRulesCanonical||
     security.GetOwner(typeof(SecurityIdentifier)) is not SecurityIdentifier owner||!trusted.Contains(owner))return false;
  var seen=new HashSet<SecurityIdentifier>();
  foreach(FileSystemAccessRule rule in security.GetAccessRules(true,true,typeof(SecurityIdentifier)))
   if(rule.IdentityReference is not SecurityIdentifier sid||!trusted.Contains(sid)||!seen.Add(sid)||
      (!allowInherited&&rule.IsInherited)||rule.AccessControlType!=AccessControlType.Allow||rule.FileSystemRights!=FileSystemRights.FullControl||
      rule.InheritanceFlags!=InheritanceFlags.None||rule.PropagationFlags!=PropagationFlags.None)return false;
  return seen.SetEquals(trusted);
 }

}
