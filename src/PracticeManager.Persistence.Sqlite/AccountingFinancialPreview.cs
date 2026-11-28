using Microsoft.Data.Sqlite;
using PracticeManager.Core;
using System.Security.Cryptography;
using System.Text;
namespace PracticeManager.Persistence;
public sealed record FinancialPreviewResult(string Outcome,FinancialPreview? Preview=null);
public sealed partial class LocalPractice
{
 public async Task<FinancialPreviewResult> ReadFinancialPreviewAsync(CancellationToken ct=default)
 {
  long actor;StoredProfile? profile;Guid? marker;var generation=sessionGeneration;
  using(var c=Open())using(var t=c.BeginTransaction()){
   actor=RequireMarker(c,t);profile=ReadProfile(c,t,actor);marker=ReadMarker(c,t);
  }
  if(profile==null)return new("NotConfigured");
  if(marker==null)return new("MarkerNotEnrolled");
  if(!OperatingSystem.IsWindows())return new("CredentialUnavailable");
  byte[] clear;
  try{clear=ProtectedData.Unprotect(profile.ProtectedValue,Context(actor,profile.Endpoint),DataProtectionScope.CurrentUser);}
  catch(CryptographicException){return new("CredentialUnavailable");}
  FinancialPreview? preview=null;string outcome="Connected";
  try{
   var key=Encoding.UTF8.GetString(clear);var probe=new ManagerDesktopProbe();
   var before=await VerifyMarkerAtAsync(profile.Endpoint,key,marker.Value,ct);
   if(!before.Success)return new(before.Outcome);
   var pages=new List<FinancialPage>();
   foreach(var resource in new[]{AccountingDiscoveryResource.SalesInvoices,AccountingDiscoveryResource.Receipts,AccountingDiscoveryResource.Payments}){
    var r=await probe.ReadFinancialPageAsync(profile.Endpoint,key,resource,ct);
    if(!r.Success||r.Financial==null)return new(r.Outcome);
    pages.Add(r.Financial);
   }
   var after=await VerifyMarkerAtAsync(profile.Endpoint,key,marker.Value,ct);
   if(!after.Success)return new(after.Outcome);
   preview=FinancialPreview.Combine(pages[0],pages[1],pages[2]);
   if(preview==null)outcome="BusinessChanged";
  }
  finally{CryptographicOperations.ZeroMemory(clear);}
  using var write=Open();using var tx=write.BeginTransaction(deferred:false);
  if(sessionGeneration!=generation||RequireMarker(write,tx)!=actor)throw new UnauthorizedAccessException();
  if(ct.IsCancellationRequested)return new("Cancelled");
  if(ReadProfile(write,tx,actor)?.Revision!=profile.Revision)return new("ProfileChanged");
  if(ReadMarker(write,tx)!=marker)return new("MarkerChanged");
  if(preview!=null){
   Exec(write,tx,"UPDATE accounting_profiles SET last_success_utc=$s WHERE user_id=$id AND revision=$r",("$s",UtcNow()),("$id",actor),("$r",profile.Revision));
   tx.Commit();
  }
  return new(outcome,preview);
 }
}
