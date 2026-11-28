namespace PracticeManager.Core;

// Read-only, same-process validation candidate. A successful review is NOT
// permission to execute or migrate. Production trust and authorization are
// deliberately absent; caller-supplied inputs here are only qualification data.
internal static class InstallerIntegrationReview
{
 internal sealed record RecoveryInput(InstallerRecoveryBindingCandidate.Binding Binding,string Source,string Rehearsal);
 internal sealed record Report(InstallerCoordinatorCandidate.Operation Operation,string Commit,string Inventory,
  int PayloadFiles,int? SourceSchema,int? TargetSchema);
 static void Require(bool ok)
 {if(!ok)throw new ArgumentException("Integrated installer evidence requires review.");}
 internal static void ValidateScope(InstallerCoordinatorCandidate.Operation operation,RecoveryInput? recovery)
 {
  Require(operation is InstallerCoordinatorCandidate.Operation.Fresh or InstallerCoordinatorCandidate.Operation.Upgrade);
  Require((operation==InstallerCoordinatorCandidate.Operation.Upgrade)==(recovery!=null));
 }
 internal static Report Review(InstallerExtractionReservation reservation,
  InstallerCoordinatorCandidate.Operation operation,byte[] document,byte[] signature,byte[] publicKey,
  string independentlyTrustedKeySha256,string independentlyTrustedPublisherSha256,
  string package,string coordinator,string expectedCommit,DateTimeOffset now,RecoveryInput? recovery)
 {
  ValidateScope(operation,recovery);
  var release=InstallerReleaseEvidence.VerifyArtifacts(document,signature,publicKey,
   independentlyTrustedKeySha256,operation,package,coordinator,expectedCommit,now);
  // Reject inconsistent reservation before inspecting any database evidence.
  var initial=reservation.ReviewForHandoff(release,now);
  if(recovery!=null)
   _=InstallerRecoveryBindingCandidate.Review(release,recovery.Binding,recovery.Source,recovery.Rehearsal,now);
  var final=reservation.ReviewAuthenticHandoff(release,package,coordinator,independentlyTrustedPublisherSha256,now);
  Require(initial==final);
  if(recovery!=null)
   _=InstallerRecoveryBindingCandidate.Review(release,recovery.Binding,recovery.Source,recovery.Rehearsal,now);
  return new(operation,release.Commit,final.Inventory,final.PayloadFiles,recovery?.Binding.SourceSchema,recovery?.Binding.TargetSchema);
 }
}
