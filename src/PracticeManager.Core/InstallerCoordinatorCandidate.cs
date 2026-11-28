using Microsoft.Win32;
using System.Runtime.Versioning;
using System.Security.Cryptography;

namespace PracticeManager.Core;

// Central entry contract for a future approved installer package. Only exact
// operations; no caller-supplied destination, registry or shortcut paths.
// A local digest/receipt is not independent release authorization.
public static class InstallerCoordinatorCandidate
{
 internal enum Operation { Fresh, Upgrade, Remove }
 internal sealed record ReviewedPlan(Operation Action,string Version,string? PreviousVersion,string UninstallerSha256);
 internal static Operation Parse(IReadOnlyList<string> args)
 {
  if(args.Count!=1)throw new ArgumentException("Choose one exact installer operation.");
  return args[0] switch
  {
   "--install"=>Operation.Fresh,
   "--upgrade"=>Operation.Upgrade,
   "--uninstall"=>Operation.Remove,
   _=>throw new ArgumentException("Unsupported installer operation.")
  };
 }
 // There is no approved release in this revision. This decision cannot be
 // overridden by command line, environment variable, registry or local file.
 static ReviewedPlan? ApprovedRelease(Operation action)=>null;
 public static int Run(IReadOnlyList<string> args)
 {
  try
  {
   var action=Parse(args);
   var plan=ApprovedRelease(action);
   if(plan==null||plan.Action!=action)
   {
    Console.Error.WriteLine("CRC installer is held. No installation changes were made.");
    return 3;
   }
   if(!OperatingSystem.IsWindows())return 2;
   ExecuteReviewedPlan(plan);
   return 0;
  }
  catch(Exception)
  {
   Console.Error.WriteLine("CRC installation requires review. No automatic retry or rollback.");
   return 2;
  }
 }
 static bool Digest(string? value)=>value is {Length:64}&&value.All(c=>c is >= '0' and <= '9' or >= 'a' and <= 'f');
 internal static void Validate(ReviewedPlan plan)
 {
  if(!Digest(plan.Version)||!Digest(plan.UninstallerSha256)||
     (plan.Action==Operation.Upgrade?(!Digest(plan.PreviousVersion)||plan.PreviousVersion==plan.Version):plan.PreviousVersion!=null)||
     !Enum.IsDefined(plan.Action))
   throw new ArgumentException("Exact reviewed installation identity required.");
 }
 static byte[] ReadBoundedInventory(string path)
 {
  var full=LocalStoragePath.FilePath(path,mustExist:true,rejectSidecars:true);
  using var input=new FileStream(full,FileMode.Open,FileAccess.Read,FileShare.Read);
  if(input.Length is <=0 or >2*1024*1024)throw new ArgumentException("Invalid installer inventory.");
  var raw=new byte[(int)input.Length];input.ReadExactly(raw);return raw;
 }
 // Intentionally unreachable until independently approved release evidence is
 // implemented. Production invocation must also verify the executing signed
 // coordinator, extraction ownership and upgrade/backup compatibility.
 [SupportedOSPlatform("windows")]
 static void ExecuteReviewedPlan(ReviewedPlan plan)
 {
  Validate(plan);
  var profile=LocalStoragePath.DirectoryPath(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
  var shortcuts=LocalStoragePath.DirectoryPath(Environment.GetFolderPath(Environment.SpecialFolder.Programs));
  using var hive=RegistryKey.OpenBaseKey(RegistryHive.CurrentUser,RegistryView.Registry64);
  using var parent=hive.OpenSubKey(WindowsRegistrationCandidate.UninstallParent,true)
   ??throw new InvalidOperationException("Windows registration parent unavailable.");
  if(plan.Action==Operation.Remove)
  {
   var boundary=InstalledProgramBoundary.ReviewInitialized(profile);
   var helper=LocalStoragePath.FilePath(Path.Combine(boundary.ProgramRoot,"CRC.Uninstall.exe"),mustExist:true,rejectSidecars:true);
   var self=LocalStoragePath.FilePath(Environment.ProcessPath??throw new InvalidOperationException(),mustExist:true,rejectSidecars:true);
   if(!string.Equals(self,helper,StringComparison.OrdinalIgnoreCase)||!WorkspaceConfidentiality.HasWorkspaceFileAcl(helper))
    throw new ArgumentException("Installed uninstaller identity requires review.");
   using(var input=new FileStream(helper,FileMode.Open,FileAccess.Read,FileShare.Read))
    if(Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant()!=plan.UninstallerSha256)
     throw new ArgumentException("Uninstaller identity requires review.");
   _=InstalledVersionSelectionCandidate.RemoveRegisteredInstallation(boundary,plan.Version,parent,shortcuts);
   return;
  }
  // The package coordinator's private extraction root is its own directory.
  // These exact names cannot be supplied or redirected by command line.
  var extraction=LocalStoragePath.DirectoryPath(AppContext.BaseDirectory);
  if(!WorkspaceConfidentiality.HasRestrictedDirectoryAcl(extraction))throw new ArgumentException("Installer extraction permissions require review.");
  var source=LocalStoragePath.DirectoryPath(Path.Combine(extraction,"payload"));
  var raw=ReadBoundedInventory(Path.Combine(extraction,"inventory.json"));
  if(plan.Action==Operation.Fresh)
  {
   _=InstalledVersionSelectionCandidate.InstallFreshRegistered(profile,plan.Version,source,raw,
    Path.Combine(extraction,"CRC.Uninstall.exe"),plan.UninstallerSha256,parent,shortcuts);
  }
  else
  {
   var boundary=InstalledProgramBoundary.ReviewInitialized(profile);
   var helper=LocalStoragePath.FilePath(Path.Combine(boundary.ProgramRoot,"CRC.Uninstall.exe"),mustExist:true,rejectSidecars:true);
   if(!WorkspaceConfidentiality.HasWorkspaceFileAcl(helper))throw new ArgumentException("Uninstaller permissions require review.");
   using(var input=new FileStream(helper,FileMode.Open,FileAccess.Read,FileShare.Read))
    if(Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant()!=plan.UninstallerSha256)
     throw new ArgumentException("Uninstaller identity requires review.");
   InstalledVersionSelectionCandidate.UpgradeRegisteredInstallation(boundary,plan.PreviousVersion!,plan.Version,source,raw,parent,shortcuts);
  }
 }
}
