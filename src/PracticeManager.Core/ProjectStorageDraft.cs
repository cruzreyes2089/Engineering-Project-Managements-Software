using System.Security.Cryptography;
using System.Text.Json;

namespace PracticeManager.Core;

// A lexical review only; never resolve, inspect, open, persist or create directories.
public sealed record ProjectStorageDraft(string ProductionRoot,string RestrictedRoot,string ProjectNumber,
 string TemplateRevision,int ProductionDirectories,int RestrictedDirectories,string Fingerprint)
{
 public bool Registered=>false;
 public bool FilesystemVerified=>false;
 public bool ProvisioningAuthorized=>false;
 public override string ToString()=>"[Unregistered project storage draft]";
}
public static class ProjectStorageDrafting
{
 static readonly HashSet<string> Reserved=new(StringComparer.OrdinalIgnoreCase)
 {
  "CON","PRN","AUX","NUL","CONIN$","CONOUT$",
  "COM0","COM1","COM2","COM3","COM4","COM5","COM6","COM7","COM8","COM9",
  "LPT0","LPT1","LPT2","LPT3","LPT4","LPT5","LPT6","LPT7","LPT8","LPT9",
  "COM¹","COM²","COM³","LPT¹","LPT²","LPT³"
 };
 static string Root(string value)
 {
  if(string.IsNullOrEmpty(value)||value.Length>180||value.Length<4||
     !char.IsAsciiLetter(value[0])||value[1]!=':'||value[2]!='\\')
   throw new ArgumentException("Use an absolute drive-letter path to a dedicated directory, not a drive root or network path.");
  var parts=value[3..].Split('\\');
  foreach(var part in parts)ValidateComponent(part);
  return char.ToUpperInvariant(value[0])+value[1..];
 }
 internal static void ValidateComponent(string part)
 {
  if(string.IsNullOrEmpty(part)||part.Length>100||part!=part.Trim()||part.EndsWith('.')||
     part.Any(c=>char.IsControl(c)||c is '/' or '\\' or ':' or '<' or '>' or '"' or '|' or '?' or '*' or '~')||
     Reserved.Contains(part.Split('.')[0].TrimEnd()))
   throw new ArgumentException("Unsupported directory name or ambiguous path syntax.");
 }
 static bool Contains(string a,string b)=>string.Equals(a,b,StringComparison.OrdinalIgnoreCase)||
  b.StartsWith(a+"\\",StringComparison.OrdinalIgnoreCase);
 public static (string Production,string Restricted) ValidateRoots(string productionRoot,string restrictedRoot)
 {
  var p=Root(productionRoot);var r=Root(restrictedRoot);
  if(Contains(p,r)||Contains(r,p))throw new ArgumentException("Production and restricted directories must be separate, nonnested roots.");
  return(p,r);
 }
 public static ProjectStorageDraft Build(string productionRoot,string restrictedRoot,long projectId,string projectNumber,
  bool includeCorrespondence,ProjectFolderLayout? layout=null)
 {
  var (p,r)=ValidateRoots(productionRoot,restrictedRoot);
  var plan=ProjectFolderPlanning.Build(projectId,projectNumber,includeCorrespondence,true,layout);
  foreach(var entry in plan.Entries)
  {
   var root=entry.Boundary=="Production"?p:r;
   if(root.Length+1+entry.RelativeDirectory.Length>240)
    throw new ArgumentException("A planned directory would exceed the conservative path-length limit.");
  }
  var fingerprint=Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new{
   production=p,restricted=r,plan=plan.Fingerprint}))).ToLowerInvariant();
  return new(p,r,projectNumber,plan.TemplateRevision,plan.Entries.Count(x=>x.Boundary=="Production"),
   plan.Entries.Count(x=>x.Boundary=="Restricted"),fingerprint);
 }
}
