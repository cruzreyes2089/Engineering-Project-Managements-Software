using System.Text.Json;
using PracticeManager.Core;
using PracticeManager.Persistence;
internal static class AccountingFinancialTests
{
 public static async Task Run(string path,string pw,Action<bool,string> check)
 {
  var probe=new ManagerDesktopProbe();var key=Guid.NewGuid().ToString("N");var id=Guid.NewGuid();var marker=Guid.NewGuid();
  object Money(decimal value)=>new{value,currency=(string?)null};
  string Page(string resource,object[] rows,int? total=null,string name="Synthetic")=>JsonSerializer.Serialize(new Dictionary<string,object>{{"business",new{name}},{"skip",0},{"pageSize",20},{"totalRecords",total??rows.Length},{resource,rows}});
  object Invoice(decimal amount,decimal due)=>new{key=id,invoiceAmount=Money(amount),balanceDue=Money(due)};
  string invoice=Page("salesInvoices",new[]{Invoice(100m,60m)});
  string receipt=Page("receipts",new object[]{new{key=Guid.NewGuid(),amount=Money(40m)}});
  string payment=Page("payments",new object[]{new{key=Guid.NewGuid(),amount=Money(10m)}});
  await AccountingProbeTests.WithServer(200,invoice,async(url,observed)=>{
   var r=await probe.ReadFinancialPageAsync(url,key,AccountingDiscoveryResource.SalesInvoices);
   check(r.Success&&r.Financial?.Rows.Single().Amount==100m&&r.Financial.Rows.Single().BalanceDue==60m,"decimal invoice parsed");
   check((await observed).StartsWith("GET /api2/sales-invoices?skip=0&pageSize=20 HTTP/1.1",StringComparison.Ordinal),"financial bounded GET");
   check(!r.ToString().Contains(id.ToString())&&!r.Financial!.ToString().Contains("Synthetic"),"financial diagnostics redact values");
  });
  foreach(var invalid in new[]{
   Page("salesInvoices",new[]{Invoice(100m,60m)},21),
   Page("salesInvoices",new[]{Invoice(100m,60m),Invoice(100m,60m)}),
   Page("salesInvoices",new[]{Invoice(100m,101m)}),
   Page("salesInvoices",new[]{Invoice(-1m,0m)}),
   Page("salesInvoices",new[]{Invoice(1.0000001m,0m)}),
   Page("salesInvoices",new object[]{new{key=id,invoiceAmount=new{value=100,currency="USD"},balanceDue=Money(60m)}}),
   Page("salesInvoices",new object[]{new{key=id,invoiceAmount=new{value="100",currency=(string?)null},balanceDue=Money(60m)}}),
   Page("salesInvoices",new object[]{new{key=id,invoiceAmount=new{value=100},balanceDue=Money(60m)}})
  })await AccountingProbeTests.WithServer(200,invalid,async(url,_)=>{var r=await probe.ReadFinancialPageAsync(url,key,AccountingDiscoveryResource.SalesInvoices);check(!r.Success&&r.Financial==null,"invalid or unsupported financial page blocked");});
  check((await probe.ReadFinancialPageAsync("http://127.0.0.1:1/api2",key,AccountingDiscoveryResource.IdentityFields)).Outcome=="InvalidResource","nonfinancial resource rejected");
  var s=new LocalPractice(path);s.Bootstrap("Synthetic Financial Firm","admin",pw,"CL");check(s.Login("admin",pw),"financial administrator login");
  string Marker(Guid k)=>JsonSerializer.Serialize(new{business=new{name="Synthetic"},skip=0,pageSize=2,totalRecords=1,textCustomFields=new[]{new{key=k,name=BusinessMarker.TestName}}});
  string Reply(string req)=>req.StartsWith("GET /api2/text-custom-fields?",StringComparison.Ordinal)?Marker(marker):req.StartsWith("GET /api2/sales-invoices?",StringComparison.Ordinal)?invoice:req.StartsWith("GET /api2/receipts?",StringComparison.Ordinal)?receipt:req.StartsWith("GET /api2/payments?",StringComparison.Ordinal)?payment:"{\"customers\":[]}";
  var requests=new List<string>();
  await AccountingProbeTests.WithServer(200,"{}",async(url,_)=>{
   check((await s.SaveAccountingProfileAsync(url,key)).Success,"financial fixture profile saved");
   check((await s.ReadFinancialPreviewAsync()).Outcome=="MarkerNotEnrolled","financial preview requires marker");
   check((await s.EnrollOrVerifyMarkerAsync(true)).Success,"financial fixture marker enrolled");
   var r=await s.ReadFinancialPreviewAsync();
   check(r.Outcome=="Connected"&&r.Preview?.MatchesFixture==true&&r.Preview.NetReceiptLessPayment==30m,"synthetic list totals match exactly");
   check(requests.Count==7&&requests[2].Contains("/text-custom-fields?")&&requests[6].Contains("/text-custom-fields?"),"marker checked before and after financial reads");
  },7,requests.Add,Reply);
  var a=new FinancialPage(AccountingDiscoveryResource.SalesInvoices,"A",new[]{new FinancialRow(id,0.1m,0m),new FinancialRow(Guid.NewGuid(),0.2m,0m)});
  var b=new FinancialPage(AccountingDiscoveryResource.Receipts,"A",Array.Empty<FinancialRow>());
  var c=new FinancialPage(AccountingDiscoveryResource.Payments,"A",Array.Empty<FinancialRow>());
  check(FinancialPreview.Combine(a,b,c)?.Invoiced==0.3m,"decimal arithmetic exact");
  check(FinancialPreview.Combine(a,b,c with{BusinessName="B"})==null,"different business responses rejected");
  s.RemoveAccountingProfile();check((await s.ReadFinancialPreviewAsync()).Outcome=="NotConfigured","removed connection blocks preview");
 }
}
