using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;
using PracticeManager.Core;
namespace PracticeManager.Persistence;

// Point-in-time metadata inspection. Never creates, writes, enumerates or opens file contents.
public sealed record ProjectStorageObservation(long BindingRevision,string Status,int Planned,int Missing,int Existing,int Blocked,string? Fingerprint)
{
 public bool FilesystemVerified=>false;
 public bool EffectiveAccessQualified=>false;
 public bool ProvisioningAuthorized=>false;
 public override string ToString()=>"[Read-only storage observation]";
}
internal static class WindowsStorageObservation
{
 const uint ReadAttributes=0x80,ShareAll=7,OpenExisting=3,BackupSemantics=0x02000000,OpenReparsePoint=0x00200000;
 const uint DirectoryAttribute=0x10,ReparseAttribute=0x400;
 [StructLayout(LayoutKind.Sequential)]
 struct Information
 {
  public uint Attributes,CreationLow,CreationHigh,AccessLow,AccessHigh,WriteLow,WriteHigh;
  public uint Volume,SizeHigh,SizeLow,Links,IndexHigh,IndexLow;
 }
 [DllImport("kernel32.dll",EntryPoint="CreateFileW",CharSet=CharSet.Unicode,SetLastError=true,ExactSpelling=true)]
 static extern SafeFileHandle CreateFile(string path,uint access,uint share,IntPtr security,uint disposition,uint flags,IntPtr template);
 [DllImport("kernel32.dll",SetLastError=true)]
 [return:MarshalAs(UnmanagedType.Bool)]
 static extern bool GetFileInformationByHandle(SafeFileHandle handle,out Information information);
 sealed record Item(string State,string? Identity);
 static Item Inspect(string path)
 {
  using var handle=CreateFile(path,ReadAttributes,ShareAll,IntPtr.Zero,OpenExisting,BackupSemantics|OpenReparsePoint,IntPtr.Zero);
  if(handle.IsInvalid)
  {
   var error=Marshal.GetLastWin32Error();
   return new(error is 2 or 3?"Missing":"Unavailable",null);
  }
  if(!GetFileInformationByHandle(handle,out var info))return new("Unavailable",null);
  if((info.Attributes&ReparseAttribute)!=0)return new("Redirected",null);
  if((info.Attributes&DirectoryAttribute)==0)return new("FileCollision",null);
  if(info.IndexHigh==0&&info.IndexLow==0)return new("IdentityUnavailable",null);
  return new("Directory",$"{info.Volume:x8}:{info.IndexHigh:x8}{info.IndexLow:x8}");
 }
 sealed record RootResult(string State,IReadOnlyList<string> Chain);
 static RootResult InspectRoot(string path)
 {
  // Conservative first qualification: local fixed NTFS volume only; no UNC or cloud support claim.
  var drive=new DriveInfo(path[..3]);
  if(drive.DriveType!=DriveType.Fixed||!string.Equals(drive.DriveFormat,"NTFS",StringComparison.OrdinalIgnoreCase))
   return new("UnsupportedVolume",Array.Empty<string>());
  var chain=new List<string>();
  var current=path[..3];
  foreach(var part in new[]{""}.Concat(path[3..].Split('\\')))
  {
   if(part.Length!=0)current=Path.Combine(current,part);
   var item=Inspect(current);
   if(item.State!="Directory")return new(item.State,chain.AsReadOnly());
   if(chain.Contains(item.Identity!,StringComparer.Ordinal))return new("Alias",chain.AsReadOnly());
   chain.Add(item.Identity!);
  }
  return new("Directory",chain.AsReadOnly());
 }
 public static ProjectStorageObservation Read(ProjectStorageBinding binding,ProjectFolderPlan plan)
 {
  ProjectStorageObservation Stop(string reason)=>new(binding.Revision,reason,plan.Entries.Count,0,0,plan.Entries.Count,null);
  if(!OperatingSystem.IsWindows())return Stop("WindowsRequired");
  if(!binding.CurrentSelection||!binding.CurrentPreference)return Stop("AssignmentReviewRequired");
  try
  {
   var (p,r)=ProjectStorageDrafting.ValidateRoots(binding.ProductionRoot,binding.RestrictedRoot);
   var prod=InspectRoot(p);var restricted=InspectRoot(r);
   if(prod.State!="Directory")return Stop("ProductionRoot:"+prod.State);
   if(restricted.State!="Directory")return Stop("RestrictedRoot:"+restricted.State);
   if(prod.Chain.Contains(restricted.Chain[^1],StringComparer.Ordinal)||restricted.Chain.Contains(prod.Chain[^1],StringComparer.Ordinal))
    return Stop("OverlappingRootIdentity");
   var states=new Dictionary<string,Item>(StringComparer.OrdinalIgnoreCase);
   var identities=new HashSet<string>(StringComparer.Ordinal){prod.Chain[^1],restricted.Chain[^1]};
   var missing=0;var existing=0;var blocked=0;
   foreach(var entry in plan.Entries)
   {
    var path=entry.RelativeDirectory;
    var key=entry.Boundary+"/"+path;
    var last=path.LastIndexOf('/');
    Item item;
    if(last>=0&&states[entry.Boundary+"/"+path[..last]].State!="Directory")
     item=new(states[entry.Boundary+"/"+path[..last]].State=="Missing"?"Missing":"ParentBlocked",null);
    else
    {
     var root=entry.Boundary=="Production"?p:r;
     item=Inspect(Path.Combine(root,Path.Combine(path.Split('/'))));
     if(item.State=="Directory"&&!identities.Add(item.Identity!))item=new("Alias",null);
    }
    states.Add(key,item);
    if(item.State=="Directory")existing++;else if(item.State=="Missing")missing++;else blocked++;
   }
   // Re-observe roots after the bounded walk. This detects some changes, not all path races.
   var finalProd=InspectRoot(p);var finalRestricted=InspectRoot(r);
   if(finalProd.State!="Directory"||finalRestricted.State!="Directory"||
      !prod.Chain.SequenceEqual(finalProd.Chain)||!restricted.Chain.SequenceEqual(finalRestricted.Chain))
    return Stop("RootChanged");
   var hash=Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new{
    binding.Revision,binding.DraftFingerprint,plan.Fingerprint,prod.Chain,restrictedChain=restricted.Chain,states}))).ToLowerInvariant();
   return new(binding.Revision,blocked>0?"PathBlocked":existing>0?"ExistingDirectoriesReviewRequired":"NoExistingProjectDirectories",
    plan.Entries.Count,missing,existing,blocked,hash);
  }
  catch(IOException){return Stop("MetadataUnavailable");}
  catch(UnauthorizedAccessException){return Stop("MetadataUnavailable");}
  catch(ArgumentException){return Stop("MetadataUnavailable");}
 }
}
