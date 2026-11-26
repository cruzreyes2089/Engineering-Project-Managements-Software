using Microsoft.Data.Sqlite;
using PracticeManager.Core;
using System.Security.Cryptography;
using System.Text;
namespace PracticeManager.Persistence;
public sealed partial class LocalPractice
{
 public bool CanReviewCollections()=>CanManageBusinessMarker()&&Can("Reports.Financial");
 private long RequireCollections(SqliteConnection c,SqliteTransaction t){var actor=RequireMarker(c,t);Require(c,t,"Reports.Financial");return actor;}
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
