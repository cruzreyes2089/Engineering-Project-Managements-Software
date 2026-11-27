using System.Text.Json;
using PracticeManager.Core;
internal static class DueFixtureTests
{
 public static async Task Run(Action<bool,string> check)
 {
  var issue=new DateOnly(2026,11,1);
  ManagerInvoiceTerm? Parse(string json,DateOnly? d=null){using var doc=JsonDocument.Parse(json);return ManagerInvoiceTerms.Parse(doc.RootElement,d??issue);}
  check(Parse("{}")==new ManagerInvoiceTerm("Immediate",issue),"omitted due fields map to immediate");
  check(Parse("{\"DueDateDays\":30}")==new ManagerInvoiceTerm("NetDays",new DateOnly(2026,12,1),30),"observed Net30 fields");
  check(Parse("{\"DueDate\":1,\"DueDateDate\":\"2026-12-15T00:00:00\"}")==new ManagerInvoiceTerm("FixedDate",new DateOnly(2026,12,15)),"observed fixed fields");
  foreach(var json in new[]{"{\"DueDate\":null}","{\"DueDate\":2}","{\"DueDateDays\":-1}","{\"DueDateDays\":\"30\"}","{\"DueDateDays\":30,\"DueDate\":1}","{\"DueDate\":1,\"DueDateDate\":\"2026-10-31T00:00:00\"}","{\"DueDateDays\":30,\"DueDateDays\":31}","{\"dueDateDays\":30}","{\"DueDateMode\":0}"})check(Parse(json)==null,"unsupported term evidence rejected");
  check(Parse("{\"DueDateDays\":30}",DateOnly.MaxValue)==null,"date overflow blocked");
  var ids=new[]{Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid()};var marker=Guid.NewGuid();var customer=Guid.NewGuid();
  var refs=new[]{"CRC-DUE-IMMEDIATE","CRC-DUE-NET30","CRC-DUE-FIXED"};
  var business=new{name="CRC DUE DATE TEST"};
  string J(object v)=>JsonSerializer.Serialize(v);
  var responses=new Dictionary<string,string>{
   ["text-custom-fields"]=J(new{business,skip=0,pageSize=2,totalRecords=1,textCustomFields=new[]{new{key=marker,name="CRC Due Date Test Identity"}}}),
   ["sales-invoices"]=J(new{business,skip=0,pageSize=20,totalRecords=3,salesInvoices=ids.Select((id,i)=>new{key=id,reference=refs[i],issueDate="2026-11-01",invoiceAmount=new{currency="USD",value=100},balanceDue=new{currency="USD",value=100}}).ToArray()})
  };
  for(int i=0;i<3;i++){
   var data=new Dictionary<string,object>{{"Key",ids[i]},{"id",ids[i]},{"Reference",refs[i]},{"IssueDate","2026-11-01T00:00:00"},{"Customer",customer}};
   if(i==1)data["DueDateDays"]=30;
   if(i==2){data["DueDate"]=1;data["DueDateDate"]="2026-12-15T00:00:00";}
   responses["sales-invoice-form/"+ids[i].ToString("D")]=J(data);
  }
  async Task<CollectionPreview> Read(Func<string,int,string>? change=null)
  {
   CollectionPreview? result=null;int count=0;bool get=true;
   await AccountingProbeTests.WithServer(200,"{}",async(url,_)=>result=await new ManagerDesktopProbe().ReadSyntheticDueDateFixtureAsync(url,"synthetic-key",true),requestCount:10,observe:req=>{count++;get&=req.StartsWith("GET /api2/");},responseFor:req=>{var route=req.Split(' ')[1]["/api2/".Length..].Split('?')[0];return change==null?responses[route]:change(route,count);});
   check(get,"due fixture GET only");return result!;
  }
  var ok=await Read();check(ok.Outcome=="ObservedForReview"&&ok.DueDates?.Count==3&&ok.TotalOutstanding==300m,"exact USD due fixture acquired twice");
  var report=CollectionDueAging.Calculate(ok,ok.DueDates!,new DateOnly(2026,11,26));
  check(report.KnownOverdue==100m&&report.NotYetDue==200m&&report.UnknownDue==0m,"observed due fixture matches November 26 UI status");
  check(ok.DueFixtureMarker==marker,"fixture marker retained only in observation");
  var when=ok.ObservedUtc!.Value.AddSeconds(1);
  var fresh=ok with{ObservedUtc=when,DueDates=ok.DueDates!.Select(d=>d with{ObservedUtc=when}).ToArray()};
  var today=new DateOnly(2026,11,26);
  var reminder=CollectionReminderReview.Prepare(ok,fresh,ids[0],today,when);
  check(reminder.Outcome=="InternalReviewPrepared"&&!reminder.MaySend&&reminder.Body!.Contains("DO NOT SEND")&&reminder.Body.Contains("USD 100.00")&&reminder.Body.Contains("25")&&!reminder.Body.Contains(customer.ToString()),"overdue fixture reminder preview only, no customer ID");
  check(CollectionReminderReview.Prepare(ok,fresh,ids[1],today,when).Outcome=="InvoiceNotOverdue","not-yet-due invoice excluded from reminder");
  check(CollectionReminderReview.Prepare(ok,fresh,ids[1],new DateOnly(2026,12,1),when).Outcome=="InvoiceNotOverdue","due-today invoice excluded from reminder");
  check(CollectionReminderReview.Prepare(ok,ok,ids[0],today,when).Outcome=="FreshObservationRequired","cached balance cannot prepare reminder");
  check(CollectionReminderReview.Prepare(ok,fresh,ids[0],today,when.AddMinutes(3)).Outcome=="FreshObservationRequired","stale recheck cannot prepare reminder");
  check(CollectionReminderReview.Prepare(ok,fresh with{DueFixtureMarker=Guid.NewGuid()},ids[0],today,when).Outcome=="CompleteSameFixtureEvidenceRequired","different marker blocks reminder");
  var changed=fresh with{Invoices=fresh.Invoices!.Select(x=>x.Key==ids[0]?x with{Outstanding=50m}:x).ToArray()};
  check(CollectionReminderReview.Prepare(ok,changed,ids[0],today,when).Outcome=="InvoiceChangedRefreshRequired","changed balance blocks reminder");
  check(CollectionReminderReview.Prepare(ok,fresh with{DueDates=null},ids[0],today,when).Outcome=="CompleteSameFixtureEvidenceRequired","missing due evidence blocks reminder");
  check((await Read((r,_)=>r=="sales-invoices"?responses[r].Replace("\"USD\"","null"):responses[r])).Outcome=="CollectionRegisterIncompleteOrInvalid","dedicated fixture does not accept null currency");
  check((await Read((r,_)=>r.StartsWith("sales-invoice-form")?responses[r].Replace("\"DueDateDays\":30","\"DueDateDays\":31"):responses[r])).Outcome=="DueFixtureTermsMismatch","changed net days blocked");
  check((await Read((r,_)=>r=="text-custom-fields"?responses[r].Replace("CRC DUE DATE TEST","Other"):responses[r])).Outcome=="CollectionMarkerMismatch","wrong business blocked");
  check((await Read((r,n)=>r=="text-custom-fields"&&n==10?responses[r].Replace(marker.ToString(),Guid.NewGuid().ToString()):responses[r])).Outcome=="CollectionMarkerMismatch","changed marker blocked");
 }
}
