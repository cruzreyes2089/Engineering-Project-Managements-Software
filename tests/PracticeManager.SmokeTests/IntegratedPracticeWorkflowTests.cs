using System.Security.Cryptography;
using PracticeManager.Core;
using PracticeManager.Persistence;
internal static class IntegratedPracticeWorkflowTests
{
 public static void Run(string directory,string pw,Action<bool,string> check)
 {
  var path=Path.Combine(directory,"integrated-practice.db");
  var admin=new LocalPractice(path);admin.Bootstrap("Synthetic integrated practice","admin",pw,"CL");
  check(admin.Login("admin",pw),"integrated administrator login");
  void Invalid(Action a,string label){try{a();}catch(ArgumentException){check(true,label);return;}throw new Exception("FAIL: "+label);}
  void Denied(Action a,string label){try{a();}catch(UnauthorizedAccessException){check(true,label);return;}throw new Exception("FAIL: "+label);}
  var engineer=admin.ReadStaffRoles().Single(x=>x.Name=="Engineer");
  var reviewerRole=admin.ReadStaffRoles().First(x=>x.Name!="Engineer"&&x.Name!="Administrator");
  admin.SetStaffGrant(engineer.Id,"Work.Own",true,false);
  foreach(var grant in new[]{"Work.Review","Billing.Classify","Billing.Invoices.View","Billing.Invoices.Approve","Projects.View","Clients.View","Employees.View"})
   admin.SetStaffGrant(reviewerRole.Id,grant,true,false);
  var workerAccount=admin.CreateStaffAccount("integrated-worker",engineer.Id,"Synthetic-integrated-worker-temp");
  admin.CreateStaffAccount("integrated-reviewer",reviewerRole.Id,"Synthetic-integrated-reviewer-temp");
  var employee=admin.CreateEmployee(new("Synthetic","Engineer","","Engineer",true),workerAccount);
  var worker=new LocalPractice(path);var reviewer=new LocalPractice(path);
  const string workerPw="Synthetic-integrated-worker-password",reviewerPw="Synthetic-integrated-reviewer-password";
  check(worker.ChangeRequiredPassword("integrated-worker","Synthetic-integrated-worker-temp",workerPw)&&worker.Login("integrated-worker",workerPw),"integrated employee credential transition");
  check(reviewer.ChangeRequiredPassword("integrated-reviewer","Synthetic-integrated-reviewer-temp",reviewerPw)&&reviewer.Login("integrated-reviewer",reviewerPw),"integrated independent reviewer credential transition");
  var first=admin.SaveClient(new Client(0,"","Business / Company","Synthetic First","","","","","","","","","","","Active"));
  var second=admin.SaveClient(new Client(0,"","Business / Company","Synthetic Second","","","","","","","","","","","Active"));
  admin.SaveContact(new Contact(0,first,"First","Contact","","","",true));
  admin.SaveContact(new Contact(0,second,"Second","Contact","","","",true));
  check(admin.SearchClients("Synthetic").Count==2&&admin.Contacts(first).Single().First=="First"&&admin.Contacts(second).Single().First=="Second","integrated client and contact separation");
  long Accept(long client,string title,out string hash)
  {
   var proposal=admin.CreateProposal(new(client,title,"Synthetic site","Synthetic scope",600m));hash=ConflictReviewTests.Issue(admin,proposal,1);
   return admin.RecordProposalDecision(proposal,hash,true,"Synthetic acceptance").ProjectId!.Value;
  }
  var project=Accept(first,"Synthetic design",out var issued);var other=Accept(second,"Synthetic other design",out _);
  var day=DateOnly.FromDateTime(DateTime.Today);
  var phase=admin.CreateProjectPhase(project,"Design",day,day);
  var task=admin.CreateProjectTask(project,new(phase,employee,"Foundation design",day,day,TaskEstimate.Parse("3:30")));
  Invalid(()=>worker.AdvanceMyTask(task,1,"InProgress"),"integrated pending project blocks work");
  var agreement=admin.RecordSignedAgreement(project,issued,"synthetic-agreement.pdf","%PDF-1.7\nSynthetic fixture only\n%%EOF"u8.ToArray(),"Synthetic reviewed agreement",true);
  ConflictReviewTests.Clear(admin,project);
  check(admin.AuthorizeProjectStart(project,issued,agreement.Id,agreement.PdfHash,true,"Synthetic start")=="Active","integrated explicit start authorization");
  foreach(var item in admin.ReadProjectSetup(project).Items)admin.RecordProjectSetupItem(project,item.Item,true,"Synthetic setup evidence",item.Complete,item.Note);
  check(admin.ReadProjectOverview(project).State=="Active"&&admin.ReadProjectOverview(other).State=="Pending","integrated project context remains distinct");
  var file="Synthetic drawing notes"u8.ToArray();var document=admin.RegisterProjectDocument(project,Guid.NewGuid(),null,"Production","Drawings","design.txt",file);
  var row=admin.ReadProjectDocuments(project).Single();
  check(row.Id==document&&admin.RetrieveProjectDocument(project,document,row.Hash).Bytes.SequenceEqual(file)&&admin.ReadProjectDocuments(other).Count==0,"integrated document identity and project isolation");
  Denied(()=>admin.RetrieveProjectDocument(other,document,row.Hash),"integrated wrong-project document retrieval denied");
  var issue=admin.CreateQualityItem(project,"Design review","Correction","Synthetic check");
  Invalid(()=>admin.RecordProjectLifecycle(project,null,true,"Premature close",true),"integrated open task and QA block close");
  worker.AdvanceMyTask(task,1,"InProgress");
  var time=worker.SubmitWork(new(Guid.NewGuid(),task,day,"Time",WorkDuration.Parse("2","30"),null,null,"Synthetic design"));
  var expense=worker.SubmitWork(new(Guid.NewGuid(),task,day,"Expense",null,15m,"Supplies","Synthetic receipt"));
  var rejected=worker.SubmitWork(new(Guid.NewGuid(),task,day,"Time",30,null,null,"Incorrect synthetic entry"));
  admin.RecordBillingPolicy(new(project,"Hourly",0,600m,true,"Synthetic reviewed terms"));
  admin.RecordEmployeeRate(new(employee,day,180m,35m));
  check(admin.ReadInvoiceCandidates(project,day).Count==0,"integrated unapproved facts excluded from invoice");
  Denied(()=>worker.ReviewWork(time,true,"Unauthorized"),"integrated employee approval denied");
  Denied(()=>worker.ReadProjectFinancialReport(project,day,day),"integrated employee cost report denied");
  check(reviewer.ReviewWork(rejected,false,"Incorrect synthetic entry")=="Rejected","integrated independent rejection");
  var replacement=worker.ReplaceRejectedWork(rejected,new(Guid.NewGuid(),task,day,"Time",15,null,null,"Corrected actual time"));
  check(reviewer.ReviewWork(time,true,"Reviewed")=="Approved"&&reviewer.ReviewWork(expense,true,"Reviewed")=="Approved"&&reviewer.ReviewWork(replacement,true,"Reviewed correction")=="Approved","integrated independent fact approvals");
  check(admin.ReadInvoiceCandidates(project,day).Count==0,"integrated approval does not classify billing");
  reviewer.ClassifyWork(time,true,"Billable design");reviewer.ClassifyWork(expense,true,"Contract expense");reviewer.ClassifyWork(replacement,false,"Nonbillable correction");
  var candidates=admin.ReadInvoiceCandidates(project,day);
  check(candidates.Count==2&&candidates.All(x=>x.Status=="Eligible")&&candidates.Sum(x=>x.Amount)==465m,"integrated eligible source amount");
  var draft=admin.CreateInvoiceDraft(new(Guid.NewGuid(),project,day,day.AddDays(30),Array.Empty<FixedFeeInput>(),new[]{time,expense}));
  var detail=admin.ReadInvoiceDraft(project,draft);
  Denied(()=>admin.DecideInvoiceDraft(project,draft,detail.Summary.PayloadHash,true,"Self approval",true),"integrated no automatic owner exception");
  check(reviewer.DecideInvoiceDraft(project,draft,detail.Summary.PayloadHash,true,"Independent approval")=="Approved","integrated independent draft approval");
  check(admin.ReadInvoiceCandidates(project,day).Count==0,"integrated duplicate allocation prevented");
  var finance=admin.ReadProjectFinancialReport(project,day,day);
  check(finance.ApprovedMinutes==165&&finance.BillableMinutes==150&&finance.NonbillableMinutes==15&&finance.TotalOperationalCost==111.25m&&finance.ApprovedInternalDraftValue==465m,"integrated exact cost and draft reconciliation");
  check(admin.ReadProjectFinancialReport(other,day,day).ApprovedMinutes==0&&admin.ReadInvoiceDrafts(other).Count==0,"integrated financial project scope");
  worker.AdvanceMyTask(task,2,"Completed");admin.CloseQualityItem(project,issue,"Synthetic verified resolution");
  var closed=admin.RecordProjectLifecycle(project,null,true,"Synthetic operational close",true);
  check(closed.State=="OperationalClosed"&&admin.ReadProjectOverview(project).State=="OperationalClosed","integrated operational closure");
  Invalid(()=>admin.CreateProjectPhase(project,"New work",day,day),"integrated closure blocks new planning");
  var backup=Path.Combine(directory,"integrated-practice-recovery.db");var recovered=Path.Combine(directory,"integrated-practice-restored.db");
  var copy=admin.CreateRecoveryCopy(backup);admin.RestoreRecoveryCopyToNewFile(backup,copy.Sha256,recovered);
  var restored=new LocalPractice(recovered);check(restored.Login("admin",pw),"integrated recovered practice opens");
  check(restored.ReadProjectOverview(project).State=="OperationalClosed"&&restored.ReadProjectOverview(other).State=="Pending","integrated restored lifecycle states");
  check(restored.ReadInvoiceDraft(project,draft).Summary.PayloadHash==detail.Summary.PayloadHash&&restored.ReadProjectFinancialReport(project,day,day)==finance,"integrated restored financial evidence exact");
  check(restored.RetrieveProjectDocument(project,document,row.Hash).Bytes.SequenceEqual(file),"integrated restored document exact");
  check(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(backup))).ToLowerInvariant()==copy.Sha256,"integrated recovery source immutable");
  check(admin.ReadProjectOverview(project).State=="OperationalClosed"&&admin.ReadInvoiceDrafts(project).Count==1,"integrated original remains intact");
 }
}
