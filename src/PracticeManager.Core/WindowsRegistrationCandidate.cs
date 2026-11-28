using Microsoft.Win32;
using System.Runtime.Versioning;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
namespace PracticeManager.Core;

// Source-only registration backend. No automatic call from installer or app.
// The coordinator must hold the program lease and an interruption marker.
[SupportedOSPlatform("windows")]
internal static class WindowsRegistrationCandidate
{
 internal const string ProductKey="CRC.PracticeManager";
 internal const string UninstallParent=@"Software\Microsoft\Windows\CurrentVersion\Uninstall";
 internal const string ShortcutName="CRC Practice Manager.lnk";
 internal sealed record Registration(string Identity,string Version,string ProgramRoot,string Executable,string Uninstaller,string UninstallerHash,string ShortcutHash);
 static string FileHash(string path)
 {
  using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
  return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
 }
 static bool Exists(string path)
 {
  try{_=File.GetAttributes(path);return true;}
  catch(FileNotFoundException){return false;}
  catch(DirectoryNotFoundException){return false;}
 }
 static void Require(bool ok)
 {if(!ok)throw new ArgumentException("Windows registration ownership requires review.");}
 static bool IsDigest(string value)=>value.Length==64&&value.All(c=>c is >= '0' and <= '9' or >= 'a' and <= 'f');
 static Dictionary<string,object> Values(Registration entry)=>new(StringComparer.Ordinal){
  ["DisplayName"]="CRC Practice Manager",["Publisher"]="CRC Engineering",
  ["InstallLocation"]=entry.ProgramRoot,["UninstallString"]="\""+entry.Uninstaller+"\"",
  ["DisplayIcon"]=entry.Executable,["NoModify"]=1,["NoRepair"]=1,
  ["CRCInstallationIdentity"]=entry.Identity,["CRCVersionInventory"]=entry.Version,
  ["CRCUninstallerSha256"]=entry.UninstallerHash,["CRCShortcutSha256"]=entry.ShortcutHash};
 internal static void VerifyOwnedKey(RegistryKey parent,Registration entry)
 {
  using var key=parent.OpenSubKey(ProductKey,false)??throw new ArgumentException("Windows registration missing.");
  var expected=Values(entry);
  Require(key.SubKeyCount==0&&key.GetValueNames().ToHashSet(StringComparer.Ordinal).SetEquals(expected.Keys));
  foreach(var item in expected)
  {
   Require(key.GetValueKind(item.Key)==(item.Value is int?RegistryValueKind.DWord:RegistryValueKind.String));
   Require(Equals(key.GetValue(item.Key,null,RegistryValueOptions.DoNotExpandEnvironmentNames),item.Value));
  }
 }
 internal static void WriteFreshKey(RegistryKey parent,Registration entry)
 {
  using(var existing=parent.OpenSubKey(ProductKey,false))Require(existing==null);
  using(var key=parent.CreateSubKey(ProductKey,true)??throw new InvalidOperationException("Cannot create Windows registration."))
  {
   Require(key.ValueCount==0&&key.SubKeyCount==0);
   foreach(var item in Values(entry))
    key.SetValue(item.Key,item.Value,item.Value is int?RegistryValueKind.DWord:RegistryValueKind.String);
   key.Flush();
  }
  VerifyOwnedKey(parent,entry);
 }
 internal static void RemoveOwnedKey(RegistryKey parent,Registration entry)
 {
  VerifyOwnedKey(parent,entry);
  // Nonrecursive, exact owned key only. Unexpected values/subkeys refused.
  parent.DeleteSubKey(ProductKey,true);parent.Flush();
  using var key=parent.OpenSubKey(ProductKey,false);Require(key==null);
 }
 static void WithShortcut(string path,Action<dynamic> action)
 {
  var type=Type.GetTypeFromProgID("WScript.Shell")??throw new InvalidOperationException("Windows shortcut service unavailable.");
  object shell=Activator.CreateInstance(type)??throw new InvalidOperationException("Windows shortcut service unavailable.");
  object? shortcut=null;
  try
  {
   shortcut=((dynamic)shell).CreateShortcut(path);
   if(shortcut==null)throw new InvalidOperationException("Windows shortcut service unavailable.");
   action((dynamic)shortcut);
  }
  finally
  {
   if(shortcut!=null&&Marshal.IsComObject(shortcut))Marshal.FinalReleaseComObject(shortcut);
   if(Marshal.IsComObject(shell))Marshal.FinalReleaseComObject(shell);
  }
 }
 static void VerifyShortcut(string path,string target,string workingDirectory)
 {
  LocalStoragePath.FilePath(path,mustExist:true,rejectSidecars:true);
  WithShortcut(path,link=>{
   Require(string.Equals((string)link.TargetPath,target,StringComparison.OrdinalIgnoreCase));
   Require(string.Equals((string)link.WorkingDirectory,workingDirectory,StringComparison.OrdinalIgnoreCase));
   Require((string)link.Arguments=="");
  });
 }
 internal static Registration RegisterFresh(InstalledProgramBoundary boundary,string version,string expectedUninstallerHash,RegistryKey parent,string shortcutDirectory)
 {
  var identity=boundary.VerifyInitializedLayout();
  var directory=boundary.ReviewExistingVersion(version);
  InstalledPayloadVerification.Verify(boundary,version);
  var executable=Path.Combine(directory,"CRC.PracticeManager.exe");
  var uninstaller=LocalStoragePath.FilePath(Path.Combine(boundary.ProgramRoot,"CRC.Uninstall.exe"),mustExist:true,rejectSidecars:true);
  Require(IsDigest(expectedUninstallerHash)&&WorkspaceConfidentiality.HasWorkspaceFileAcl(uninstaller)&&FileHash(uninstaller)==expectedUninstallerHash);
  var shortcuts=LocalStoragePath.DirectoryPath(shortcutDirectory);
  var shortcut=LocalStoragePath.FilePath(Path.Combine(shortcuts,ShortcutName),mustExist:false,mustBeNew:true,rejectSidecars:true);
  using(var existing=parent.OpenSubKey(ProductKey,false))Require(existing==null);
  // Reserve a temporary name; publish by no-overwrite move after readback.
  var staged=Path.Combine(shortcuts,".crc-shortcut-"+Guid.NewGuid().ToString("N")+".lnk");
  Require(!Exists(staged));
  WithShortcut(staged,link=>{link.TargetPath=executable;link.WorkingDirectory=directory;link.Arguments="";link.Save();});
  VerifyShortcut(staged,executable,directory);
  var hash=FileHash(staged);
  File.Move(staged,shortcut,false);
  VerifyShortcut(shortcut,executable,directory);
  Require(FileHash(shortcut)==hash);
  var entry=new Registration(identity,version,boundary.ProgramRoot,executable,uninstaller,expectedUninstallerHash,hash);
  WriteFreshKey(parent,entry);
  return entry;
 }
 // Existing registration is verified against the independently retained
 // receipt, not merely the current registry values or file name.
 internal static void VerifyOwnedRegistration(InstalledProgramBoundary boundary,RegistryKey parent,string shortcutDirectory,Registration entry)
 {
  Require(boundary.VerifyInitializedLayout()==entry.Identity&&boundary.ProgramRoot==entry.ProgramRoot);
  var directory=boundary.ReviewExistingVersion(entry.Version);
  Require(entry.Executable==Path.Combine(directory,"CRC.PracticeManager.exe")&&entry.Uninstaller==Path.Combine(boundary.ProgramRoot,"CRC.Uninstall.exe"));
  var uninstaller=LocalStoragePath.FilePath(entry.Uninstaller,mustExist:true,rejectSidecars:true);
  Require(WorkspaceConfidentiality.HasWorkspaceFileAcl(uninstaller)&&IsDigest(entry.UninstallerHash)&&IsDigest(entry.ShortcutHash)&&FileHash(uninstaller)==entry.UninstallerHash);
  var path=LocalStoragePath.FilePath(Path.Combine(shortcutDirectory,ShortcutName),mustExist:true,rejectSidecars:true);
  Require(FileHash(path)==entry.ShortcutHash);
  VerifyShortcut(path,entry.Executable,directory);
  VerifyOwnedKey(parent,entry);
 }
 // Caller holds the shared lease and durable removal intent. Never adopt a
 // modified shortcut or registry key; a partial failure is review-only.
 internal static void RemoveOwnedRegistration(InstalledProgramBoundary boundary,RegistryKey parent,string shortcutDirectory,Registration entry)
 {
  VerifyOwnedRegistration(boundary,parent,shortcutDirectory,entry);
  RemoveOwnedKey(parent,entry);
  var shortcut=LocalStoragePath.FilePath(Path.Combine(shortcutDirectory,ShortcutName),mustExist:true,rejectSidecars:true);
  Require(FileHash(shortcut)==entry.ShortcutHash);
  VerifyShortcut(shortcut,entry.Executable,Path.GetDirectoryName(entry.Executable)!);
  Require(FileHash(shortcut)==entry.ShortcutHash);
  File.Delete(shortcut);
  Require(!Exists(shortcut));
 }
}
