using PracticeManager.Core;
using PracticeManager.Persistence;
internal static class InvoiceDraftTests
{
 public static void Run(string path,string pw,Action<bool,string> check)
 {
  var s=new LocalPractice(path);s.Bootstrap("Synthetic Billing Firm","admin",pw,"CL");check(s.Login("admin",pw),"invoice admin authenticated");
  void Invalid(Action a,string label){try{a();}catch(ArgumentException){check(true,label);return;}throw new Exception("FAIL: "+label);}
  void Denied(Action a,string label){try{a();}catch(UnauthorizedAccessException){check(true,label);return;}throw new Exception("FAIL: "+label);}
  var role=s.ReadStaffRoles().Single(r=>r.Name=="Engineer");s.SetStaffGrant(role.Id,"Work.Own",true,false);var account=s.CreateStaffAccount("worker",role.Id,"Synthetic-worker-temp-0001");var employee=s.CreateEmployee(new("Synthetic","Worker","","",true),account);
  var worker=new LocalPractice(path);check(worker.ChangeRequiredPassword("worker","Synthetic-worker-temp-0001","Synthetic-worker-password-0001")&&worker.Login("worker","Synthetic-worker-password-0001"),"invoice employee authenticated");
  var client=s.SaveClient(new Client(0,"","Business / Company","Synthetic Client","","","","","","","","","","","Active"));var prop=s.CreateProposal(new(client,"Synthetic","Site","Scope",500m));var hash=ConflictReviewTests.Issue(s,prop,1);var project=s.RecordProposalDecision(prop,hash,true,"Synthetic").ProjectId!.Value;
  var a=s.RecordSignedAgreement(project,hash,"signed.pdf","%PDF-1.7\nSynthetic\n%%EOF"u8.ToArray(),"Reviewed",true);ConflictReviewTests.Clear(s,project);s.AuthorizeProjectStart(project,hash,a.Id,a.PdfHash,true,"Synthetic");
  var day=DateOnly.FromDateTime(DateTime.Today);var phase=s.CreateProjectPhase(project,"Design",day.AddDays(-1),day.AddDays(1));var task=s.CreateProjectTask(project,new(phase,employee,"Design",day.AddDays(-1),day.AddDays(1),120));worker.AdvanceMyTask(task,1,"InProgress");
  var time=worker.SubmitWork(new(Guid.NewGuid(),task,day,"Time",61,null,null,"Design time"));var expense=worker.SubmitWork(new(Guid.NewGuid(),task,day,"Expense",null,12.34m,"Supplies","Receipt reference"));
  Invalid(()=>s.ReadInvoiceCandidates(project,day),"no implicit billing policy");
  s.RecordBillingPolicy(new(project,"Hybrid",100m,500m,true,"Synthetic agreement terms"));
  check(s.ReadInvoiceCandidates(project,day).Count==0,"unreviewed work excluded");
  s.ReviewWork(time,true,"Reviewed");s.ClassifyWork(time,true,"Billable");s.ReviewWork(expense,true,"Reviewed");s.ClassifyWork(expense,true,"Billable");
  check(s.ReadInvoiceCandidates(project,day).Single(x=>x.SourceId==time).Status=="Positive billing rate required","missing rate is explicit");
  check(s.ReadProjectFinancialReport(project,day,day).EstimatedLaborCost==null&&s.ReadProjectFinancialReport(project,day,day).MissingCostRates==1,"missing cost rate does not become zero cost");
  s.RecordEmployeeRate(new(employee,day.AddDays(-1),180m,35m));
  Invalid(()=>s.RecordEmployeeRate(new(employee,day.AddDays(-2),190m,36m)),"backdated rate insertion rejected");
  Denied(()=>worker.ReadEmployeeRates(employee),"employee cannot see rate history");
  Denied(()=>worker.ReadInvoiceDrafts(project),"employee cannot see invoice register");
  check(s.ReadInvoiceCandidates(project,day).Single(x=>x.SourceId==time).Amount==183m,"time pricing exact to cents");
  var input=new InvoiceDraftInput(Guid.NewGuid(),project,day,day.AddDays(30),new[]{new FixedFeeInput("Milestone",100m)},new[]{time,expense});var draft=s.CreateInvoiceDraft(input);var detail=s.ReadInvoiceDraft(project,draft);
  check(detail.Summary.Total==295.34m&&detail.Lines.Count==3,"mixed billing draft total and line history");
  check(s.CreateInvoiceDraft(input)==draft,"draft operation idempotent");
  Invalid(()=>s.CreateInvoiceDraft(input with{DueDate=day.AddDays(31)}),"draft operation conflict");
  Invalid(()=>s.CreateInvoiceDraft(input with{OperationKey=Guid.NewGuid()}),"allocated sources unavailable");
  check(s.ReadInvoiceCandidates(project,day).Count==0,"allocated sources hidden from candidate queue");
  Invalid(()=>s.DecideInvoiceDraft(project,draft,"wrong",true,"Reviewed"),"wrong payload rejected");
  check(s.DecideInvoiceDraft(project,draft,detail.Summary.PayloadHash,false,"Recreate")=="Cancelled","unsent draft cancellation retained");
  check(s.ReadInvoiceCandidates(project,day).Count==2,"cancelled unsent draft releases work allocation");
  var next=s.CreateInvoiceDraft(input with{OperationKey=Guid.NewGuid()});var nextDetail=s.ReadInvoiceDraft(project,next);
  Denied(()=>s.DecideInvoiceDraft(project,next,nextDetail.Summary.PayloadHash,true,"Approved internal draft",true),"invoice self approval disabled by default");
  s.SetOwnerApprovalPolicy(true,null,"Synthetic single-owner authorization");
  Denied(()=>s.DecideInvoiceDraft(project,next,nextDetail.Summary.PayloadHash,true,"Approved internal draft"),"invoice self approval needs explicit confirmation");
  check(s.DecideInvoiceDraft(project,next,nextDetail.Summary.PayloadHash,true,"Approved internal draft",true)=="Approved","draft approved under explicit owner exception without posting");
  check(s.DecideInvoiceDraft(project,next,nextDetail.Summary.PayloadHash,false,"Cancel")=="AlreadyApproved","approved draft cannot silently cancel");
  check(s.ReadOwnerDecisionEvidence().Single().Kind=="Invoice approval"&&s.ReadOwnerDecisionEvidence().Single().PolicyEventId.HasValue,"invoice exception evidence visible");
  Invalid(()=>s.CreateInvoiceDraft(new(Guid.NewGuid(),project,day,day.AddDays(30),new[]{new FixedFeeInput("Over cap",1m)},Array.Empty<long>())),"fixed component contractual cap enforced");
  check(s.ReadInvoiceDrafts(project).Count==2,"invoice draft history retained");
  s.RecordEmployeeRate(new(employee,day.AddDays(1),200m,40m));
  check(s.ReadInvoiceDraft(project,next).Lines.Single(x=>x.Kind=="Time").Amount==183m,"new rate does not alter approved history");
  var ops=s.ReadProjectOperations(project,day,day);check(ops.ApprovedMinutes==61&&ops.SubmittedMinutes==61&&ops.ExpenseSubmissions==1&&ops.StartedTasks==1,"operational report matches approved facts");
  var fin=s.ReadProjectFinancialReport(project,day,day);
  check(fin.ApprovedMinutes==61&&fin.BillableMinutes==61&&fin.EstimatedLaborCost==35.58m&&fin.ApprovedExpenseCost==12.34m&&fin.TotalOperationalCost==47.92m,"operational cost report uses effective rate and cents");
  check(fin.ApprovedInternalDraftValue==295.34m&&fin.CancelledDraftValue==295.34m&&fin.DraftValue==0,"approved and cancelled drafts reported separately");
  Denied(()=>worker.ReadProjectFinancialReport(project,day,day),"employee cannot read cost report");
  Denied(()=>worker.ExportInvoiceDraftCsv(project,next,nextDetail.Summary.PayloadHash),"employee cannot export billing draft");
  Invalid(()=>s.ExportInvoiceDraftCsv(project,draft,detail.Summary.PayloadHash),"cancelled draft cannot export");
  Denied(()=>worker.PrepareInvoiceReviewDocx(project,next,nextDetail.Summary.PayloadHash),"employee cannot generate invoice review document");
  Invalid(()=>s.PrepareInvoiceReviewDocx(project,draft,detail.Summary.PayloadHash),"cancelled invoice draft cannot generate document");
  var doc=s.PrepareInvoiceReviewDocx(project,next,nextDetail.Summary.PayloadHash);var docText=ReviewDocxTests.Text(doc.Bytes);
  check(doc.Id==s.PrepareInvoiceReviewDocx(project,next,nextDetail.Summary.PayloadHash).Id&&docText.Contains("295.34 USD")&&docText.Contains("NOT AN ISSUED INVOICE")&&!docText.Contains("35.58"),"approved invoice review registered, exact totals, no cost data");
  check(s.ReadInvoiceReviewDocuments(project,next).Single().Id==doc.Id&&s.RetrieveInvoiceReviewDocument(project,next,doc.Id,doc.Sha256).Bytes.SequenceEqual(doc.Bytes),"approved draft document register and byte verification");
  Denied(()=>worker.ReadInvoiceReviewDocuments(project,next),"ordinary employee cannot list invoice documents");
  Invalid(()=>s.RetrieveInvoiceReviewDocument(project,next,doc.Id,"wrong"),"wrong invoice document hash denied");
  var csv=s.ExportInvoiceDraftCsv(project,next,nextDetail.Summary.PayloadHash);
  check(csv.Contains("NOT AN ISSUED INVOICE")&&csv.Contains("295.34")&&!csv.Contains("35.58"),"export labeled internal and omits cost rate");
  check(SafeCsv.Text(" =SUM(1,2)").StartsWith("\"'")&&SafeCsv.Text("quote\"").Contains("\"\""),"CSV protects formula text and quotes");
 }
}
