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
  var client=s.SaveClient(new Client(0,"","Business / Company","Synthetic Client","","","","","","","","","","","Active"));var prop=s.CreateProposal(new(client,"Synthetic","Site","Scope",500m));var hash=s.RecordProposalIssued(prop,1);var project=s.RecordProposalDecision(prop,hash,true,"Synthetic").ProjectId!.Value;
  var a=s.RecordSignedAgreement(project,hash,"signed.pdf","%PDF-1.7\nSynthetic\n%%EOF"u8.ToArray(),"Reviewed",true);s.AuthorizeProjectStart(project,hash,a.Id,a.PdfHash,true,"Synthetic");
  var day=DateOnly.FromDateTime(DateTime.Today);var phase=s.CreateProjectPhase(project,"Design",day.AddDays(-1),day.AddDays(1));var task=s.CreateProjectTask(project,new(phase,employee,"Design",day.AddDays(-1),day.AddDays(1),120));worker.AdvanceMyTask(task,1,"InProgress");
  var time=worker.SubmitWork(new(Guid.NewGuid(),task,day,"Time",61,null,null,"Design time"));var expense=worker.SubmitWork(new(Guid.NewGuid(),task,day,"Expense",null,12.34m,"Supplies","Receipt reference"));
  Invalid(()=>s.ReadInvoiceCandidates(project,day),"no implicit billing policy");
  s.RecordBillingPolicy(new(project,"Hybrid",100m,500m,true,"Synthetic agreement terms"));
  check(s.ReadInvoiceCandidates(project,day).Count==0,"unreviewed work excluded");
  s.ReviewWork(time,true,"Reviewed");s.ClassifyWork(time,true,"Billable");s.ReviewWork(expense,true,"Reviewed");s.ClassifyWork(expense,true,"Billable");
  check(s.ReadInvoiceCandidates(project,day).Single(x=>x.SourceId==time).Status=="Positive billing rate required","missing rate is explicit");
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
  check(s.DecideInvoiceDraft(project,next,nextDetail.Summary.PayloadHash,true,"Approved internal draft")=="Approved","draft approved without posting");
  check(s.DecideInvoiceDraft(project,next,nextDetail.Summary.PayloadHash,false,"Cancel")=="AlreadyApproved","approved draft cannot silently cancel");
  Invalid(()=>s.CreateInvoiceDraft(new(Guid.NewGuid(),project,day,day.AddDays(30),new[]{new FixedFeeInput("Over cap",1m)},Array.Empty<long>())),"fixed component contractual cap enforced");
  check(s.ReadInvoiceDrafts(project).Count==2,"invoice draft history retained");
  s.RecordEmployeeRate(new(employee,day.AddDays(1),200m,40m));
  check(s.ReadInvoiceDraft(project,next).Lines.Single(x=>x.Kind=="Time").Amount==183m,"new rate does not alter approved history");
 }
}
