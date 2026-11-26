using PracticeManager.Core;
using PracticeManager.Persistence;
internal static class ConflictReviewTests
{
 public static string Issue(LocalPractice s,long id,int revision)
 {
  if(!s.ReadJurisdictionPolicy().Any(x=>x.Jurisdiction=="Synthetic jurisdiction"))s.RecordJurisdictionPolicy("Synthetic jurisdiction",null,"EligibleForScreening","Synthetic policy evidence",true);
  s.RecordProposalConflictReview(id,revision,s.ReadProposalConflictHistory(id).FirstOrDefault()?.Id,new("Synthetic jurisdiction","No","No","No","No","Synthetic evidence, not actual clearance"),true);
  return s.RecordProposalIssued(id,revision);
 }
 public static void Clear(LocalPractice s,long project)=>s.RecordConflictReview(project,null,new("Synthetic jurisdiction","No","No","No","No","Synthetic evidence, not actual clearance"),true);
 public static void Run(string path,string pw,Action<bool,string> check)
 {
  var s=new LocalPractice(path);s.Bootstrap("Synthetic Compliance Firm","admin",pw,"CL");check(s.Login("admin",pw),"compliance admin logged in");
  var owner=s.ReadStaffAccounts().Single().Id;var emp=s.CreateEmployee(new("Synthetic","Owner","","",true),owner);
  var c=s.SaveClient(new Client(0,"","Business / Company","Synthetic Client","","","","","","","","","","","Active"));
  var p=s.CreateProposal(new(c,"Synthetic","Site","Scope",100m));var h=Issue(s,p,1);var project=s.RecordProposalDecision(p,h,true,"Synthetic").ProjectId!.Value;
  var a=s.RecordSignedAgreement(project,h,"test.pdf","%PDF-1.7\nSynthetic\n%%EOF"u8.ToArray(),"Reviewed",true);
  string Start()=>s.AuthorizeProjectStart(project,h,a.Id,a.PdfHash,true,"Synthetic");
  void Invalid(Action action,string label){try{action();}catch(ArgumentException){check(true,label);return;}throw new Exception("FAIL: "+label);}
  check(s.ReadProjectConflictStatus(project)=="NotReviewed"&&Start()=="ConflictReviewRequired","missing conflict evidence blocks start");
  var input=new ConflictReviewInput("Synthetic jurisdiction","No","No","No","No","Synthetic review");
  var draftInput=new ProposalInput(c,"Pre-engagement","Site","Scope",50m);var draft=s.CreateProposal(draftInput);
  Invalid(()=>s.RecordProposalIssued(draft,1),"proposal issue requires pre-engagement review");
  var blocked=s.RecordProposalConflictReview(draft,1,null,input with{OtherConflict="Unknown"},true);
  Invalid(()=>s.RecordProposalIssued(draft,1),"unresolved proposal conflict blocks issue");
  var pre=s.RecordProposalConflictReview(draft,1,blocked.Id,input,true);
  check(s.ReadProposalConflictStatus(draft,1)=="Clear","proposal review clears exact draft");
  s.EditProposal(draft,1,draftInput with{Site="Changed site"});
  Invalid(()=>s.RecordProposalIssued(draft,2),"draft edit invalidates pre-engagement review");
  Invalid(()=>s.RecordProposalConflictReview(draft,1,pre.Id,input,true),"stale draft review rejected");
  check(Issue(s,draft,2).Length==64&&s.ReadProposalConflictHistory(draft).Count==3,"revised draft reviewed and history retained");
  var unlisted=s.CreateProposal(draftInput);
  check(s.RecordProposalConflictReview(unlisted,1,null,input with{Jurisdiction="Unknown place"},true).Disposition=="Hold","unregistered jurisdiction cannot be cleared by manual answers");
  Invalid(()=>s.RecordProposalIssued(unlisted,1),"unknown policy blocks issue");
  var policy=s.ReadJurisdictionPolicy().Single(x=>x.Jurisdiction=="Synthetic jurisdiction");
  var restrictedPolicy=s.RecordJurisdictionPolicy("synthetic jurisdiction",policy.Id,"Restricted","Synthetic restriction",true);
  var blockedDraft=s.CreateProposal(draftInput);
  check(s.RecordProposalConflictReview(blockedDraft,1,null,input,true).Disposition=="Hold","restricted policy overrides No answers");
  Invalid(()=>s.RecordProposalIssued(blockedDraft,1),"restricted policy blocks issue");
  var eligible=s.RecordJurisdictionPolicy("Synthetic jurisdiction",restrictedPolicy.Id,"EligibleForScreening","Synthetic new evidence",true);
  Invalid(()=>s.RecordProposalIssued(blockedDraft,1),"later eligibility does not revive old review");
  check(Issue(s,blockedDraft,1).Length==64,"fresh review required after policy changes");
  Invalid(()=>s.RecordConflictReview(project,null,input,false),"confirmation required");
  var held=s.RecordConflictReview(project,null,input with{RestrictedJurisdiction="Unknown"},true);
  check(held.Disposition=="Hold"&&Start()=="ConflictReviewRequired","unknown jurisdiction blocks start");
  var restricted=s.RecordConflictReview(project,held.Id,input with{RestrictedJurisdiction="Yes"},true);
  check(restricted.Disposition=="Hold"&&Start()=="ConflictReviewRequired","restricted jurisdiction blocks start");
  Invalid(()=>s.RecordConflictReview(project,held.Id,input,true),"stale review cannot clear hold");
  var clear=s.RecordConflictReview(project,restricted.Id,input,true);check(clear.Disposition=="Clear"&&Start()=="Active","clear screening plus signed agreement and explicit authorization");
  var day=DateOnly.FromDateTime(DateTime.Today);var phase=s.CreateProjectPhase(project,"Design",day,day);var task=s.CreateProjectTask(project,new(phase,emp,"Task",day,day,60));
  var scope=s.ProposeScopeChange(project,"Extra","Synthetic");
  var hold=s.RecordConflictReview(project,clear.Id,input with{PublicDutyOverlap="Yes"},true);
  Invalid(()=>s.AdvanceMyTask(task,1,"InProgress"),"new conflict blocks task start on active project");
  Invalid(()=>s.DecideScopeChange(project,scope,true,"Reviewed",true),"new conflict blocks additional scope authorization");
  var fresh=s.RecordConflictReview(project,hold.Id,input with{Evidence="Synthetic corrected facts"},true);
  var policyHold=s.RecordJurisdictionPolicy("Synthetic jurisdiction",eligible.Id,"Unverified","Synthetic policy change",true);
  check(s.ReadProjectConflictStatus(project)=="Hold","changed jurisdiction policy invalidates active project clearance");
  Invalid(()=>s.AdvanceMyTask(task,1,"InProgress"),"changed policy blocks new work");
  s.RecordJurisdictionPolicy("Synthetic jurisdiction",policyHold.Id,"EligibleForScreening","Synthetic policy reconsidered",true);
  Invalid(()=>s.AdvanceMyTask(task,1,"InProgress"),"reinstated jurisdiction needs fresh review");
  s.RecordConflictReview(project,fresh.Id,input,true);s.AdvanceMyTask(task,1,"InProgress");
  check(s.ReadMyWork(day,day).Single().State=="InProgress"&&s.ReadConflictHistory(project).Count==6,"new reviewed clearance preserves prior holds");
 }
}
