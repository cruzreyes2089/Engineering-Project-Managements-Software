using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PracticeManager.Core;
namespace PracticeManager.Persistence;

// Qualification only. The independently pinned signer attests the exact
// recovery export and intended release; no production trust is provisioned.
// This verifies isolated recovery artifacts, not live backup freshness,
// complete migration authorization, or permission to restore over a database.
internal static class InstallerWorkspaceRecoveryReview
{
 internal sealed record Binding(string WorkspaceId,string Commit,string PreviousInventory,string Inventory,
  int SourceSchema,int TargetSchema,string RecoverySha256,long Bytes,long NotBeforeUnix,long ExpiresUnix,
  string PackageSha256,string CoordinatorSha256,string UninstallerSha256);
 static bool Hex(string? value,int length)=>value is not null&&value.Length==length&&value.All(c=>c is >= '0' and <= '9' or >= 'a' and <= 'f');
 static void Require(bool ok)
 {if(!ok)throw new ArgumentException("Workspace recovery evidence requires review.");}
 static string Hash(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
 // Fixed ordered array; sign these exact UTF-8 bytes (no external JSON parsing).
 internal static byte[] CanonicalBytes(Binding binding)=>JsonSerializer.SerializeToUtf8Bytes(new object[]{
  "CRC-Workspace-Recovery-v2",binding.WorkspaceId,binding.Commit,binding.PreviousInventory,binding.Inventory,
  binding.SourceSchema,binding.TargetSchema,binding.RecoverySha256,binding.Bytes,binding.NotBeforeUnix,binding.ExpiresUnix,
  binding.PackageSha256,binding.CoordinatorSha256,binding.UninstallerSha256});
 internal static void VerifySignature(Binding binding,byte[] signature,byte[] keySpki,string independentKeyPin,DateTimeOffset now)
 {
  Require(Guid.TryParseExact(binding.WorkspaceId,"N",out var id)&&id!=Guid.Empty&&id.ToString("N")==binding.WorkspaceId&&
   Hex(binding.Commit,40)&&Hex(binding.PreviousInventory,64)&&Hex(binding.Inventory,64)&&binding.PreviousInventory!=binding.Inventory&&
   binding.SourceSchema is >=47 and <=49&&binding.TargetSchema>=binding.SourceSchema&&binding.TargetSchema<=49&&
   Hex(binding.PackageSha256,64)&&Hex(binding.CoordinatorSha256,64)&&Hex(binding.UninstallerSha256,64)&&
   Hex(binding.RecoverySha256,64)&&binding.Bytes is >=512 and <=4L*1024*1024*1024);
  Require(binding.NotBeforeUnix>=0&&binding.ExpiresUnix<=253402300799L&&binding.ExpiresUnix>binding.NotBeforeUnix&&
   binding.ExpiresUnix-binding.NotBeforeUnix<=86400&&now.ToUnixTimeSeconds()>=binding.NotBeforeUnix&&now.ToUnixTimeSeconds()<binding.ExpiresUnix);
  Require(signature.Length==64&&keySpki.Length is >0 and <=512&&Hex(independentKeyPin,64)&&Hash(keySpki)==independentKeyPin);
  using var key=ECDsa.Create();
  key.ImportSubjectPublicKeyInfo(keySpki,out int consumed);
  Require(consumed==keySpki.Length&&key.KeySize==256&&key.ExportParameters(false).Curve.Oid.Value=="1.2.840.10045.3.1.7");
  Require(key.VerifyData(CanonicalBytes(binding),signature,HashAlgorithmName.SHA256,DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
 }
 // Verify that the independently signed recovery attestation matches the
 // exact detached release evidence, including all executable digests.
 internal static void VerifyReleaseBinding(Binding binding,InstallerReleaseEvidence.Evidence release,DateTimeOffset now)
 {
  InstallerCoordinatorCandidate.Validate(release.Plan);
  Require(release.Plan.Action==InstallerCoordinatorCandidate.Operation.Upgrade&&
   binding.Commit==release.Commit&&binding.PreviousInventory==release.Plan.PreviousVersion&&binding.Inventory==release.Plan.Version&&
   binding.PackageSha256==release.PackageSha256&&binding.CoordinatorSha256==release.CoordinatorSha256&&
   binding.UninstallerSha256==release.Plan.UninstallerSha256&&
   binding.NotBeforeUnix>=release.NotBeforeUnix&&binding.ExpiresUnix<=release.ExpiresUnix&&
   now.ToUnixTimeSeconds()>=release.NotBeforeUnix&&now.ToUnixTimeSeconds()<release.ExpiresUnix);
 }
 internal static Binding ReviewForRelease(Binding binding,byte[] signature,byte[] keySpki,string independentKeyPin,
  InstallerReleaseEvidence.Evidence release,string expectedWorkspace,int expectedTargetSchema,
  ProductionStorageBoundary layout,string backup,string isolatedRestore,DateTimeOffset now)
 {
  VerifyReleaseBinding(binding,release,now);
  return Review(binding,signature,keySpki,independentKeyPin,expectedWorkspace,release.Commit,
   release.Plan.PreviousVersion!,release.Plan.Version,expectedTargetSchema,layout,backup,isolatedRestore,now);
 }
 static void VerifyBytes(FileStream stream,Binding binding)
 {
  stream.Position=0;
  Require(stream.Length==binding.Bytes&&Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant()==binding.RecoverySha256&&stream.Length==binding.Bytes);
 }
 internal static Binding Review(Binding binding,byte[] signature,byte[] keySpki,string independentKeyPin,
  string expectedWorkspace,string expectedCommit,string expectedPrevious,string expectedInventory,int expectedTargetSchema,
  ProductionStorageBoundary layout,string backup,string isolatedRestore,DateTimeOffset now)
 {
  VerifySignature(binding,signature,keySpki,independentKeyPin,now);
  Require(binding.WorkspaceId==expectedWorkspace&&binding.Commit==expectedCommit&&binding.PreviousInventory==expectedPrevious&&
   binding.Inventory==expectedInventory&&binding.TargetSchema==expectedTargetSchema);
  var root=layout.VerifyBackupDestination();
  var source=LocalStoragePath.FilePath(backup,mustExist:true,rejectSidecars:true);
  var restored=LocalStoragePath.FilePath(isolatedRestore,mustExist:true,rejectSidecars:true);
  Require(!string.Equals(source,restored,StringComparison.OrdinalIgnoreCase)&&
   string.Equals(Path.GetDirectoryName(source),root,StringComparison.OrdinalIgnoreCase)&&
   string.Equals(Path.GetDirectoryName(restored),root,StringComparison.OrdinalIgnoreCase)&&
   string.Equals(Path.GetExtension(source),".db",StringComparison.OrdinalIgnoreCase)&&
   string.Equals(Path.GetExtension(restored),".db",StringComparison.OrdinalIgnoreCase)&&
   WorkspaceConfidentiality.HasRestrictedFileAcl(source)&&WorkspaceConfidentiality.HasRestrictedFileAcl(restored));
  using var original=new FileStream(source,FileMode.Open,FileAccess.Read,FileShare.Read);
  using var copy=new FileStream(restored,FileMode.Open,FileAccess.Read,FileShare.Read);
  VerifyBytes(original,binding);VerifyBytes(copy,binding);
  using(var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=restored,Mode=SqliteOpenMode.ReadOnly,Pooling=false,ForeignKeys=true,DefaultTimeout=10}.ToString()))
  {
   db.Open();using var q=db.CreateCommand();
   q.CommandText="PRAGMA trusted_schema=OFF; PRAGMA query_only=ON";q.ExecuteNonQuery();
   long Count(string sql){q.CommandText=sql;return Convert.ToInt64(q.ExecuteScalar());}
   Require(Count("PRAGMA trusted_schema")==0&&Count("PRAGMA query_only")==1&&Count("PRAGMA foreign_keys")==1);
   using var tx=db.BeginTransaction(deferred:true);q.Transaction=tx;
   ProductionWorkspaceIdentityCandidate.VerifyMigrationIdentity(db,tx,expectedWorkspace,layout,binding.SourceSchema,true);
   q.CommandText="PRAGMA integrity_check";using(var rows=q.ExecuteReader())Require(rows.Read()&&rows.GetString(0)=="ok"&&!rows.Read());
   q.CommandText="PRAGMA foreign_key_check";using(var rows=q.ExecuteReader())Require(!rows.Read());
   Require(Count("SELECT count(*) FROM firms WHERE id=1")==1&&
    Count("SELECT count(*) FROM audit_events WHERE action='RecoveryCopyIsolated' AND entity='Firm' AND entity_id=1")>=1&&
    Count("SELECT count(*) FROM accounting_profiles")==0&&
    Count("SELECT count(*) FROM accounting_lifecycle_hold WHERE id=1")==1&&
    Count("SELECT coalesce((SELECT enabled FROM owner_approval_events ORDER BY id DESC LIMIT 1),0)")==0);
   _=LocalPractice.HistoricalArchiveFingerprint(db,tx);
   tx.Commit();
  }
  _=LocalStoragePath.FilePath(source,mustExist:true,rejectSidecars:true);
  _=LocalStoragePath.FilePath(restored,mustExist:true,rejectSidecars:true);
  Require(layout.VerifyBackupDestination()==root);
  VerifyBytes(original,binding);VerifyBytes(copy,binding);
  return binding;
 }
}
