using System.Text.Json;
using PracticeManager.Core;
internal static class CollectionsReviewTests
{
 public static async Task Run(Action<bool,string> check)
 {
  var marker=Guid.NewGuid();var invoice=Guid.NewGuid();var customer=Guid.NewGuid();var business=new{name="CRC ACCOUNTING TEST"};
  string J(object o)=>JsonSerializer.Serialize(o);
  object Money(decimal n)=>new{currency=(string?)null,value=n};
  var responses=new Dictionary<string,string>{
   ["text-custom-fields"]=J(new{business,skip=0,pageSize=2,totalRecords=1,textCustomFields=new[]{new{key=marker,name="CRC Integration Identity Test"}}}),
   ["sales-invoices"]=J(new{business,skip=0,pageSize=20,totalRecords=1,salesInvoices=new[]{new{key=invoice,reference="TEST-1",invoiceAmount=Money(100),balanceDue=Money(60)}}}),
   [$"sales-invoice-form/{invoice}"]=J(new{Key=invoice,id=invoice,Reference="TEST-1",IssueDate="2026-10-01T00:00:00",Customer=customer})
  };
  var probe=new ManagerDesktopProbe();
  async Task<CollectionPreview> Read(Func<string,int,string>? modify=null,Guid? expected=null){
   CollectionPreview? result=null;int count=0;bool get=true;
   await AccountingProbeTests.WithServer(200,"{}",async(url,_)=>result=await probe.ReadSyntheticCollectionsAsync(url,"synthetic-key",expected??marker,true),requestCount:6,observe:req=>{count++;get&=req.StartsWith("GET /api2/");},responseFor:req=>{var route=req.Split(' ')[1]["/api2/".Length..].Split('?')[0];return modify==null?responses[route]:modify(route,count);});
   check(get,"collections GET only");return result!;
  }
  var ok=await Read();check(ok.Outcome=="ObservedForReview"&&ok.TotalOutstanding==60m&&ok.Invoices!.Single().Reference=="TEST-1","collection register balance and invoice identity observed twice");
  check((await Read(expected:Guid.NewGuid())).Outcome=="CollectionMarkerMismatch","collections wrong marker blocked");
  check((await Read((route,_)=>route=="sales-invoices"?responses[route].Replace("\"totalRecords\":1","\"totalRecords\":21"):responses[route])).Outcome=="CollectionRegisterIncompleteOrInvalid","truncated collection register blocked");
  check((await Read((route,_)=>route.StartsWith("sales-invoice-form")?responses[route].Replace(invoice.ToString(),Guid.NewGuid().ToString()):responses[route])).Outcome=="CollectionInvoiceIdentityMismatch","wrong detail identity blocked");
  check((await Read((route,_)=>route.StartsWith("sales-invoice-form")?responses[route].Replace("T00:00:00","T12:00:00"):responses[route])).Outcome=="CollectionIssueDateUnverified","unverified issue date blocked");
  check((await Read((route,count)=>route=="sales-invoices"&&count>3?responses[route].Replace("\"value\":60","\"value\":61"):responses[route])).Outcome=="CollectionRegisterChanged","balance drift blocks publication");
  check((await Read((route,_)=>route.StartsWith("sales-invoice-form")?responses[route].Replace("TEST-1","OTHER"):responses[route])).Outcome=="CollectionReferenceMismatch","register reference mismatch blocked");
  check((await probe.ReadSyntheticCollectionsAsync("http://127.0.0.1:1/api2","synthetic-key",marker,false)).Outcome=="OperatorScopeEvidenceRequired","no implied currency");
  check(ok.Invoices!.Single().CustomerKey==customer,"collection detail customer identity retained");
  check((await Read((route,_)=>route.StartsWith("sales-invoice-form")?responses[route].Replace(customer.ToString(),"invalid"):responses[route])).Outcome=="CollectionCustomerIdentityInvalid","invalid customer identity blocks acquisition");
  var draft=new InvoiceDraftSummary(1,2,"DRAFT-1",new DateOnly(2026,10,1),new DateOnly(2026,10,31),100m,"Approved","synthetic");
  var candidate=InvoiceCandidateComparison.Compare(draft,"TEST-1",customer,ok);
  check(candidate.Outcome=="CandidateForManualReview"&&candidate.ObservedTotal==100m&&candidate.Outstanding==60m&&!candidate.MayPost&&!candidate.MayMarkIssued&&!candidate.MaySendReminder,"exact candidate match does not authorize posting or issuance");
  check(InvoiceCandidateComparison.Compare(draft,"test-1",customer,ok).Outcome=="ExactReferenceMismatch","case-only reference collision not accepted");
  check(InvoiceCandidateComparison.Compare(draft,"OTHER",customer,ok).Outcome=="ReferenceNotObserved","missing reference not interpreted as unposted");
  check(InvoiceCandidateComparison.Compare(draft,"TEST-1",Guid.NewGuid(),ok).Outcome=="CustomerMismatch","wrong customer rejected");
  check(InvoiceCandidateComparison.Compare(draft,"TEST-1",customer,ok with{Invoices=new[]{ok.Invoices!.Single() with{CustomerKey=null}}}).Outcome=="CustomerIdentityUnavailable","missing customer evidence fails closed");
  check(InvoiceCandidateComparison.Compare(draft with{Total=101m},"TEST-1",customer,ok).Outcome=="InvoiceTotalMismatch","wrong total rejected");
  check(InvoiceCandidateComparison.Compare(draft with{InvoiceDate=draft.InvoiceDate.AddDays(1)},"TEST-1",customer,ok).Outcome=="IssueDateMismatch","wrong issue date rejected");
  check(InvoiceCandidateComparison.Compare(draft with{State="Draft"},"TEST-1",customer,ok).Outcome=="ApprovedDraftAndExactReferenceRequired","unapproved draft rejected");
  check(InvoiceCandidateComparison.Compare(draft,"TEST-1",customer,ok with{Invoices=new[]{ok.Invoices!.Single(),ok.Invoices!.Single() with{Key=Guid.NewGuid(),Reference="test-1"}}}).Outcome=="DuplicateReference","duplicate case-insensitive reference rejected");
  check(InvoiceCandidateComparison.Compare(draft,"TEST-1",customer,new("Blocked")).Outcome=="CompleteObservationRequired","unavailable acquisition not a match");
  var today=new DateOnly(2026,11,26);CollectionInvoice Row(int days,decimal balance)=>new(Guid.NewGuid(),"Synthetic",today.AddDays(-days),100m,balance);
  var sample=new CollectionPreview("ObservedForReview",new[]{Row(-1,1),Row(0,2),Row(30,3),Row(31,4),Row(60,5),Row(61,6),Row(90,7),Row(91,8),Row(100,0)},DateTimeOffset.UtcNow);
  var a=CollectionAging.Calculate(sample,today);
  check(a.Rows.Count==8&&a.Outstanding==36m&&a.FutureDated==1m&&a.Age0To30==5m&&a.Age31To60==9m&&a.Age61To90==13m&&a.AgeOver90==8m,"issue-age boundaries and sum");
  check(a.Rows.Single(x=>x.AgeBand=="Future issue date").DaysSinceIssue==null,"future date not overdue");
  try{CollectionAging.Calculate(new("Blocked"),today);throw new Exception("FAIL: unavailable data");}catch(ArgumentException){check(true,"unavailable collections not interpreted as zero");}
 }
}
