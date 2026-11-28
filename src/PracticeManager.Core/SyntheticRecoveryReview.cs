using System.Text.RegularExpressions;
namespace PracticeManager.Core;

// Offline evidence classification only. No live provider, network, journal mutation,
// approval, recovery release or replay authority. Caller-provided evidence claims are
// not authentication and must never be treated as such by a future live adapter.
public sealed record SyntheticRecoveryTarget(Guid OperationKey,Guid MarkerKey,RehearsalKind Kind,string Reference,string Fingerprint,Guid? KnownExternalKey=null)
{
 public override string ToString()=>"[Synthetic recovery target]";
}
public sealed record SyntheticRecoveryCandidate(Guid ExternalKey,RehearsalKind Kind,string? DetailReference,string? Fingerprint)
{
 public override string ToString()=>"[Synthetic recovery candidate]";
}
public sealed record SyntheticRecoverySweep(Guid MarkerKey,bool Complete,bool IndependentEvidenceVerified,IReadOnlyList<SyntheticRecoveryCandidate> Candidates)
{
 public override string ToString()=>"[Synthetic recovery sweep]";
}
public enum SyntheticRecoveryFinding { ReviewRequired,NoMatchObserved,Conflict,MatchedForReview }
public sealed record SyntheticRecoveryAssessment(SyntheticRecoveryFinding Finding,string Reason)
{
 // None of these findings permits a retry or release, including a perfect match.
 public bool MayRetry=>false;
 public bool MayReleaseHold=>false;
}
public static class SyntheticRecoveryReview
{
 private const int Limit=200;
 private static bool Hash(string? value)=>value!=null&&Regex.IsMatch(value,@"\A[0-9A-F]{64}\z",RegexOptions.CultureInvariant);
 // A deterministic operation reference is a proposed local correlation convention,
 // not a claim that Manager accepts its length or provides uniqueness/idempotency.
 public static string ReferenceFor(Guid operation)=>operation==Guid.Empty?throw new ArgumentException("Missing operation identity."):"CRC-DEV-"+operation.ToString("N").ToUpperInvariant();
 public static SyntheticRecoveryAssessment Evaluate(SyntheticRecoveryTarget target,SyntheticRecoverySweep first,SyntheticRecoverySweep second)
 {
  static SyntheticRecoveryAssessment Review(string reason)=>new(SyntheticRecoveryFinding.ReviewRequired,reason);
  static SyntheticRecoveryAssessment Conflict(string reason)=>new(SyntheticRecoveryFinding.Conflict,reason);
  if(target.OperationKey==Guid.Empty||target.MarkerKey==Guid.Empty||!Enum.IsDefined(target.Kind)||!Hash(target.Fingerprint)||
     target.Reference!=ReferenceFor(target.OperationKey)||target.KnownExternalKey==Guid.Empty)return Review("InvalidTarget");
  if(first.MarkerKey!=target.MarkerKey||second.MarkerKey!=target.MarkerKey)return Conflict("MarkerMismatch");
  if(!first.Complete||!second.Complete||!first.IndependentEvidenceVerified||!second.IndependentEvidenceVerified)return Review("IncompleteEvidence");
  // A complete sweep here means complete across all three fixture document kinds.
  // Bounds are deliberately small; a partial page is never evidence of absence.
  bool Valid(SyntheticRecoverySweep s)=>s.Candidates!=null&&s.Candidates.Count<=Limit&&
   s.Candidates.All(c=>c!=null&&c.ExternalKey!=Guid.Empty&&Enum.IsDefined(c.Kind)&&c.DetailReference!=null&&
    c.DetailReference.Length<=200&&!c.DetailReference.Any(char.IsControl)&&Hash(c.Fingerprint))&&
   s.Candidates.Select(c=>c.ExternalKey).Distinct().Count()==s.Candidates.Count;
  if(!Valid(first)||!Valid(second))return Review("InvalidOrIncompleteDetails");
  var a=first.Candidates.OrderBy(c=>c.ExternalKey).ToArray();
  var b=second.Candidates.OrderBy(c=>c.ExternalKey).ToArray();
  if(!a.SequenceEqual(b))return Review("EvidenceChanged");
  // Detail references only: the observed payment register omits its reference.
  // Search all kinds to detect cross-kind source reference collisions.
  var matches=b.Where(c=>string.Equals(c.DetailReference,target.Reference,StringComparison.OrdinalIgnoreCase)).ToArray();
  if(matches.Length>1)return Conflict("DuplicateReference");
  if(matches.Length==0)
  {
   if(target.KnownExternalKey.HasValue&&b.Any(c=>c.ExternalKey==target.KnownExternalKey.Value))return Conflict("KnownExternalIdentityChanged");
   return new(SyntheticRecoveryFinding.NoMatchObserved,"AbsenceDoesNotProveNonPosting");
  }
  var match=matches[0];
  if(match.DetailReference!=target.Reference||match.Kind!=target.Kind||match.Fingerprint!=target.Fingerprint||
     (target.KnownExternalKey.HasValue&&match.ExternalKey!=target.KnownExternalKey.Value))return Conflict("CorrelationMismatch");
  return new(SyntheticRecoveryFinding.MatchedForReview,"IndependentManualReviewStillRequired");
 }
}
