using System.Security.Cryptography;
using System.Text.Json;

namespace PracticeManager.Core;

// Detached signature verification only. An independently trusted key pin
// must be provisioned by a reviewed release process, not by the package or
// command line. No key is provisioned and no release is authorized today.
// External evidence avoids a self-referential package or uninstaller hash.
internal static class InstallerReleaseEvidence
{
 internal sealed record Evidence(InstallerCoordinatorCandidate.ReviewedPlan Plan,string Commit,string PackageSha256,
  string CoordinatorSha256,long NotBeforeUnix,long ExpiresUnix);
 static bool Hex(string? value,int length)=>value!=null&&value.Length==length&&value.All(c=>c is >= '0' and <= '9' or >= 'a' and <= 'f');
 static void Require(bool ok)
 {if(!ok)throw new ArgumentException("Detached installer evidence requires review.");}
 static string Hash(byte[] value)=>Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();
 static string ReadString(JsonElement root,string name)
 {
  var value=root.GetProperty(name);Require(value.ValueKind==JsonValueKind.String);
  return value.GetString()!;
 }
 static long ReadLong(JsonElement root,string name)
 {
  var value=root.GetProperty(name);
  if(value.ValueKind!=JsonValueKind.Number||!value.TryGetInt64(out var result))throw new ArgumentException("Invalid evidence time.");
  return result;
 }
 internal static Evidence Verify(byte[] document,byte[] signature,byte[] publicKeySpki,string independentlyTrustedKeySha256,
  InstallerCoordinatorCandidate.Operation requestedOperation,string observedPackageSha256,string observedCoordinatorSha256,
  string expectedCommit,DateTimeOffset now)
 {
  Require(document.Length is >0 and <=16384&&signature.Length==64&&publicKeySpki.Length is >0 and <=512);
  Require(Hex(independentlyTrustedKeySha256,64)&&Hash(publicKeySpki)==independentlyTrustedKeySha256);
  // Verify the exact raw bytes; never reserialize the signed object.
  using var key=ECDsa.Create();
  key.ImportSubjectPublicKeyInfo(publicKeySpki,out int consumed);
  Require(consumed==publicKeySpki.Length&&key.KeySize==256&&key.ExportParameters(false).Curve.Oid.Value=="1.2.840.10045.3.1.7");
  Require(key.VerifyData(document,signature,HashAlgorithmName.SHA256,DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
  using var json=JsonDocument.Parse(document,new JsonDocumentOptions{MaxDepth=4});
  var root=json.RootElement;
  Require(root.ValueKind==JsonValueKind.Object);
  var names=root.EnumerateObject().Select(x=>x.Name).ToArray();
  string[] expected=["format","product","operation","commit","inventory","previousInventory","uninstallerSha256","packageSha256","coordinatorSha256","notBeforeUnix","expiresUnix"];
  Require(names.Length==expected.Length&&names.ToHashSet(StringComparer.Ordinal).SetEquals(expected));
  Require(ReadString(root,"format")=="CRC-Detached-Installer-Evidence-v1"&&ReadString(root,"product")=="CRC.PracticeManager.win-x64");
  var action=InstallerCoordinatorCandidate.Parse([ReadString(root,"operation")]);
  Require(action==requestedOperation);
  var previous=root.GetProperty("previousInventory");
  Require(previous.ValueKind is JsonValueKind.Null or JsonValueKind.String);
  var plan=new InstallerCoordinatorCandidate.ReviewedPlan(action,ReadString(root,"inventory"),
   previous.ValueKind==JsonValueKind.Null?null:previous.GetString(),ReadString(root,"uninstallerSha256"));
  InstallerCoordinatorCandidate.Validate(plan);
  var commit=ReadString(root,"commit");var package=ReadString(root,"packageSha256");var coordinator=ReadString(root,"coordinatorSha256");
  Require(Hex(commit,40)&&commit==expectedCommit&&Hex(package,64)&&package==observedPackageSha256&&Hex(coordinator,64)&&coordinator==observedCoordinatorSha256);
  var start=ReadLong(root,"notBeforeUnix");var end=ReadLong(root,"expiresUnix");var current=now.ToUnixTimeSeconds();
  // Limit validity to 30 days and avoid arithmetic overflow.
  Require(start>=0&&end<=253402300799L&&end>start&&end-start<=30L*86400&&current>=start&&current<end);
  return new(plan,commit,package,coordinator,start,end);
 }
}
