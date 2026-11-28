using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PracticeManager.Core;

// Runtime byte verification candidate. The expected inventory digest must
// come from separately trusted release evidence, not from the installed files.
// Trusted quiescent local filesystem only; no signature or race-proof identity claim.
public static class InstalledPayloadVerification
{
 const int MaxManifest=2*1024*1024;
 static readonly string[] Required=["CRC.PracticeManager.exe","CRC.PracticeManager.dll",
  "CRC.PracticeManager.deps.json","CRC.PracticeManager.runtimeconfig.json",
  "PracticeManager.Core.dll","PracticeManager.Persistence.Sqlite.dll","Microsoft.Data.Sqlite.dll",
  "e_sqlite3.dll","SQLitePCLRaw.core.dll","SQLitePCLRaw.provider.e_sqlite3.dll",
  "SQLitePCLRaw.batteries_v2.dll","hostfxr.dll","hostpolicy.dll","coreclr.dll",
  "PresentationFramework.dll","WindowsBase.dll"];
 static readonly string[] Extra=["createdump.exe","CRC.PracticeManager.pdb",
  "PracticeManager.Core.pdb","PracticeManager.Persistence.Sqlite.pdb"];
 static void Require(bool ok){if(!ok)throw new ArgumentException("Installed program inventory or file contents require review.");}
 static void Shape(JsonElement obj,params string[] names)
 {
  Require(obj.ValueKind==JsonValueKind.Object);
  var actual=obj.EnumerateObject().Select(x=>x.Name).ToArray();
  Require(actual.Length==names.Length&&actual.ToHashSet(StringComparer.Ordinal).SetEquals(names));
 }
 static bool Digest(string? value)=>value is {Length:64}&&value.All(c=>c is >= '0' and <= '9' or >= 'a' and <= 'f');
 static string Hash(Stream stream)=>Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
 static string ReadString(JsonElement obj,string name)
 {
  var value=obj.GetProperty(name);Require(value.ValueKind==JsonValueKind.String);return value.GetString()!;
 }
 static long ReadLong(JsonElement obj,string name)
 {
  var value=obj.GetProperty(name);
  if(value.ValueKind!=JsonValueKind.Number||!value.TryGetInt64(out var n))throw new ArgumentException("Invalid inventory number.");
  return n;
 }
 internal sealed record VerifiedPayload(string Directory,IReadOnlyDictionary<string,(long Bytes,string Hash)> Files,IReadOnlyList<string> Subdirectories);
 public static void Verify(InstalledProgramBoundary boundary,string expectedDigest)=>_=Review(boundary,expectedDigest);
 internal static VerifiedPayload Review(InstalledProgramBoundary boundary,string expectedDigest)
 {
  var version=boundary.ReviewExistingVersion(expectedDigest);
  var manifests=LocalStoragePath.DirectoryPath(Path.Combine(boundary.ProgramRoot,"manifests"));
  Require(WorkspaceConfidentiality.HasRestrictedDirectoryAcl(manifests));
  var manifest=LocalStoragePath.FilePath(Path.Combine(manifests,expectedDigest+".json"),mustExist:true,rejectSidecars:true);
  Require(WorkspaceConfidentiality.HasWorkspaceFileAcl(manifest));
  byte[] raw;
  using(var stream=new FileStream(manifest,FileMode.Open,FileAccess.Read,FileShare.Read))
  {
   Require(stream.Length is >0 and <=MaxManifest);
   raw=new byte[(int)stream.Length];stream.ReadExactly(raw);
  }
  return ReviewTree(version,raw,expectedDigest);
 }
 // Shared exact-tree verifier for an isolated source and installed destination.
 internal static VerifiedPayload ReviewTree(string version,byte[] raw,string expectedDigest)
 {
  version=LocalStoragePath.DirectoryPath(version);
  Require(raw.Length is >0 and <=MaxManifest&&Digest(expectedDigest));
  Require(Convert.ToHexString(SHA256.HashData(raw)).ToLowerInvariant()==expectedDigest);
  using var json=JsonDocument.Parse(raw,new JsonDocumentOptions{MaxDepth=16});
  var root=json.RootElement;
  Shape(root,"format","architecture","fileCount","bytes","files");
  Require(ReadString(root,"format")=="CRC-Development-Build-Inventory-v1"&&ReadString(root,"architecture")=="win-x64");
  var files=root.GetProperty("files");
  Require(files.ValueKind==JsonValueKind.Array&&files.GetArrayLength() is >0 and <=2000&&files.GetArrayLength()==ReadLong(root,"fileCount"));
  var rows=new Dictionary<string,(long Bytes,string Hash)>(StringComparer.Ordinal);
  var dirs=new HashSet<string>(StringComparer.Ordinal);
  var aliases=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
  long total=0;
  foreach(var row in files.EnumerateArray())
  {
   Shape(row,"path","bytes","sha256");
   var name=ReadString(row,"path");var parts=name.Split('/');
   Require(name.Length is >0 and <=180&&parts.Length<=8);
   for(int i=0;i<parts.Length;i++)
   {
    var part=parts[i];
    Require(Regex.IsMatch(part,@"\A[A-Za-z0-9_.+-]{1,100}\z")&&part!="."&&part!=".."&&!part.EndsWith('.')&&!LocalStoragePath.IsReservedWindowsName(part));
    var prefix=string.Join("/",parts.Take(i+1));
    if(aliases.TryGetValue(prefix,out var old))Require(old==prefix);else aliases.Add(prefix,prefix);
    if(i<parts.Length-1)dirs.Add(prefix);
   }
   var bytes=ReadLong(row,"bytes");var hash=ReadString(row,"sha256");
   Require(bytes is >0 and <=250L*1024*1024&&Digest(hash));
   Require(Required.Contains(name,StringComparer.Ordinal)||Extra.Contains(name,StringComparer.Ordinal)||name.EndsWith(".dll",StringComparison.Ordinal));
   Require(rows.TryAdd(name,(bytes,hash)));
   total+=bytes;Require(total<=750L*1024*1024);
  }
  Require(total==ReadLong(root,"bytes")&&Required.All(rows.ContainsKey)&&!rows.Keys.Any(dirs.Contains));
  void Census()
  {
   var found=new HashSet<string>(StringComparer.Ordinal);
   var pending=new Stack<(string Path,string Relative)>();pending.Push((version,""));
   int entries=0;
   while(pending.Count>0)
   {
    var current=pending.Pop();
    LocalStoragePath.DirectoryPath(current.Path);
    foreach(var item in Directory.EnumerateFileSystemEntries(current.Path))
    {
     Require(++entries<=16000);
     var relative=current.Relative+Path.GetFileName(item);
     var attr=File.GetAttributes(item);
     Require((attr&FileAttributes.ReparsePoint)==0);
     if((attr&FileAttributes.Directory)!=0)
     {Require(dirs.Contains(relative));pending.Push((item,relative+"/"));}
     else{Require(rows.ContainsKey(relative)&&found.Add(relative));}
    }
   }
   Require(found.SetEquals(rows.Keys));
  }
  Census();
  foreach(var pair in rows)
  {
   var path=LocalStoragePath.FilePath(Path.Combine(version,pair.Key.Replace('/',Path.DirectorySeparatorChar)),mustExist:true,rejectSidecars:true);
   using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
   Require(stream.Length==pair.Value.Bytes&&Hash(stream)==pair.Value.Hash&&stream.Length==pair.Value.Bytes);
  }
  Census();
  return new(version,rows,dirs.OrderByDescending(x=>x.Count(c=>c=='/')).ThenBy(x=>x,StringComparer.Ordinal).ToArray());
 }
}
