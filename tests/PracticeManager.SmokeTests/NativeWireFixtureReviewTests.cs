using System.Text.Json;
using System.Text.Json.Nodes;
using PracticeManager.Core;
internal static class NativeWireFixtureReviewTests
{
 public static async Task Run(Action<bool,string> check)
 {
  var marker=Guid.NewGuid();var customer=Guid.NewGuid();var bank=Guid.NewGuid();var iv=Guid.NewGuid();var rc=Guid.NewGuid();var pm=Guid.NewGuid();
  var day=new DateOnly(2026,11,25);
  var invoice=new SyntheticWireIntent(Guid.NewGuid(),marker,RehearsalKind.Invoice,Guid.NewGuid(),125m,day,"USD",customer);
  var receipt=new SyntheticWireIntent(Guid.NewGuid(),marker,RehearsalKind.Receipt,Guid.NewGuid(),50m,day,"USD",customer,bank,iv);
  var payment=new SyntheticWireIntent(Guid.NewGuid(),marker,RehearsalKind.ExpensePayment,Guid.NewGuid(),20m,day,"USD",CashAccountKey:bank,VendorContact:"Synthetic vendor");
  var business=new{name="CRC NATIVE WRITE TEST"};
  object Money(decimal value)=>new{currency=(string?)null,value};
  string J(object value)=>JsonSerializer.Serialize(value);
  string Form(SyntheticWireIntent x,Guid key){
   var obj=JsonNode.Parse(SyntheticWireCompiler.Compile(x).Json)!.AsObject();
   var date=x.Kind==RehearsalKind.Invoice?"IssueDate":"Date";obj[date]=obj[date]!.GetValue<string>()+"T00:00:00";
   obj["Key"]=key.ToString("D");obj["id"]=key.ToString("D");obj["text"]="Synthetic display";obj["UniqueName"]="Synthetic display";
   obj["TaxCodeEnabled"]=true;obj["ProjectEnabled"]=true;return obj.ToJsonString();
  }
  var responses=new Dictionary<string,string>{
   ["text-custom-fields"]=J(new{business,skip=0,pageSize=2,totalRecords=1,textCustomFields=new[]{new{key=marker,name="CRC Native Write Test Identity"}}}),
   ["sales-invoices"]=J(new{business,skip=0,pageSize=20,totalRecords=1,salesInvoices=new[]{new{key=iv,invoiceAmount=Money(125),balanceDue=Money(75)}}}),
   ["receipts"]=J(new{business,skip=0,pageSize=20,totalRecords=1,receipts=new[]{new{key=rc,amount=Money(50)}}}),
   ["payments"]=J(new{business,skip=0,pageSize=20,totalRecords=1,payments=new[]{new{key=pm,amount=Money(20)}}}),
   [$"sales-invoice-form/{iv}"]=Form(invoice,iv),[$"receipt-form/{rc}"]=Form(receipt,rc),[$"payment-form/{pm}"]=Form(payment,pm)
  };
  var target=SyntheticWireCompiler.Compile(payment).RecoveryTarget(pm);
  var probe=new ManagerDesktopProbe();
  async Task<SyntheticRecoveryAssessment> Run(SyntheticRecoveryTarget? t=null,Func<string,int,string>? change=null){
   SyntheticRecoveryAssessment? result=null;int count=0;bool getOnly=true;
   await AccountingProbeTests.WithServer(200,"{}",async(url,_)=>result=await probe.ReviewNativeWireFixtureAsync(url,"synthetic-key",t??target,true),
    requestCount:14,observe:req=>{count++;getOnly&=req.StartsWith("GET /api2/",StringComparison.Ordinal);},
    responseFor:req=>{var route=req.Split(' ')[1]["/api2/".Length..].Split('?')[0];return change==null?responses[route]:change(route,count);});
   check(getOnly&&!result!.MayRetry&&!result.MayReleaseHold,"native fixture review uses GET only and never authorizes replay");
   return result!;
  }
  check((await Run()).Finding==SyntheticRecoveryFinding.MatchedForReview,"native fixture payment detail reference resolves without register reference");
  check((await Run(SyntheticWireCompiler.Compile(invoice).RecoveryTarget(iv))).Finding==SyntheticRecoveryFinding.MatchedForReview,"native fixture invoice exact detail match");
  check((await Run(SyntheticWireCompiler.Compile(receipt).RecoveryTarget(rc))).Finding==SyntheticRecoveryFinding.MatchedForReview,"native fixture receipt exact detail match");
  check((await Run(target with{MarkerKey=Guid.NewGuid()})).Reason=="NativeFixtureMarkerMismatch","native fixture wrong marker blocked");
  check((await Run(change:(route,_)=>route=="text-custom-fields"?responses[route].Replace("CRC NATIVE WRITE TEST","CRC ACCOUNTING TEST"):responses[route])).Reason=="NativeFixtureMarkerMismatch","native fixture wrong business blocked");
  check((await Run(change:(route,_)=>route=="payments"?responses[route].Replace("\"totalRecords\":1","\"totalRecords\":21"):responses[route])).Reason=="NativeFixtureRegisterIncompleteOrInvalid","native fixture incomplete page blocked");
  check((await Run(change:(route,_)=>route==$"payment-form/{pm}"?Form(payment with{Amount=21m},pm):responses[route])).Reason=="NativeFixtureRegisterAmountMismatch","native fixture register and form amounts must agree");
  check((await Run(change:(route,count)=>route==$"payment-form/{pm}"&&count>7?Form(payment with{VendorContact="Changed vendor"},pm):responses[route])).Reason=="NativeFixtureChanged","native fixture second sweep drift blocked");
  check((await Run(change:(route,_)=>route==$"payment-form/{pm}"?Form(payment with{VendorContact="Changed vendor"},pm):responses[route])).Finding==SyntheticRecoveryFinding.Conflict,"native fixture independent fingerprint mismatch");
  var absent=SyntheticWireCompiler.Compile(payment with{OperationKey=Guid.NewGuid()}).RecoveryTarget();
  check((await Run(absent)).Finding==SyntheticRecoveryFinding.NoMatchObserved,"native fixture no match never authorizes retry");
  check((await probe.ReviewNativeWireFixtureAsync("http://127.0.0.1:1/api2","synthetic-key",target,false)).Reason=="OperatorScopeEvidenceRequired","native fixture currency assertion required before GET");
 }
}
