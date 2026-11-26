using Microsoft.Data.Sqlite;
using PracticeManager.Core;
using PracticeManager.Persistence;
internal static class OwnerApprovalTests
{
 public static void Run(string path,string pw,Action<bool,string> check)
 {
  var s=new LocalPractice(path);s.Bootstrap("Synthetic Owner Firm","admin",pw,"CL");check(s.Login("admin",pw),"owner logged in");
  void Denied(Action a,string label){try{a();}catch(UnauthorizedAccessException){check(true,label);return;}throw new Exception("FAIL: "+label);}
  void Invalid(Action a,string label){try{a();}catch(ArgumentException){check(true,label);return;}throw new Exception("FAIL: "+label);}
  var owner=s.ReadStaffAccounts().Single().Id;
  var e=s.CreateEmployee(new("Synthetic","Owner","","",true),owner);
  var c=s.SaveClient(new Client(0,"","Business / Company","Synthetic Client","","","","","","","","","","","Active"));
  var p=s.CreateProposal(new(c,"Synthetic","Site","Scope",100m));var h=s.RecordProposalIssued(p,1);var project=s.RecordProposalDecision(p,h,true,"Synthetic").ProjectId!.Value;
  var a=s.RecordSignedAgreement(project,h,"test.pdf","%PDF-1.7\nSynthetic\n%%EOF"u8.ToArray(),"Reviewed",true);s.AuthorizeProjectStart(project,h,a.Id,a.PdfHash,true,"Synthetic");
  var day=DateOnly.FromDateTime(DateTime.Today);var phase=s.CreateProjectPhase(project,"Design",day,day);var task=s.CreateProjectTask(project,new(phase,e,"Design",day,day,120));s.AdvanceMyTask(task,1,"InProgress");
  var time=s.SubmitWork(new(Guid.NewGuid(),task,day,"Time",60,null,null,"Synthetic time"));var expense=s.SubmitWork(new(Guid.NewGuid(),task,day,"Expense",null,10m,"Supplies","Synthetic expense"));
  var initial=s.ReadOwnerApprovalPolicy();check(initial.Eligible&&!initial.Enabled&&initial.EventId==null,"owner exception default off");
  Denied(()=>s.ReviewWork(time,true,"Synthetic",true),"owner default denial");
  Invalid(()=>s.SetOwnerApprovalPolicy(true,null,""),"policy justification required");
  var enabled=s.SetOwnerApprovalPolicy(true,null,"Synthetic sole owner");check(enabled.Enabled&&enabled.EventId.HasValue,"owner explicit activation");
  Denied(()=>s.ReviewWork(time,true,"Synthetic"),"individual confirmation required");
  Invalid(()=>s.ReviewWork(time,true,"",true),"self review justification required");
  Denied(()=>s.ReviewWork(time,false,"Synthetic",true),"self rejection not permitted");
  check(s.ReviewWork(time,true,"Verified time",true)=="Approved"&&s.ClassifyWork(time,true,"Reviewed contract",true)=="Billable","owner time and classification exception");
  check(s.ReviewWork(expense,true,"Verified expense",true)=="Approved","owner expense exception");
  var role=s.ReadStaffRoles().Single(x=>x.Name=="Financial / Accounting");
  var staff=s.CreateStaffAccount("reviewer",role.Id,"Synthetic-reviewer-temp-0001");
  check(s.ReadOwnerApprovalPolicy().Enabled,"ordinary nonreview account does not suspend exception");
  s.SetStaffGrant(role.Id,"Work.Review",true,false);
  var suspended=s.ReadOwnerApprovalPolicy();check(!suspended.Enabled&&!suspended.Eligible&&suspended.EventId!=enabled.EventId,"new reviewer grant suspends even before password change");
  Denied(()=>s.ClassifyWork(expense,true,"Synthetic",true),"suspension covers classification");
  Denied(()=>s.SetOwnerApprovalPolicy(true,suspended.EventId,"Synthetic"),"cannot enable while other approval account exists");
  s.SetStaffGrant(role.Id,"Work.Review",false,true);
  var off=s.ReadOwnerApprovalPolicy();check(off.Eligible&&!off.Enabled&&off.EventId==suspended.EventId,"grant removal does not silently reactivate");
  Invalid(()=>s.SetOwnerApprovalPolicy(true,enabled.EventId,"Synthetic"),"stale policy change rejected");
  var again=s.SetOwnerApprovalPolicy(true,off.EventId,"Explicit reconsideration");
  check(s.ClassifyWork(expense,false,"Not reimbursable",true)=="Nonbillable","explicit reactivation allows owner classification");
  var copy=s.CreateRecoveryCopy(Path.Combine(Path.GetDirectoryName(path)!,"owner-copy.db"));
  var restored=new LocalPractice(copy.Path);check(restored.Login("admin",pw)&&!restored.ReadOwnerApprovalPolicy().Enabled,"recovery copy exception disabled");
  check(s.ReadOwnerApprovalPolicy().Enabled,"source owner policy not changed by recovery copy");
  using var db=new SqliteConnection($"Data Source={path};Pooling=False");db.Open();using var q=db.CreateCommand();
  q.CommandText="SELECT count(*) FROM work_reviews WHERE owner_exception_event_id IS NOT NULL";check(Convert.ToInt64(q.ExecuteScalar())==2,"self reviews linked to explicit policy history");
  q.CommandText="SELECT count(*) FROM work_billing_classifications WHERE owner_exception_event_id IS NOT NULL";check(Convert.ToInt64(q.ExecuteScalar())==2,"self classifications linked to explicit policy history");
  q.CommandText="SELECT count(*) FROM audit_events WHERE action='OwnerSelfReviewApproved'";check(Convert.ToInt64(q.ExecuteScalar())==2,"owner approvals distinctly audited");
  q.CommandText="SELECT count(*) FROM audit_events WHERE action='OwnerExceptionAutoSuspended'";check(Convert.ToInt64(q.ExecuteScalar())==1,"automatic suspension audited");
  s.SetOwnerApprovalPolicy(false,again.EventId,"Owner disabled");check(!s.ReadOwnerApprovalPolicy().Enabled,"manual disable supported");
 }
}
