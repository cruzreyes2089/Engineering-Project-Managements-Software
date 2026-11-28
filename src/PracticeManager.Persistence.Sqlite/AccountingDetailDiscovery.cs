using PracticeManager.Core;
using System.Security.Cryptography;
using System.Text;
namespace PracticeManager.Persistence;
public sealed record DetailDiscoveryResult(string Outcome,string? Report=null)
{
 public override string ToString()=>$"Detail discovery: {Outcome}";
}
public sealed partial class LocalPractice
{
 public async Task<DetailDiscoveryResult> InspectFinancialDetailsAsync(CancellationToken ct=default)
 {
  var start=GetDirectoryContext();if(start.Context==null)return new(start.Outcome);var d=start.Context;
  string? business;DirectoryAccountRole[] roles;
  using(var c=Open())using(var t=c.BeginTransaction()){
   if(!CurrentDirectoryContext(c,t,d))return new("ProfileChanged");
   business=DirectoryBusiness(c,t,d);if(business==null)return new("NotSynchronized");
   roles=BuildDirectoryReport(c,t,d).Roles.ToArray();
  }
  if(roles.Length!=2||!roles.Any(x=>x.Role=="ServiceIncome")||!roles.Any(x=>x.Role=="OfficeExpense"))return new("RolesNotConfigured");
  if(!OperatingSystem.IsWindows())return new("CredentialUnavailable");
  byte[] clear;
  try{clear=ProtectedData.Unprotect(d.Profile.ProtectedValue,Context(d.Actor,d.Profile.Endpoint),DataProtectionScope.CurrentUser);}
  catch(CryptographicException){return new("CredentialUnavailable");}
  var lines=new List<string>{"Synthetic financial detail discovery — read-only"};
  using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(TimeSpan.FromSeconds(60));var token=deadline.Token;
  try{
   var key=Encoding.UTF8.GetString(clear);var probe=new ManagerDesktopProbe();
   var before=await VerifyMarkerAtAsync(d.Profile.Endpoint,key,d.Marker,token);if(!before.Success)return new(before.Outcome);
   var pages=new List<FinancialPage>();
   foreach(var resource in new[]{AccountingDiscoveryResource.SalesInvoices,AccountingDiscoveryResource.Receipts,AccountingDiscoveryResource.Payments}){
    var r=await probe.ReadFinancialPageAsync(d.Profile.Endpoint,key,resource,token);if(!r.Success||r.Financial==null)return new(r.Outcome);
    if(r.Financial.BusinessName!=business)return new("BusinessChanged");
    if(r.Financial.Rows.Count!=1)return new("FixtureRequiresOneRecordPerRegister");
    pages.Add(r.Financial);
   }
   var preview=FinancialPreview.Combine(pages[0],pages[1],pages[2]);
   if(preview==null||!preview.MatchesFixture)return new("FixtureAmountsDiffer");
   var accounts=await probe.ReadAccountsAsync(d.Profile.Endpoint,key,token);
   if(!accounts.Success||accounts.Accounts==null)return new(accounts.Outcome);
   if(accounts.Accounts.BusinessName!=business)return new("BusinessChanged");
   foreach(var role in roles)if(!accounts.Accounts.Rows.Any(x=>x.Key==role.Key&&x.Name==role.Name&&x.Code==role.Code))return new("AccountChanged");
   var requests=new List<(string Label,AccountingDetailResource Resource,Guid Key)>{
    ("Sales invoice",AccountingDetailResource.Invoice,pages[0].Rows[0].Key),
    ("Receipt",AccountingDetailResource.Receipt,pages[1].Rows[0].Key),
    ("Payment",AccountingDetailResource.Payment,pages[2].Rows[0].Key)};
   requests.AddRange(roles.Select(x=>(x.Role,AccountingDetailResource.ProfitAndLossAccount,x.Key)));
   foreach(var item in requests){
    var result=await probe.InspectDetailAsync(d.Profile.Endpoint,key,item.Resource,item.Key,token);
    lines.Add("");lines.Add(item.Label+" — "+(result.StatusCode.HasValue?"HTTP "+result.StatusCode:result.Outcome));
    if(!result.Success||result.SchemaSummary==null)return new("Detail"+result.Outcome);
    lines.Add(result.SchemaSummary);
   }
   var after=await VerifyMarkerAtAsync(d.Profile.Endpoint,key,d.Marker,token);if(!after.Success)return new(after.Outcome);
   lines.Insert(1,"Marker verified; synthetic list amounts matched (100 invoiced, 60 outstanding, 40 receipts, 10 payments).");
  }finally{CryptographicOperations.ZeroMemory(clear);}
  using var write=Open();using var tx=write.BeginTransaction(deferred:false);
  if(!CurrentDirectoryContext(write,tx,d))return new("ProfileChanged");
  if(token.IsCancellationRequested)return new("Cancelled");
  if(DirectoryBusiness(write,tx,d)!=business||!BuildDirectoryReport(write,tx,d).Roles.SequenceEqual(roles))return new("DirectoryChanged");
  return new("Connected",string.Join(Environment.NewLine,lines));
 }
}
