using PracticeManager.Core;
using PracticeManager.Persistence;
internal static class SoloPracticeWorkflowTests
{
 public static void Run(string path,string password,Action<bool,string> check)
 {
  // Ordinary schema 47, fresh firm. No optional document-management activation,
  // external accounting connection, live credentials or legacy import.
  var s=new LocalPractice(path);
  s.Bootstrap("Synthetic solo practice","admin",password,"CL");
  check(s.Login("admin",password)&&!s.IsDocumentAcceptance,"solo workflow uses ordinary local workspace");
  var employee=s.CreateEmployee(new("Synthetic","Owner","","",true),s.ReadStaffAccounts().Single().Id);
  var client=s.SaveClient(new Client(0,"","Business / Company","Synthetic client","","","","","","","","","","","Active"));
  s.SaveContact(new Contact(0,client,"Synthetic","Contact","","","",true));
  var proposal=s.CreateProposal(new(client,"Residential design fixture","Synthetic site","Synthetic fixed-fee scope",1000m));
  var hash=ConflictReviewTests.Issue(s,proposal,1);
  var decision=s.RecordProposalDecision(proposal,hash,true,"Synthetic acceptance");
  var project=decision.ProjectId!.Value;
  check(s.ReadProjectDashboardCounts()==(1L,0L)&&s.ReadClientCount()==1,"solo workflow acceptance creates exactly one pending project");
  var agreement=s.RecordSignedAgreement(project,hash,"signed.pdf","%PDF-1.7\nSynthetic signed agreement\n%%EOF"u8.ToArray(),"Reviewed fixture",true);
  ConflictReviewTests.Clear(s,project);
  var setup=s.ReadProjectSetup(project);
  var desired=setup.Items.Select(x=>x with{Complete=true,Note="Reviewed synthetic scope, contacts and delivery"}).ToArray();
  check(s.SaveProjectSetupChecklist(project,setup.Items,desired).State=="Complete"&&s.ReadProjectHandoff(project).State=="Pending","single checklist save does not authorize work");
  check(s.AuthorizeProjectStart(project,hash,agreement.Id,agreement.PdfHash,true,"Authorized synthetic work")=="Active"&&s.ReadProjectDashboardCounts()==(0L,1L),"explicit start activates solo project");
  var day=DateOnly.FromDateTime(DateTime.Today);
  var phase=s.CreateProjectPhase(project,"Design",day,day);
  var task=s.CreateProjectTask(project,new(phase,employee,"Calculations",day,day,120));
  var assigned=s.ReadMyWork(day,day).Single();
  check(assigned.CanStart&&assigned.WorkEntryLabel.Contains("Calculations"),"solo owner has correctly scoped task");
  s.AdvanceMyTask(task,assigned.Revision,"InProgress");
  var time=s.SubmitWork(new(Guid.NewGuid(),task,day,"Time",WorkDuration.Parse("1:30"),null,null,"Calculations"));
  var office=s.SubmitOverheadTime(new(Guid.NewGuid(),day,WorkDuration.Parse("0:30"),"Administration","Office work"));
  var expense=s.SubmitWork(new(Guid.NewGuid(),task,day,"Expense",null,12.34m,"Supplies","Synthetic receipt"));
  s.RecordEmployeeRate(new(employee,day,180m,35m));
  s.RecordBillingPolicy(new(project,"FixedFee",1000m,1000m,false,"Reviewed fixed fee; expenses included"));
  check(s.ReadInvoiceCandidates(project,day).Count==0&&s.ReadProjectFinancialReport(project,day,day).ApprovedMinutes==0,"unreviewed solo work does not enter billing or approved totals");
  var policy=s.ReadOwnerApprovalPolicy();
  check(policy.Eligible&&!policy.Enabled,"solo self-approval never enabled implicitly");
  s.SetOwnerApprovalPolicy(true,policy.EventId,"Explicit synthetic sole-owner policy");
  check(s.ReviewWork(time,true,"Verified actual work",true)=="Approved"&&s.ClassifyWork(time,true,"Contract work",true)=="Billable","solo project time reviewed and classified");
  check(s.ReviewOverheadTime(office,true,"Verified office work",true)=="Approved"&&s.ReviewWork(expense,true,"Verified expense",true)=="Approved"&&s.ClassifyWork(expense,false,"Included in fixed fee",true)=="Nonbillable","office time and included expenses reviewed");
  var candidate=s.ReadInvoiceCandidates(project,day).Single();
  check(candidate.SourceId==time&&candidate.Status=="Included in fixed fee","fixed-fee time cannot be charged again as hourly line");
  var draft=s.CreateInvoiceDraft(new(Guid.NewGuid(),project,day,day.AddDays(30),new[]{new FixedFeeInput("Design milestone",500m)},Array.Empty<long>()));
  var detail=s.ReadInvoiceDraft(project,draft);
  check(detail.Summary.Total==500m&&detail.Lines.Count==1&&s.DecideInvoiceDraft(project,draft,detail.Summary.PayloadHash,true,"Verified milestone",true)=="Approved","one fixed-fee draft approved without external posting");
  var fin=s.ReadProjectFinancialReport(project,day,day);
  var ops=s.ReadProjectOperations(project,day,day);
  var report=s.ReadEmployeeTimeReport(new(employee,null,day,day,"Approved"));
  var mix=s.ReadFirmTimeMix(day,day);
  check(fin.ApprovedMinutes==90&&ops.ApprovedMinutes==90&&report.ApprovedProjectMinutes==90&&report.ApprovedOverheadMinutes==30&&mix.ApprovedMinutes==120,"project and firm time reconcile across reports");
  check(fin.EstimatedLaborCost==52.50m&&fin.ApprovedExpenseCost==12.34m&&fin.TotalOperationalCost==64.84m&&fin.ApprovedInternalDraftValue==500m,"fixed-fee draft and operational costs reconcile without inferred profit");
  check(s.ExportInvoiceDraftCsv(project,draft,detail.Summary.PayloadHash).Contains("NOT AN ISSUED INVOICE"),"solo draft export remains internal");
  var started=s.ReadMyWork(day,day).Single();s.AdvanceMyTask(task,started.Revision,"Completed");
  var closed=s.RecordProjectLifecycle(project,null,true,"Synthetic operational completion",true);
  check(closed.State=="OperationalClosed"&&s.ReadProjectDashboardCounts()==(0L,0L)&&s.ReadProjectDocuments(project).Count==0,"solo operational closeout needs no ordinary document registration");
  var recovery=s.CreateRecoveryCopy(Path.Combine(Path.GetDirectoryName(path)!,"solo-practice-recovery.db"));
  var restoredPath=Path.Combine(Path.GetDirectoryName(path)!,"solo-practice-restored.db");
  var restored=s.RestoreRecoveryCopyToNewFile(recovery.Path,recovery.Sha256,restoredPath);
  var copy=new LocalPractice(restoredPath);
  check(restored.Clients==1&&restored.Projects==1&&copy.Login("admin",password)&&!copy.ReadOwnerApprovalPolicy().Enabled,"isolated restore preserves counts and disables self-approval");
  check(copy.ReadProjectFinancialReport(project,day,day)==fin&&copy.ReadProjectLifecycle(project).Single().Id==closed.Id&&copy.ReadInvoiceDraft(project,draft).Summary.PayloadHash==detail.Summary.PayloadHash,"restored project reports, lifecycle and invoice evidence reconcile");
  check(s.ReadOwnerApprovalPolicy().Enabled&&s.ReadProjectCloseoutReview(project).ProjectState=="OperationalClosed","source state unchanged by recovery");
  copy.Logout();s.Logout();
 }
}
