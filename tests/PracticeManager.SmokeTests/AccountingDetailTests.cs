using System.Text.Json;
using PracticeManager.Core;
using PracticeManager.Persistence;
internal static class AccountingDetailTests
{
 public static async Task Run(string path,string pw,Action<bool,string> check)
 {
  var probe=new ManagerDesktopProbe();var key=Guid.NewGuid().ToString("N");var id=Guid.NewGuid();
  var body=JsonSerializer.Serialize(new{customer=id,lines=new[]{new{account=id,amount=100m,description="Private description"}},customFields=new Dictionary<string,string>{{"PrivateField","secret-value"}}});
  await AccountingProbeTests.WithServer(200,body,async(url,observed)=>{
   var r=await probe.InspectDetailAsync(url,key,AccountingDetailResource.Invoice,id);
   check(r.Success&&r.SchemaSummary?.Contains("lines[].account: String (UUID-shaped)")==true,"nested detail relationship schema");
   check(r.SchemaSummary!=null&&!r.SchemaSummary.Contains(id.ToString())&&!r.SchemaSummary.Contains("Private description")&&!r.SchemaSummary.Contains("PrivateField")&&!r.SchemaSummary.Contains("secret-value"),"detail values and custom keys omitted");
   check((await observed).StartsWith("GET /api2/sales-invoice-form/"+id.ToString("D")+" HTTP/1.1",StringComparison.Ordinal),"fixed invoice form route");
  });
  foreach(var (resource,route) in new[]{(AccountingDetailResource.Receipt,"receipt-form"),(AccountingDetailResource.Payment,"payment-form"),(AccountingDetailResource.ProfitAndLossAccount,"profit-and-loss-statement-account-form")})
   await AccountingProbeTests.WithServer(200,body,async(url,observed)=>check((await probe.InspectDetailAsync(url,key,resource,id)).Success&&(await observed).StartsWith("GET /api2/"+route+"/"+id.ToString("D")+" HTTP/1.1",StringComparison.Ordinal),"fixed detail route"));
  check((await probe.InspectDetailAsync("http://127.0.0.1:1/api2",key,AccountingDetailResource.Invoice,Guid.Empty)).Outcome=="InvalidDetail","empty key refused");
  check((await probe.InspectDetailAsync("http://127.0.0.1:1/api2",key,(AccountingDetailResource)99,id)).Outcome=="InvalidDetail","invalid detail resource refused");
  await AccountingProbeTests.WithServer(200,"[]",async(url,_)=>check((await probe.InspectDetailAsync(url,key,AccountingDetailResource.Invoice,id)).Outcome=="InvalidDetailResponse","non-object detail rejected"));
  await AccountingProbeTests.WithServer(200,"{\"a\":1,\"a\":2}",async(url,_)=>check((await probe.InspectDetailAsync(url,key,AccountingDetailResource.Invoice,id)).Outcome=="InvalidDetailResponse","duplicate detail field rejected"));
  var s=new LocalPractice(path);s.Bootstrap("Synthetic Detail Firm","admin",pw,"CL");check(s.Login("admin",pw),"detail admin login");
  check((await s.InspectFinancialDetailsAsync()).Outcome=="NotConfigured","detail requires saved profile");
  var evidence=new SyntheticSummaryEvidence(new(60,30,100,10,90,0,90,90),true);
  check((await s.ReconcileSavedSyntheticAsync(evidence)).Differences.Contains("NotConfigured"),"reconciliation requires saved profile");
  var marker=Guid.NewGuid();var income=Guid.NewGuid();var expense=Guid.NewGuid();var cust=Guid.NewGuid();
  string Marker()=>JsonSerializer.Serialize(new{business=new{name="Synthetic"},skip=0,pageSize=2,totalRecords=1,textCustomFields=new[]{new{key=marker,name=BusinessMarker.TestName}}});
  string Customers()=>JsonSerializer.Serialize(new{business=new{name="Synthetic"},skip=0,pageSize=20,totalRecords=1,customers=new[]{new{key=cust,name="Synthetic customer",status="Paid"}}});
  string Accounts()=>JsonSerializer.Serialize(new{business=new{name="Synthetic"},totalRecords=2,chartOfAccounts=new[]{new{key=income,name="Synthetic Income",code="4000"},new{key=expense,name="Synthetic Expense",code="6000"}}});
  object Money(decimal value)=>new{value,currency=(string?)null};
  string Page(string resource,object row)=>JsonSerializer.Serialize(new Dictionary<string,object>{{"business",new{name="Synthetic"}},{"skip",0},{"pageSize",20},{"totalRecords",1},{resource,new[]{row}}});
  var inv=Page("salesInvoices",new{key=id,invoiceAmount=Money(100),balanceDue=Money(60)});
  var receipt=Page("receipts",new{key=Guid.NewGuid(),amount=Money(40)});
  var payment=Page("payments",new{key=Guid.NewGuid(),amount=Money(10)});
  string Reply(string req)=>req.StartsWith("GET /api2/text-custom-fields?")?Marker():req.StartsWith("GET /api2/customers")?Customers():req.StartsWith("GET /api2/chart-of-accounts?")?Accounts():req.StartsWith("GET /api2/sales-invoices?")?inv:req.StartsWith("GET /api2/receipts?")?receipt:req.StartsWith("GET /api2/payments?")?payment:body;
  var requests=new List<string>();
  await AccountingProbeTests.WithServer(200,"{}",async(url,_)=>{
   check((await s.SaveAccountingProfileAsync(url,key)).Success,"detail profile saved");
   check((await s.InspectFinancialDetailsAsync()).Outcome=="MarkerNotEnrolled","detail requires marker");
   check((await s.ReconcileSavedSyntheticAsync(evidence)).Differences.Contains("MarkerNotEnrolled"),"reconciliation requires enrolled marker");
   check((await s.EnrollOrVerifyMarkerAsync(true)).Success,"detail marker enrolled");
   check((await s.InspectFinancialDetailsAsync()).Outcome=="NotSynchronized","detail requires directory");
   var synced=await s.SyncCustomerDirectoryAsync();
   check(synced.Outcome=="Synchronized","detail directory synchronized");
   check((await s.ReconcileSavedSyntheticAsync(evidence)).Differences.Contains("SyntheticDirectoryRequired"),"reconciliation refuses other business before API access");
   check((await s.InspectFinancialDetailsAsync()).Outcome=="RolesNotConfigured","detail requires both roles");
   var rev=synced.Report!.Revision;
   check((await s.SaveProvisionalAccountRoleAsync(rev,"ServiceIncome",new(income,"Synthetic Income","4000"))).Outcome=="RoleSaved","detail income configured");
   check((await s.SaveProvisionalAccountRoleAsync(rev,"OfficeExpense",new(expense,"Synthetic Expense","6000"))).Outcome=="RoleSaved","detail expense configured");
   var result=await s.InspectFinancialDetailsAsync();
   check(result.Outcome=="Connected"&&result.Report?.Contains("Sales invoice — HTTP 200")==true&&result.Report.Contains("OfficeExpense — HTTP 200"),"five detail checks complete");
   check(result.Report!=null&&!result.Report.Contains(id.ToString())&&!result.Report.Contains("Private description"),"combined report sanitized");
   check(requests.Count==21&&requests.Last().StartsWith("GET /api2/text-custom-fields?"),"detail sequence bounded and final marker checked");
  },21,requests.Add,Reply);
  s.Logout();
  try{await s.ReconcileSavedSyntheticAsync(evidence);throw new Exception("FAIL: reconciliation without session");}catch(UnauthorizedAccessException){check(true,"reconciliation denied without active authorized session");}
 }
}
