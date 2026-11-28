using PracticeManager.Core;
using System.Security.Cryptography;
using System.Text;
namespace PracticeManager.Persistence;
public sealed partial class LocalPractice
{
 public async Task<TestLedgerReconciliationResult> ReconcileSavedSyntheticAsync(SyntheticSummaryEvidence evidence,CancellationToken ct=default)
 {
  static TestLedgerReconciliationResult Block(string reason)=>new("Blocked",Array.AsReadOnly(new[]{reason}));
  var start=GetDirectoryContext();if(start.Context==null)return Block(start.Outcome);var d=start.Context;
  DirectoryAccountRole[] roles;
  using(var c=Open())using(var t=c.BeginTransaction()){
   if(!CurrentDirectoryContext(c,t,d))return Block("ProfileChanged");
   if(DirectoryBusiness(c,t,d)!="CRC ACCOUNTING TEST")return Block("SyntheticDirectoryRequired");
   roles=BuildDirectoryReport(c,t,d).Roles.ToArray();
  }
  if(roles.Length!=2||!roles.Any(x=>x.Role=="ServiceIncome"&&x.Code=="4000"&&x.Name=="Engineering Services Income")||
    !roles.Any(x=>x.Role=="OfficeExpense"&&x.Code=="6000"&&x.Name=="Office Supplies"))return Block("FixtureRolesRequired");
  if(!evidence.SingleCurrencyAndZeroOpeningConfirmed)return Block("OperatorEvidenceRequired");
  if(ct.IsCancellationRequested)return Block("Cancelled");
  if(!OperatingSystem.IsWindows())return Block("CredentialUnavailable");
  byte[] clear;
  try{clear=ProtectedData.Unprotect(d.Profile.ProtectedValue,Context(d.Actor,d.Profile.Endpoint),DataProtectionScope.CurrentUser);}
  catch(CryptographicException){return Block("CredentialUnavailable");}
  TestLedgerReconciliationResult result;
  try{
   var probe=new ManagerDesktopProbe();
   result=await probe.ReconcileSyntheticAsync(d.Profile.Endpoint,Encoding.UTF8.GetString(clear),d.Marker,
    roles.Single(x=>x.Role=="ServiceIncome").Key,roles.Single(x=>x.Role=="OfficeExpense").Key,evidence,ct);
  }finally{CryptographicOperations.ZeroMemory(clear);}
  using var read=Open();using var tx=read.BeginTransaction(deferred:false);
  if(!CurrentDirectoryContext(read,tx,d))return Block("ProfileChanged");
  if(ct.IsCancellationRequested)return Block("Cancelled");
  if(DirectoryBusiness(read,tx,d)!="CRC ACCOUNTING TEST"||!BuildDirectoryReport(read,tx,d).Roles.SequenceEqual(roles))return Block("DirectoryChanged");
  return result; // No accounting or local financial record writes.
 }
}
