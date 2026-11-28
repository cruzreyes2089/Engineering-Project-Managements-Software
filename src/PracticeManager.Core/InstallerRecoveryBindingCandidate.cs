using System.Buffers.Binary;
using System.Security.Cryptography;
namespace PracticeManager.Core;

// Read-only integration candidate. The source is a closed synthetic snapshot,
// not a live database or an isolated RecoveryCopy export. A match is evidence
// for review, never permission to install, migrate, restore or execute.
internal static class InstallerRecoveryBindingCandidate
{
 internal sealed record Binding(string Commit,string PreviousInventory,string Inventory,int SourceSchema,int TargetSchema,string SourceSha256,string RehearsalSha256);
 static bool Hex(string? text,int size)=>text is not null&&text.Length==size&&text.All(c=>c is >= '0' and <= '9' or >= 'a' and <= 'f');
 static void Require(bool ok)
 {if(!ok)throw new ArgumentException("Installer recovery evidence requires review.");}
 static string Observe(FileStream stream,int schema)
 {
  var length=stream.Length;
  Require(length is >=512 and <=4L*1024*1024*1024);
  var header=new byte[100];stream.ReadExactly(header);
  Require(header.AsSpan(0,16).SequenceEqual("SQLite format 3\0"u8));
  Require(BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(60,4))==schema);
  // Refuse production/recovery application IDs: this route is for ordinary
  // synthetic fixtures only, not the production identity overlay.
  Require(BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(68,4))==0);
  stream.Position=0;
  var hash=Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
  Require(stream.Length==length);
  return hash;
 }
 internal static Binding Review(InstallerReleaseEvidence.Evidence release,Binding independentlyRetained,
  string syntheticSource,string syntheticRehearsal,DateTimeOffset now)
 {
  InstallerCoordinatorCandidate.Validate(release.Plan);
  Require(release.Plan.Action==InstallerCoordinatorCandidate.Operation.Upgrade);
  Require(now.ToUnixTimeSeconds()>=release.NotBeforeUnix&&now.ToUnixTimeSeconds()<release.ExpiresUnix);
  Require(Hex(independentlyRetained.Commit,40)&&independentlyRetained.Commit==release.Commit&&
   independentlyRetained.PreviousInventory==release.Plan.PreviousVersion&&independentlyRetained.Inventory==release.Plan.Version);
  Require(independentlyRetained.SourceSchema is >=47 and <=49&&
   independentlyRetained.TargetSchema>=independentlyRetained.SourceSchema&&independentlyRetained.TargetSchema<=49&&
   Hex(independentlyRetained.SourceSha256,64)&&Hex(independentlyRetained.RehearsalSha256,64));
  var source=LocalStoragePath.FilePath(syntheticSource,mustExist:true,rejectSidecars:true);
  var rehearsal=LocalStoragePath.FilePath(syntheticRehearsal,mustExist:true,rejectSidecars:true);
  Require(!ProjectFolderPolicy.Overlaps(Path.GetDirectoryName(source)!,Path.GetDirectoryName(rehearsal)!));
  Require(WorkspaceConfidentiality.HasWorkspaceFileAcl(source)&&WorkspaceConfidentiality.HasWorkspaceFileAcl(rehearsal));
  using var original=new FileStream(source,FileMode.Open,FileAccess.Read,FileShare.Read);
  using var candidate=new FileStream(rehearsal,FileMode.Open,FileAccess.Read,FileShare.Read);
  Require(Observe(original,independentlyRetained.SourceSchema)==independentlyRetained.SourceSha256&&
   Observe(candidate,independentlyRetained.TargetSchema)==independentlyRetained.RehearsalSha256);
  _=LocalStoragePath.FilePath(source,mustExist:true,rejectSidecars:true);
  _=LocalStoragePath.FilePath(rehearsal,mustExist:true,rejectSidecars:true);
  return independentlyRetained;
 }
}
