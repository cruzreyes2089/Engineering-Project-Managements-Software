using System.Security.Cryptography;
namespace PracticeManager.Core;

// Read-only validation of a future protected extraction. Not a reservation,
// execution lease, Authenticode check or release authorization.
internal static class InstallerExtractionEvidence
{
 internal sealed record Witness(string Inventory,string HelperSha256,int PayloadFiles);
 static void Require(bool ok)
 {if(!ok)throw new ArgumentException("Installer extraction requires review.");}
 static byte[] ReadInventory(string path)
 {
  var file=LocalStoragePath.FilePath(path,mustExist:true,rejectSidecars:true);
  Require(WorkspaceConfidentiality.HasWorkspaceFileAcl(file));
  using var stream=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.Read);
  Require(stream.Length is >0 and <=2*1024*1024);
  var bytes=new byte[(int)stream.Length];stream.ReadExactly(bytes);return bytes;
 }
 internal static Witness Review(string extraction,InstallerReleaseEvidence.Evidence evidence)
 {
  InstallerCoordinatorCandidate.Validate(evidence.Plan);
  Require(evidence.Plan.Action!=InstallerCoordinatorCandidate.Operation.Remove);
  var root=LocalStoragePath.DirectoryPath(extraction);
  Require(WorkspaceConfidentiality.HasRestrictedDirectoryAcl(root));
  // Only this fixed envelope is accepted; the coordinator executable lives
  // outside it. This method never creates or cleans an extraction directory.
  void Census()
  {
   var names=Directory.EnumerateFileSystemEntries(root).Select(Path.GetFileName).ToArray();
   Require(names.Length==3&&names.ToHashSet(StringComparer.Ordinal).SetEquals(
    new[]{"payload","inventory.json","CRC.Uninstall.exe"}));
  }
  Census();
  var payload=LocalStoragePath.DirectoryPath(Path.Combine(root,"payload"));
  Require(WorkspaceConfidentiality.HasRestrictedDirectoryAcl(payload));
  var manifest=ReadInventory(Path.Combine(root,"inventory.json"));
  var verified=InstalledPayloadVerification.ReviewTree(payload,manifest,evidence.Plan.Version);
  foreach(var dir in verified.Subdirectories)
   Require(WorkspaceConfidentiality.HasRestrictedDirectoryAcl(LocalStoragePath.DirectoryPath(Path.Combine(payload,dir.Replace('/',Path.DirectorySeparatorChar)))));
  foreach(var entry in verified.Files)
   Require(WorkspaceConfidentiality.HasWorkspaceFileAcl(LocalStoragePath.FilePath(Path.Combine(payload,entry.Key.Replace('/',Path.DirectorySeparatorChar)),mustExist:true,rejectSidecars:true)));
  var helper=LocalStoragePath.FilePath(Path.Combine(root,"CRC.Uninstall.exe"),mustExist:true,rejectSidecars:true);
  Require(WorkspaceConfidentiality.HasWorkspaceFileAcl(helper));
  using(var stream=new FileStream(helper,FileMode.Open,FileAccess.Read,FileShare.Read))
  {
   var length=stream.Length;
   Require(length is >0 and <=250L*1024*1024);
   Require(Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant()==evidence.Plan.UninstallerSha256&&stream.Length==length);
  }
  Require(ReadInventory(Path.Combine(root,"inventory.json")).SequenceEqual(manifest));
  Census();
  return new(evidence.Plan.Version,evidence.Plan.UninstallerSha256,verified.Files.Count);
 }
}
