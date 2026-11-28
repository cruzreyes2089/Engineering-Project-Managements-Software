using PracticeManager.Core;
using PracticeManager.Persistence;
using System.Text.Json;
internal static class AccountingDiscoveryTests
{
 public static async Task Run(string path,string password,Action<bool,string> check)
 {
  var probe=new ManagerDesktopProbe();var marker=Guid.NewGuid().ToString("N");
  var body=JsonSerializer.Serialize(new {business=new {name=marker},totalRecords=1,items=new[]{new {key=Guid.NewGuid(),name=marker,amount=17.25}}});
  var routes=new Dictionary<AccountingDiscoveryResource,string>{
   {AccountingDiscoveryResource.IdentityFields,"text-custom-fields?skip=0&pageSize=2"},
   {AccountingDiscoveryResource.ChartOfAccounts,"chart-of-accounts?fields=Key&fields=Code&fields=Name"},
   {AccountingDiscoveryResource.SalesInvoices,"sales-invoices?skip=0&pageSize=2"},
   {AccountingDiscoveryResource.Receipts,"receipts?skip=0&pageSize=2"},
   {AccountingDiscoveryResource.Payments,"payments?skip=0&pageSize=2"}
  };
  foreach(var (resource,route) in routes)
   await AccountingProbeTests.WithServer(200,body,async(url,observed)=>{
    var r=await probe.InspectResourceAsync(url,marker,resource);
    check(r.Success&&r.SchemaSummary?.Contains("key: String (UUID-shaped)")==true,"resource schema shape "+resource);
    check(r.SchemaSummary?.Contains(marker)==false&&!r.ToString().Contains("17.25"),"resource values redacted "+resource);
    check((await observed).StartsWith("GET /api2/"+route+" HTTP/1.1",StringComparison.Ordinal),"fixed read-only resource route "+resource);
   });
  check((await probe.InspectResourceAsync("http://127.0.0.1:1/api2",marker,(AccountingDiscoveryResource)99)).Outcome=="InvalidResource","unknown discovery resource rejected");
  check((await probe.InspectResourceAsync("http://example.invalid:12345/api2",marker,AccountingDiscoveryResource.ChartOfAccounts)).Outcome=="InvalidEndpoint","external discovery blocked");
  await AccountingProbeTests.WithServer(401,"{}",async(url,_)=>check((await probe.InspectResourceAsync(url,marker,AccountingDiscoveryResource.Receipts)).Outcome=="Unauthorized","discovery authentication rejected"));
  await AccountingProbeTests.WithServer(302,"{}",async(url,_)=>check((await probe.InspectResourceAsync(url,marker,AccountingDiscoveryResource.Payments)).Outcome=="RedirectBlocked","discovery redirect rejected"));
  var s=new LocalPractice(path);check(s.Login("admin",password),"discovery administrator login");
  await AccountingProbeTests.WithServer(200,body,async(url,_)=>{
    check((await s.SaveAccountingProfileAsync(url,marker)).Success,"discovery profile saved");
    var r=await s.InspectSavedResourceAsync(AccountingDiscoveryResource.ChartOfAccounts);
    check(r.Success&&!string.IsNullOrEmpty(r.ProfileRevision),"discovery saved profile bound");
    s.RemoveAccountingProfile();
  },2);
  check((await s.InspectSavedResourceAsync(AccountingDiscoveryResource.ChartOfAccounts)).Outcome=="NotConfigured","discovery removed profile blocked");
  check(s.Login("employee",password),"discovery employee login");
  try{await s.InspectSavedResourceAsync(AccountingDiscoveryResource.ChartOfAccounts);throw new Exception("FAIL: unauthorized discovery");}catch(UnauthorizedAccessException){check(true,"discovery employee denied");}
 }
}
