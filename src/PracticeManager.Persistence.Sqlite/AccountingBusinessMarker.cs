using Microsoft.Data.Sqlite;
using PracticeManager.Core;
using System.Security.Cryptography;
using System.Text;
namespace PracticeManager.Persistence;
public sealed record BusinessMarkerStatus(bool Enrolled);
public sealed partial class LocalPractice
{
 private static Guid? ReadMarker(SqliteConnection c,SqliteTransaction t)
 {
  var value=Scalar(c,t,"SELECT marker_key FROM accounting_business_marker WHERE id=1");
  return value==null||value==DBNull.Value?null:Guid.Parse((string)value);
 }
 private long RequireMarker(SqliteConnection c,SqliteTransaction t)
 {
  var a=Require(c,t,"Accounting.Identity.Manage");Require(c,t,"Accounting.Connection.Test");Require(c,t,"Accounting.Connection.Manage");return a;
 }
 public bool CanManageBusinessMarker()=>Can("Accounting.Identity.Manage")&&Can("Accounting.Connection.Test")&&Can("Accounting.Connection.Manage");
 public BusinessMarkerStatus ReadBusinessMarker()
 {
  using var c=Open();using var t=c.BeginTransaction();RequireMarker(c,t);return new(ReadMarker(c,t).HasValue);
 }
 private static async Task<AccountingProbeResult> VerifyMarkerAtAsync(string endpoint,string key,Guid expected,CancellationToken ct)
 {
  var r=await new ManagerDesktopProbe().FindMarkerAsync(endpoint,key,BusinessMarker.TestName,ct);
  return r.Success&&r.MarkerKey!=expected?new("MarkerMismatch",200):r;
 }
 public async Task<AccountingProbeResult> EnrollOrVerifyMarkerAsync(bool enroll,CancellationToken ct=default)
 {
  long actor;Guid? expected;StoredProfile? profile;var generation=sessionGeneration;
  using(var c=Open())using(var t=c.BeginTransaction()){
   actor=RequireMarker(c,t);expected=ReadMarker(c,t);profile=ReadProfile(c,t,actor);
  }
  if(profile==null)return new("NotConfigured");
  if(expected==null&&!enroll)return new("MarkerNotEnrolled");
  if(!OperatingSystem.IsWindows())return new("CredentialUnavailable");
  byte[] clear;
  try{clear=ProtectedData.Unprotect(profile.ProtectedValue,Context(actor,profile.Endpoint),DataProtectionScope.CurrentUser);}
  catch(CryptographicException){return new("CredentialUnavailable");}
  AccountingProbeResult r;
  try{r=await new ManagerDesktopProbe().FindMarkerAsync(profile.Endpoint,Encoding.UTF8.GetString(clear),BusinessMarker.TestName,ct);}
  finally{CryptographicOperations.ZeroMemory(clear);}
  using var write=Open();using var tx=write.BeginTransaction(deferred:false);
  if(sessionGeneration!=generation||RequireMarker(write,tx)!=actor)throw new UnauthorizedAccessException();
  if(ct.IsCancellationRequested)return new("Cancelled");
  if(ReadProfile(write,tx,actor)?.Revision!=profile.Revision)return new("ProfileChanged");
  if(ReadMarker(write,tx)!=expected)return new("MarkerChanged");
  if(!r.Success||!r.MarkerKey.HasValue)return r;
  if(expected.HasValue&&r.MarkerKey!=expected)return new("MarkerMismatch",200);
  if(!expected.HasValue){
   Exec(write,tx,"INSERT INTO accounting_business_marker(id,marker_key,bound_utc) VALUES(1,$k,$u)",("$k",r.MarkerKey.Value.ToString("D")),("$u",UtcNow()));
   Audit(write,tx,actor,"Enroll","AccountingBusinessMarker",1);
  }
  tx.Commit();return new("Connected",200,"Object");
 }
}
