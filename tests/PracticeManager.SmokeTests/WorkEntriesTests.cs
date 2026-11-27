using Microsoft.Data.Sqlite;
using PracticeManager.Core;
using PracticeManager.Persistence;
internal static class WorkEntriesTests
{
 public static async Task Run(string path,string pw,Action<bool,string> check)
 {
  var admin=new LocalPractice(path);admin.Bootstrap("Synthetic Time Firm","admin",pw,"CL");check(admin.Login("admin",pw),"work entries admin login");
  void Invalid(Action a,string label){try{a();}catch(ArgumentException){check(true,label);return;}throw new Exception("FAIL: "+label);}
  void Denied(Action a,string label){try{a();}catch(UnauthorizedAccessException){check(true,label);return;}throw new Exception("FAIL: "+label);}
  var role=admin.ReadStaffRoles().Single(r=>r.Name=="Engineer");admin.SetStaffGrant(role.Id,"Work.Own",true,false);
  var user=admin.CreateStaffAccount("worker",role.Id,"Synthetic-worker-temp-0001");var employee=admin.CreateEmployee(new("Synthetic","Worker","","",true),user);
  var worker=new LocalPractice(path);check(worker.ChangeRequiredPassword("worker","Synthetic-worker-temp-0001","Synthetic-worker-password-0001")&&worker.Login("worker","Synthetic-worker-password-0001"),"work entries employee login");
  var peer=new LocalPractice(path);check(peer.Login("worker","Synthetic-worker-password-0001"),"second attendance session");
  var clocks=await Task.WhenAll(Task.Run(worker.ClockIn),Task.Run(peer.ClockIn));check(clocks.Count(x=>x=="ClockedIn")==1&&clocks.Count(x=>x=="AlreadyClockedIn")==1,"concurrent attendance opens once");
  var open=worker.ReadMyOpenAttendance()!;check(peer.ClockOut(open.Id)=="ClockedOut"&&worker.ClockOut(open.Id)=="SessionUnavailable"&&worker.ReadMyOpenAttendance()==null,"clock-out atomic and not repeatable");
  var day=DateOnly.FromDateTime(DateTime.Today);var utc=DateOnly.FromDateTime(DateTime.UtcNow);check(worker.ReadMyAttendance(utc,utc).Single().ClockOut!=null,"attendance history retained");
  var client=admin.SaveClient(new Client(0,"","Business / Company","Synthetic Client","","","","","","","","","","","Active"));
  var proposal=admin.CreateProposal(new(client,"Synthetic","Site","Scope",100m));var hash=ConflictReviewTests.Issue(admin,proposal,1);var project=admin.RecordProposalDecision(proposal,hash,true,"Synthetic").ProjectId!.Value;
  var phase=admin.CreateProjectPhase(project,"Design",day.AddDays(-2),day.AddDays(2));var task=admin.CreateProjectTask(project,new(phase,employee,"Design",day.AddDays(-2),day.AddDays(2),120));
  var entry=new WorkSubmissionInput(Guid.NewGuid(),task,day,"Time",60,null,null,"Synthetic time");
  Invalid(()=>worker.SubmitWork(entry),"pending and unstarted task blocks time");
  var a=admin.RecordSignedAgreement(project,hash,"test.pdf","%PDF-1.7\nSynthetic only\n%%EOF"u8.ToArray(),"Synthetic review",true);ConflictReviewTests.Clear(admin,project);check(admin.AuthorizeProjectStart(project,hash,a.Id,a.PdfHash,true,"Synthetic")=="Active","work entries project authorized");
  Invalid(()=>worker.SubmitWork(entry),"unstarted task blocks time");
  worker.AdvanceMyTask(task,1,"InProgress");
  Invalid(()=>worker.SubmitWork(entry with{Minutes=0}),"zero minutes rejected");
  Invalid(()=>worker.SubmitWork(entry with{Date=day.AddDays(1)}),"future time rejected");
  var record=worker.SubmitWork(entry);check(peer.SubmitWork(entry)==record,"work submission operation idempotent");
  Invalid(()=>worker.SubmitWork(entry with{Minutes=61}),"same operation changed content rejected");
  Invalid(()=>worker.SubmitWork(entry with{OperationKey=Guid.NewGuid(),Minutes=1400}),"daily time cap enforced");
  var expense=new WorkSubmissionInput(Guid.NewGuid(),task,day,"Expense",null,20m,"Supplies","Synthetic receipt reference");
  Invalid(()=>worker.SubmitWork(expense with{Amount=20.001m}),"fractional expense cents rejected");
  var expenseId=worker.SubmitWork(expense);check(worker.ReadMySubmissions(day,day).Count==2,"employee sees submitted facts only");
  Denied(()=>worker.ReadBillingWork(day,day),"ordinary employee cannot access billing decisions");
  check(admin.ReadBillingWork(day,day).Count==0&&admin.ClassifyWork(record,true,"Synthetic")=="ApprovalRequired","unapproved work not billable");
  // Even a mistakenly granted review/classification role cannot self-review.
  foreach(var p in new[]{"Work.Review","Billing.Classify","Projects.View","Clients.View","Employees.View"})admin.SetStaffGrant(role.Id,p,true,false);
  Denied(()=>worker.ReviewWork(record,true,"Synthetic"),"self review denied even with grant");
  Denied(()=>worker.ClassifyWork(record,true,"Synthetic"),"self classification denied even with grant");
  check(admin.ReadWorkReview(day,day).Count==2,"review queue contains both facts");
  check(admin.ReviewWork(record,true,"Verified")=="Approved"&&admin.ReviewWork(expenseId,false,"Missing evidence")=="Rejected","independent fact review");
  check(admin.ReviewWork(record,false,"Changed")=="AlreadyApproved","review decision retained");
  check(admin.ReadBillingWork(day,day).Single().Id==record&&admin.ClassifyWork(expenseId,true,"Synthetic")=="ApprovalRequired","rejected expense excluded from billing");
  check(admin.ClassifyWork(record,false,"Nonbillable administration")=="Nonbillable","approved time explicitly classified");
  check(admin.ReadBillingWork(day,day).Count==0&&admin.ReadBillingWork(day,day,false).Single().Classification=="Nonbillable","classification not inferred or reset");
  check(worker.ReadMySubmissions(day,day).Single(x=>x.Id==record).ReviewState=="Approved","own projection retains approval without billing field");
  check(worker.SubmitWork(expense with{OperationKey=Guid.NewGuid(),Description="Replacement evidence"})!=expenseId,"rejected expense can be resubmitted as new fact");
  var overhead=new OverheadTimeInput(Guid.NewGuid(),day,120,"Marketing","Synthetic marketing");
  var o=worker.SubmitOverheadTime(overhead);check(peer.SubmitOverheadTime(overhead)==o,"overhead submission idempotent across sessions");
  Invalid(()=>worker.SubmitOverheadTime(overhead with{Minutes=121}),"changed overhead operation rejected");
  Invalid(()=>worker.SubmitOverheadTime(overhead with{OperationKey=entry.OperationKey}),"project operation key cannot become overhead");
  Invalid(()=>worker.SubmitWork(entry with{OperationKey=overhead.OperationKey}),"overhead operation key cannot become project work");
  Invalid(()=>worker.SubmitOverheadTime(overhead with{OperationKey=Guid.NewGuid(),Minutes=1261}),"overhead and project minutes share cap");
  Invalid(()=>worker.SubmitWork(entry with{OperationKey=Guid.NewGuid(),Minutes=1261}),"project and overhead minutes share cap");
  Invalid(()=>worker.SubmitOverheadTime(overhead with{OperationKey=Guid.NewGuid(),Category="Billable"}),"overhead cannot be marked billable");
  Invalid(()=>worker.SubmitOverheadTime(overhead with{OperationKey=Guid.NewGuid(),Date=day.AddDays(1)}),"future overhead rejected");
  Denied(()=>worker.ReviewOverheadTime(o,true,"Synthetic"),"ordinary employee cannot approve own overhead");
  check(admin.ReadOverheadReview(day,day).Single().Id==o&&admin.ReviewOverheadTime(o,true,"Reviewed")== "Approved","independent overhead review");
  check(worker.ReadMyOverheadTime(day,day).Single().ReviewState=="Approved"&&admin.ReadOverheadReview(day,day).Count==0,"approved overhead history retained");
  check(admin.ReviewOverheadTime(o,false,"Changed")=="AlreadyApproved","overhead decision immutable");
  var rejected=worker.SubmitOverheadTime(overhead with{OperationKey=Guid.NewGuid(),Minutes=1260,Category="Administration"});
  Invalid(()=>worker.SubmitWork(entry with{OperationKey=Guid.NewGuid(),Minutes=1}),"combined 1440 minute limit reached");
  check(admin.ReviewOverheadTime(rejected,false,"Wrong duration")=="Rejected","overhead rejection retained");
  var replacement=worker.SubmitOverheadTime(overhead with{OperationKey=Guid.NewGuid(),Minutes=60,Category="Administration"});
  check(replacement!=rejected,"rejected overhead no longer consumes daily cap");
  var own=admin.CreateEmployee(new("Synthetic","Owner","","",true),1);
  var ownerEntry=admin.SubmitOverheadTime(new(Guid.NewGuid(),day,15,"Management","Synthetic owner administration"));
  Denied(()=>admin.ReviewOverheadTime(ownerEntry,true,"Self",true),"owner overhead self review blocked by default");
  admin.SetStaffGrant(role.Id,"Work.Review",false,true);admin.SetStaffGrant(role.Id,"Billing.Classify",false,true);
  admin.SetOwnerApprovalPolicy(true,null,"Synthetic owner exception");
  Denied(()=>admin.ReviewOverheadTime(ownerEntry,true,"Self"),"owner overhead review requires explicit confirmation");
  check(admin.ReviewOverheadTime(ownerEntry,true,"Reviewed owner time",true)=="Approved"&&admin.ReadOwnerDecisionEvidence().Single(x=>x.Kind=="Overhead review").PolicyEventId.HasValue,"owner overhead exception retained in history");
  check(admin.ReadBillingWork(day,day,false).Count==1,"overhead never enters project billing queue");
  var mix=admin.ReadFirmTimeMix(day,day);
  check(mix.Rows.Count==2&&mix.ApprovedMinutes==195&&mix.ApprovedOverheadMinutes==135&&mix.ApprovedProjectNonbillableMinutes==60&&mix.PendingMinutes==60&&mix.RejectedMinutes==1260&&mix.ApprovedBillableShare==0m,"firm time mix reconciles approved, pending and rejected overhead/project minutes");
  Denied(()=>worker.ReadFirmTimeMix(day,day),"ordinary employee cannot see firm billing mix");
  var unclassified=worker.SubmitWork(entry with{OperationKey=Guid.NewGuid(),Minutes=30});
  check(admin.ReviewWork(unclassified,true,"Verified")=="Approved","additional approved project time recorded");
  mix=admin.ReadFirmTimeMix(day,day);check(mix.ApprovedMinutes==225&&mix.ApprovedUnclassifiedMinutes==30&&mix.ApprovedBillableShare==null,"unclassified time prevents definitive share");
  admin.ClassifyWork(unclassified,true,"Reviewed billable work");
  mix=admin.ReadFirmTimeMix(day,day);check(mix.ApprovedBillableMinutes==30&&mix.ApprovedMinutes==225&&mix.ApprovedBillableShare==30m/225m,"firm share includes approved overhead in denominator");
  var empty=admin.ReadFirmTimeMix(day.AddDays(-1),day.AddDays(-1));check(empty.Rows.Count==0&&empty.ApprovedBillableShare==null,"empty range does not fabricate utilization");
  check(admin.ReadProjectOperations(project,day,day).ApprovedMinutes==90,"overhead excluded from project report");
  using(var db=new SqliteConnection($"Data Source={path};Pooling=False")){db.Open();using var q=db.CreateCommand();q.CommandText="UPDATE overhead_time SET minutes=1";try{q.ExecuteNonQuery();throw new Exception("FAIL: mutable overhead");}catch(SqliteException){check(true,"SQL prevents overhead history edits");}}
 }
}
