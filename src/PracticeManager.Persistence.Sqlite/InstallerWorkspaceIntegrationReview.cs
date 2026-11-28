using PracticeManager.Core;
namespace PracticeManager.Persistence;

// Composes release, synthetic migration rehearsal and independent workspace
// recovery export evidence in one read-only review. No execution or migration
// authorization, no trust provisioning and no active-workspace writes.
internal static class InstallerWorkspaceIntegrationReview
{
 internal static InstallerIntegrationReview.Report ReviewUpgrade(InstallerExtractionReservation reservation,
  byte[] document,byte[] signature,byte[] publicKey,string releaseKeyPin,string publisherPin,
  string package,string coordinator,string commit,DateTimeOffset now,
  InstallerIntegrationReview.RecoveryInput migration,
  InstallerWorkspaceRecoveryReview.Binding recovery,byte[] recoverySignature,byte[] recoveryKey,string recoveryKeyPin,
  string expectedWorkspace,int expectedSourceSchema,int expectedTargetSchema,
  ProductionStorageBoundary layout,string backup,string isolatedRestore)
 {
  // Require separate independently provisioned release and recovery signer pins.
  if(releaseKeyPin==recoveryKeyPin||migration.Binding.SourceSchema!=expectedSourceSchema||
     migration.Binding.TargetSchema!=expectedTargetSchema||recovery.SourceSchema!=expectedSourceSchema)
   throw new ArgumentException("Independent upgrade and workspace recovery evidence required.");
  var release=InstallerReleaseEvidence.VerifyArtifacts(document,signature,publicKey,releaseKeyPin,
   InstallerCoordinatorCandidate.Operation.Upgrade,package,coordinator,commit,now);
  _=reservation.ReviewForHandoff(release,now);
  _=InstallerWorkspaceRecoveryReview.ReviewForRelease(recovery,recoverySignature,recoveryKey,recoveryKeyPin,release,
   expectedWorkspace,expectedTargetSchema,layout,backup,isolatedRestore,now);
  var report=InstallerIntegrationReview.Review(reservation,InstallerCoordinatorCandidate.Operation.Upgrade,document,signature,publicKey,
   releaseKeyPin,publisherPin,package,coordinator,commit,now,migration);
  _=InstallerWorkspaceRecoveryReview.ReviewForRelease(recovery,recoverySignature,recoveryKey,recoveryKeyPin,release,
   expectedWorkspace,expectedTargetSchema,layout,backup,isolatedRestore,now);
  if(report.Commit!=release.Commit||report.Inventory!=release.Plan.Version||report.SourceSchema!=expectedSourceSchema||
     report.TargetSchema!=expectedTargetSchema)throw new ArgumentException("Upgrade review reconciliation failed.");
  return report;
 }
}
