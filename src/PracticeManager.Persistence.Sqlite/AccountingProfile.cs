using Microsoft.Data.Sqlite;
using PracticeManager.Core;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
namespace PracticeManager.Persistence;

public sealed record AccountingProfile(string Endpoint,string LastSuccessUtc);
public sealed partial class LocalPractice
{
 private sealed record StoredProfile(string Endpoint,byte[] ProtectedValue,string LastSuccessUtc,string Revision);
 private static StoredProfile? ReadProfile(SqliteConnection c,SqliteTransaction t,long actor)
 {
  using var q=Command(c,t,"SELECT endpoint,protected_value,last_success_utc,revision FROM accounting_profiles WHERE user_id=$id",("$id",actor));
  using var r=q.ExecuteReader();
  return r.Read()?new(r.GetString(0),(byte[])r.GetValue(1),r.GetString(2),r.GetString(3)):null;
 }
 public AccountingProfile? ReadAccountingProfile()
 {
  using var c=Open();using var t=c.BeginTransaction();
  var actor=Require(c,t,"Accounting.Connection.Test");
  var p=ReadProfile(c,t,actor);
  return p==null?null:new(p.Endpoint,p.LastSuccessUtc);
 }
 private static byte[] Context(long actor,string endpoint)=>Encoding.UTF8.GetBytes("CRC.PracticeManager.ManagerDesktop.v1|"+actor.ToString(CultureInfo.InvariantCulture)+"|"+endpoint);
 private static string UtcNow()=>DateTimeOffset.UtcNow.ToString("O",CultureInfo.InvariantCulture);
 private long CheckProfileSession(SqliteConnection c,SqliteTransaction t,long generation,long actor,bool manage)
 {
  if(sessionGeneration!=generation||Require(c,t,"Accounting.Connection.Test")!=actor)throw new UnauthorizedAccessException();
  if(manage)Require(c,t,"Accounting.Connection.Manage");
  return actor;
 }
 public async Task<AccountingProbeResult> SaveAccountingProfileAsync(string endpoint,string key,CancellationToken ct=default)
 {
  long actor;string? previous;var generation=sessionGeneration;
  using(var c=Open())using(var t=c.BeginTransaction()){
   actor=Require(c,t,"Accounting.Connection.Test");Require(c,t,"Accounting.Connection.Manage");previous=ReadProfile(c,t,actor)?.Revision;
  }
  if(!OperatingSystem.IsWindows())return new("CredentialUnavailable");
  endpoint=endpoint.Trim();
  var result=await new ManagerDesktopProbe().TestAsync(endpoint,key,ct);
  if(!result.Success)return result;
  if(ct.IsCancellationRequested)return new("Cancelled");
  byte[] encrypted;
  var input=Encoding.UTF8.GetBytes(key);
  try{encrypted=ProtectedData.Protect(input,Context(actor,endpoint),DataProtectionScope.CurrentUser);}
  catch(CryptographicException){return new("CredentialUnavailable");}
  finally{CryptographicOperations.ZeroMemory(input);}
  using var write=Open();using var tx=write.BeginTransaction(deferred:false);
  CheckProfileSession(write,tx,generation,actor,true);
  if(ct.IsCancellationRequested)return new("Cancelled");
  if(ReadProfile(write,tx,actor)?.Revision!=previous)return new("ProfileChanged");
  Exec(write,tx,"INSERT INTO accounting_profiles(user_id,endpoint,protected_value,last_success_utc,revision) VALUES($id,$e,$v,$s,$r) ON CONFLICT(user_id) DO UPDATE SET endpoint=excluded.endpoint,protected_value=excluded.protected_value,last_success_utc=excluded.last_success_utc,revision=excluded.revision",("$id",actor),("$e",endpoint),("$v",encrypted),("$s",UtcNow()),("$r",Guid.NewGuid().ToString("N")));
  Audit(write,tx,actor,"Save","AccountingProfile",actor);tx.Commit();return result;
 }
 public Task<AccountingProbeResult> TestSavedAccountingProfileAsync(CancellationToken ct=default)=>UseSavedAccountingProfileAsync(false,ct);
 public Task<AccountingProbeResult> InspectSavedCustomersAsync(CancellationToken ct=default)=>UseSavedAccountingProfileAsync(true,ct);
 public Task<AccountingProbeResult> ReadSavedCustomersAsync(int skip,CancellationToken ct=default)=>UseSavedAccountingProfileAsync(false,ct,skip);
 private async Task<AccountingProbeResult> UseSavedAccountingProfileAsync(bool inspect,CancellationToken ct,int? customerSkip=null)
 {
  long actor;StoredProfile? profile;var generation=sessionGeneration;
  using(var c=Open())using(var t=c.BeginTransaction()){
   actor=Require(c,t,"Accounting.Connection.Test");profile=ReadProfile(c,t,actor);
  }
  if(profile==null)return new("NotConfigured");
  if(!OperatingSystem.IsWindows())return new("CredentialUnavailable");
  byte[] clear;
  try{clear=ProtectedData.Unprotect(profile.ProtectedValue,Context(actor,profile.Endpoint),DataProtectionScope.CurrentUser);}
  catch(CryptographicException){return new("CredentialUnavailable");}
  AccountingProbeResult result;
  try{var probe=new ManagerDesktopProbe();var key=Encoding.UTF8.GetString(clear);result=customerSkip is int offset?await probe.ReadCustomerPageAsync(profile.Endpoint,key,offset,ct):inspect?await probe.InspectCustomersAsync(profile.Endpoint,key,ct):await probe.TestAsync(profile.Endpoint,key,ct);}
  finally{CryptographicOperations.ZeroMemory(clear);}
  using var write=Open();using var tx=write.BeginTransaction(deferred:false);
  CheckProfileSession(write,tx,generation,actor,false);
  if(ct.IsCancellationRequested)return new("Cancelled");
  if(ReadProfile(write,tx,actor)?.Revision!=profile.Revision)return new("ProfileChanged");
  if(result.Success){
   Exec(write,tx,"UPDATE accounting_profiles SET last_success_utc=$s WHERE user_id=$id AND revision=$r",("$s",UtcNow()),("$id",actor),("$r",profile.Revision));
   tx.Commit();
  }
  return result.Success&&customerSkip.HasValue?result with {ProfileRevision=profile.Revision}:result;
 }
 public void RemoveAccountingProfile()
 {
  using var c=Open();using var t=c.BeginTransaction(deferred:false);
  var actor=Require(c,t,"Accounting.Connection.Test");Require(c,t,"Accounting.Connection.Manage");
  if(Exec(c,t,"DELETE FROM accounting_profiles WHERE user_id=$id",("$id",actor))>0)Audit(c,t,actor,"Remove","AccountingProfile",actor);
  t.Commit();
 }
}
