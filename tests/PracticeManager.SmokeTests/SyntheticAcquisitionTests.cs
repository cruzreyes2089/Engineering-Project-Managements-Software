using System.Text.Json;
using PracticeManager.Core;
internal static class SyntheticAcquisitionTests
{
 public static async Task Run(Action<bool,string> check)
 {
  var marker=Guid.NewGuid();var income=Guid.NewGuid();var expense=Guid.NewGuid();var ar=Guid.NewGuid();var customer=Guid.NewGuid();var bank=Guid.NewGuid();var iv=Guid.NewGuid();var rc=Guid.NewGuid();var pm=Guid.NewGuid();var ig=Guid.NewGuid();var eg=Guid.NewGuid();
  var business=new{name="CRC ACCOUNTING TEST"};
  object Money(decimal value)=>new{currency=(string?)null,value};
  string J(object o)=>JsonSerializer.Serialize(o);
  var responses=new Dictionary<string,string>{
   ["text-custom-fields"]=J(new{business,skip=0,pageSize=2,totalRecords=1,textCustomFields=new[]{new{key=marker,name=BusinessMarker.TestName}}}),
   ["sales-invoices"]=J(new{business,skip=0,pageSize=20,totalRecords=1,salesInvoices=new[]{new{key=iv,invoiceAmount=Money(100),balanceDue=Money(60)}}}),
   ["receipts"]=J(new{business,skip=0,pageSize=20,totalRecords=1,receipts=new[]{new{key=rc,amount=Money(40),receivedIn=new{key=bank}}}}),
   ["payments"]=J(new{business,skip=0,pageSize=20,totalRecords=1,payments=new[]{new{key=pm,amount=Money(10)}}}),
   ["customers"]=J(new{business,skip=0,pageSize=20,totalRecords=1,customers=new[]{new{key=customer,name="CRC Test Customer 001",status="Active"}}}),
   ["chart-of-accounts"]=J(new{business,totalRecords=3,chartOfAccounts=new[]{new{key=income,name="Engineering Services Income",code=(string?)"4000"},new{key=expense,name="Office Supplies",code=(string?)"6000"},new{key=ar,name="Accounts receivable",code=(string?)null}}}),
   ["bank-and-cash-accounts"]=J(new{business,skip=0,pageSize=20,totalRecords=1,bankAndCashAccounts=new[]{new{key=bank,name="CRC Test Cash"}}}),
   [$"sales-invoice-form/{iv}"]=J(new{Key=iv,id=iv,Customer=customer,TaxCodeEnabled=true,ProjectEnabled=true,Lines=new[]{new{Account=income,Qty=1,SalesUnitPrice=100}}}),
   [$"receipt-form/{rc}"]=J(new{Key=rc,id=rc,TaxCodeEnabled=true,ProjectEnabled=true,ReceivedIn=bank,Lines=new[]{new{Account=ar,AccountsReceivableSalesInvoice=iv,Amount=40}}}),
   [$"payment-form/{pm}"]=J(new{Key=pm,id=pm,TaxCodeEnabled=true,ProjectEnabled=true,PaidFrom=bank,Lines=new[]{new{Account=expense,Amount=10}}}),
   [$"profit-and-loss-statement-account-form/{income}"]=J(new{Key=income,id=income,Group=ig,Code="4000",Name="Engineering Services Income",IsProfitAndLossAccount=true}),
   [$"profit-and-loss-statement-account-form/{expense}"]=J(new{Key=expense,id=expense,Group=eg,Code="6000",Name="Office Supplies",IsProfitAndLossAccount=true}),
   [$"profit-and-loss-statement-group-form/{ig}"]=J(new{Name="Income"}),
   [$"profit-and-loss-statement-group-form/{eg}"]=J(new{Name="Expenses"})
  };
  var summary=new SyntheticSummaryEvidence(new(60,30,100,10,90,0,90,90),true);
  var probe=new ManagerDesktopProbe();
  async Task<TestLedgerReconciliationResult> Run(SyntheticSummaryEvidence evidence,Func<string,int,string>? change=null,Guid? expectedMarker=null)
  {
   TestLedgerReconciliationResult? result=null;int count=0;bool allGet=true;
   await AccountingProbeTests.WithServer(200,"{}",async (url,_)=>result=await probe.ReconcileSyntheticAsync(url,"synthetic-key",expectedMarker??marker,income,expense,evidence),requestCount:28,observe:req=>{count++;allGet&=req.StartsWith("GET /api2/",StringComparison.Ordinal);},responseFor:req=>{
    var route=req.Split(' ')[1]["/api2/".Length..].Split('?')[0];
    return change==null?responses[route]:change(route,count);
   });
   check(allGet,"acquisition uses only fixed GET routes");
   return result!;
  }
  check((await Run(summary)).Matches,"two-sweep native fixture acquisition reconciles independent balances");
  check((await Run(summary with{Balances=summary.Balances with{Cash=31}})).Outcome=="Mismatch","native acquisition detects independent summary mismatch");
  check((await Run(summary,expectedMarker:Guid.NewGuid())).Differences.Contains("MarkerMismatch"),"native acquisition rejects wrong enrolled marker");
  check((await Run(summary,(route,count)=>route=="receipts"?responses[route].Replace(bank.ToString(),Guid.NewGuid().ToString()):responses[route])).Differences.Contains("FixtureBankMismatch"),"native acquisition rejects receipt bank mismatch");
  check((await Run(summary,(route,count)=>route==$"sales-invoice-form/{iv}"&&count>14?responses[route].TrimEnd('}')+",\"Description\":\"changed\"}":responses[route])).Differences.Contains("FixtureChanged"),"second sweep drift detected");
  check((await Run(summary,(route,count)=>route==$"profit-and-loss-statement-group-form/{ig}"?J(new{Name="Expenses"}):responses[route])).Differences.Contains("FixtureGroups"),"wrong group name blocked");
  check((await Run(summary,(route,count)=>route=="customers"?responses[route].Replace("\"totalRecords\":1","\"totalRecords\":2"):responses[route])).Differences.Contains("IncompleteFixtureCustomers"),"incomplete customer register rejected");
  check((await probe.ReconcileSyntheticAsync("http://127.0.0.1:1/api2","synthetic-key",marker,income,expense,summary with{SingleCurrencyAndZeroOpeningConfirmed=false})).Differences.Contains("OperatorEvidenceRequired"),"operator evidence required before network access");
 }
}
