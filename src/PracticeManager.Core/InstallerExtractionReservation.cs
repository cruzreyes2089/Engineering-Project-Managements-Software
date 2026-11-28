using System.Text.Json;
namespace PracticeManager.Core;

// Cooperative, validation-only reservation. No process launch, automatic
// cleanup, release authorization or protection against a malicious same-user process.
internal sealed class InstallerExtractionReservation : IDisposable
{
 readonly FileStream lease;
 readonly byte[] binding;
 readonly InstallerReleaseEvidence.Evidence evidence;
 readonly string root;
 bool disposed;
 bool sealedForReview;
 internal string Envelope {get;}
 const string LockName="reservation.lock";
 const string BindingName="reservation.json";
 const string ReadyName="reviewed.json";
 static void Require(bool ok)
 {if(!ok)throw new ArgumentException("Installer staging reservation requires review.");}
 static bool Exists(string path)
 {
  try{_=File.GetAttributes(path);return true;}
  catch(FileNotFoundException){return false;}
  catch(DirectoryNotFoundException){return false;}
 }
 static void WriteNew(string path,byte[] bytes)
 {
  var file=LocalStoragePath.FilePath(path,mustExist:false,mustBeNew:true,rejectSidecars:true);
  using(var stream=new FileStream(file,FileMode.CreateNew,FileAccess.Write,FileShare.None))
  {stream.Write(bytes);stream.Flush(true);}
  Require(WorkspaceConfidentiality.HasWorkspaceFileAcl(file));
 }
 static byte[] ReadBounded(string path)
 {
  var file=LocalStoragePath.FilePath(path,mustExist:true,rejectSidecars:true);
  Require(WorkspaceConfidentiality.HasWorkspaceFileAcl(file));
  using var stream=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.Read);
  Require(stream.Length is >0 and <=16384);
  var bytes=new byte[(int)stream.Length];stream.ReadExactly(bytes);return bytes;
 }
 InstallerExtractionReservation(string root,FileStream lease,byte[] binding,InstallerReleaseEvidence.Evidence evidence)
 {this.root=root;this.lease=lease;this.binding=binding;this.evidence=evidence;Envelope=Path.Combine(root,"envelope");}
 // Caller supplies an already protected, dedicated staging parent. Fixed
 // child name ensures a second reservation cannot silently bypass a leftover.
 internal static InstallerExtractionReservation Create(string protectedParent,InstallerReleaseEvidence.Evidence evidence)
 {
  InstallerCoordinatorCandidate.Validate(evidence.Plan);
  Require(evidence.Plan.Action!=InstallerCoordinatorCandidate.Operation.Remove);
  var parent=LocalStoragePath.DirectoryPath(protectedParent);
  Require(WorkspaceConfidentiality.HasRestrictedDirectoryAcl(parent));
  var root=Path.Combine(parent,"crc-extraction-reservation");
  Require(!Exists(root));
  WorkspaceConfidentiality.EnsureWorkspaceDirectory(root);
  Require(WorkspaceConfidentiality.HasRestrictedDirectoryAcl(root));
  var path=LocalStoragePath.FilePath(Path.Combine(root,LockName),mustExist:false,mustBeNew:true,rejectSidecars:true);
  var lease=new FileStream(path,FileMode.CreateNew,FileAccess.ReadWrite,FileShare.None);
  try
  {
   Require(WorkspaceConfidentiality.HasRestrictedWorkspaceFileHandleAcl(lease));
   lease.Flush(true);
   Require(Directory.EnumerateFileSystemEntries(root).Select(Path.GetFileName).SequenceEqual(new[]{LockName}));
   var binding=JsonSerializer.SerializeToUtf8Bytes(new{format="CRC-Extraction-Reservation-v1",identity=Guid.NewGuid().ToString("N"),evidence});
   Require(binding.Length<=16384);
   WriteNew(Path.Combine(root,BindingName),binding);
   WorkspaceConfidentiality.EnsureWorkspaceDirectory(Path.Combine(root,"envelope"));
   var result=new InstallerExtractionReservation(root,lease,binding,evidence);
   result.CheckReservation(false);
   return result;
  }
  catch{lease.Dispose();throw;}
 }
 void CheckReservation(bool ready)
 {
  ObjectDisposedException.ThrowIf(disposed,this);
  Require(lease.Length==0&&WorkspaceConfidentiality.HasRestrictedWorkspaceFileHandleAcl(lease));
  Require(WorkspaceConfidentiality.HasRestrictedDirectoryAcl(LocalStoragePath.DirectoryPath(root)));
  Require(WorkspaceConfidentiality.HasRestrictedDirectoryAcl(LocalStoragePath.DirectoryPath(Envelope)));
  var names=Directory.EnumerateFileSystemEntries(root).Select(Path.GetFileName).ToArray();
  var expected=ready?new[]{LockName,BindingName,"envelope",ReadyName}:new[]{LockName,BindingName,"envelope"};
  Require(names.Length==expected.Length&&names.ToHashSet(StringComparer.Ordinal).SetEquals(expected));
  Require(ReadBounded(Path.Combine(root,BindingName)).SequenceEqual(binding));
 }
 // Only seal after exact content validation. Failed or interrupted staging
 // remains on disk and cannot be adopted or retried automatically.
 internal InstallerExtractionEvidence.Witness SealForReview()
 {
  Require(!sealedForReview);
  CheckReservation(false);
  var witness=InstallerExtractionEvidence.Review(Envelope,evidence);
  WriteNew(Path.Combine(root,ReadyName),JsonSerializer.SerializeToUtf8Bytes(witness));
  sealedForReview=true;
  return ReviewSealed();
 }
 internal InstallerExtractionEvidence.Witness ReviewSealed()
 {
  Require(sealedForReview);
  CheckReservation(true);
  var witness=InstallerExtractionEvidence.Review(Envelope,evidence);
  Require(ReadBounded(Path.Combine(root,ReadyName)).SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(witness)));
  return witness;
 }
 // Same-process handoff review only; the exclusive reservation stays open.
 // The caller must use separately verified release evidence. A receipt is
 // not a substitute for signature validation or execution authorization.
 internal InstallerExtractionEvidence.Witness ReviewForHandoff(InstallerReleaseEvidence.Evidence expected,DateTimeOffset now)
 {
  Require(expected==evidence&&now.ToUnixTimeSeconds()>=evidence.NotBeforeUnix&&now.ToUnixTimeSeconds()<evidence.ExpiresUnix);
  return ReviewSealed();
 }
 public void Dispose()
 {
  if(disposed)return;
  disposed=true;
  lease.Dispose();
  // Deliberately retain all evidence; no automatic deletion or reopening.
 }
}
