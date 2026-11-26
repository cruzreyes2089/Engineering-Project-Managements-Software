using PracticeManager.Core;
using PracticeManager.Persistence;
internal static class ConflictReviewTests
{
 public static void Clear(LocalPractice s,long project)=>s.RecordConflictReview(project,null,new("Synthetic jurisdiction","No","No","No","No","Synthetic evidence, not actual clearance"),true);
 public static void Run(string path,string pw,Action<bool,string> check)
 {
  var s=new LocalPractice(path);s.Bootstrap("Synthetic Compliance Firm","admin",pw,"CL");check(s.Login("admin",pw),"compliance admin logged in");
  var owner=s.ReadStaffAccounts().Single().Id;var emp=s.CreateEmployee(new("Synthetic","Owner","","",true),owner);
  var c=s.SaveClient(new Client(0,"","Business / Company","Synthetic Client","","","","","","","","","","","Active"));
  var p=s.CreateProposal(new(c,"Synthetic","Site","Scope",100m));var h=s.RecordProposalIssued(p,1);var project=s.RecordProposalDecision(p,h,true,"Synthetic").ProjectId!.Value;
  var a=s.RecordSignedAgreement(project,h,"test.pdf","%PDF-1.7\nSynthetic\n%%EOF"u8.ToArray(),"Reviewed",true);
  string Start()=>s.AuthorizeProjectStart(project,h,a.Id,a.PdfHash,true,"Synthetic");
  void Invalid(Action action,string label){try{action();}catch(ArgumentException){check(true,label);return;}throw new Exception("FAIL: "+label);}
  check(s.ReadProjectConflictStatus(project)=="NotReviewed"&&Start()=="ConflictReviewRequired","missing conflict evidence blocks start");
  var input=new ConflictReviewInput("Synthetic jurisdiction","No","No","No","No","Synthetic review");
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
  s.RecordConflictReview(project,hold.Id,input with{Evidence="Synthetic corrected facts"},true);s.AdvanceMyTask(task,1,"InProgress");
  check(s.ReadMyWork(day,day).Single().State=="InProgress"&&s.ReadConflictHistory(project).Count==5,"new reviewed clearance preserves prior holds");
 }
}
