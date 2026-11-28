using Microsoft.Data.Sqlite;
using PracticeManager.Core;
using System.Security.Cryptography;
using System.Text;
namespace PracticeManager.Persistence;
public sealed partial class LocalPractice
{
 public bool CanReviewCollections()=>CanManageBusinessMarker()&&Can("Reports.Financial");
 private long RequireCollections(SqliteConnection c,SqliteTransaction t){var actor=RequireMarker(c,t);Require(c,t,"Reports.Financial");return actor;}
 // Deliberately separate from saved accounting profile and enrolled original marker.
 // One-time credential in memory only; no profile, journal or hold mutation.
 public async Task<CollectionPreview> ReadDueDateFixtureAsync(string endpoint,string secret,bool confirmed,CancellationToken ct=default)
 {
  long actor;var generation=sessionGeneration;
  using(var c=Open())using(var t=c.BeginTransaction()){actor=RequireCollections(c,t);}
  if(!confirmed)return new("OperatorScopeEvidenceRequired");
  var result=await new ManagerDesktopProbe().ReadSyntheticDueDateFixtureAsync(endpoint,secret,true,ct);
  using var read=Open();using var tx=read.BeginTransaction();
  if(sessionGeneration!=generation||RequireCollections(read,tx)!=actor)throw new UnauthorizedAccessException();
  if(ct.IsCancellationRequested)return new("Cancelled");
  return result;
 }
 public async Task<ReminderReview> PrepareDueFixtureReminderAsync(string endpoint,string secret,bool confirmed,CollectionPreview previous,Guid invoiceKey,CancellationToken ct=default)
 {
  var fresh=await ReadDueDateFixtureAsync(endpoint,secret,confirmed,ct);
  if(ct.IsCancellationRequested)return new("Cancelled");
  if(fresh.Outcome!="ObservedForReview")return new(fresh.Outcome);
  return CollectionReminderReview.Prepare(previous,fresh,invoiceKey,DateOnly.FromDateTime(DateTime.Today),DateTimeOffset.UtcNow);
 }
 public async Task<CollectionPreview> ReadSavedCollectionsAsync(bool singleUsdConfirmed,CancellationToken ct=default)
 {
  long actor;StoredProfile? profile;Guid? marker;var generation=sessionGeneration;
  using(var c=Open())using(var t=c.BeginTransaction()){actor=RequireCollections(c,t);profile=ReadProfile(c,t,actor);marker=ReadMarker(c,t);}
  if(!singleUsdConfirmed)return new("OperatorScopeEvidenceRequired");
  if(profile==null)return new("NotConfigured");
  if(marker==null)return new("MarkerNotEnrolled");
  if(!OperatingSystem.IsWindows())return new("CredentialUnavailable");
  byte[] clear;
  try{clear=ProtectedData.Unprotect(profile.ProtectedValue,Context(actor,profile.Endpoint),DataProtectionScope.CurrentUser);}
  catch(CryptographicException){return new("CredentialUnavailable");}
  CollectionPreview result;
  try{result=await new ManagerDesktopProbe().ReadSyntheticCollectionsAsync(profile.Endpoint,Encoding.UTF8.GetString(clear),marker.Value,true,ct);}
  finally{CryptographicOperations.ZeroMemory(clear);}
  using var read=Open();using var tx=read.BeginTransaction();
  if(sessionGeneration!=generation||RequireCollections(read,tx)!=actor)throw new UnauthorizedAccessException();
  if(ct.IsCancellationRequested)return new("Cancelled");
  if(ReadProfile(read,tx,actor)?.Revision!=profile.Revision)return new("ProfileChanged");
  if(ReadMarker(read,tx)!=marker)return new("MarkerChanged");
  return result;
 }
}
