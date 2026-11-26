using Microsoft.Data.Sqlite;
using PracticeManager.Core;
using PracticeManager.Persistence;
internal static class ProjectHandoffTests
{
 public static async Task Run(string path,string pw,Action<bool,string> check)
 {
  var s=new LocalPractice(path);s.Bootstrap("Synthetic Handoff Firm","admin",pw,"CL");check(s.Login("admin",pw),"handoff fixture login");
  var c=s.SaveClient(new Client(0,"","Business / Company","Synthetic Client","","","","","","","","","","","Active"));
  var id=s.CreateProposal(new(c,"Synthetic","Site","Scope",100m));var hash=s.RecordProposalIssued(id,1);
  var project=s.RecordProposalDecision(id,hash,true,"Synthetic").ProjectId!.Value;
  void Denied(Action a,string label){try{a();}catch(UnauthorizedAccessException){check(true,label);return;}throw new Exception("FAIL: "+label);}
  void Invalid(Action a,string label){try{a();}catch(ArgumentException){check(true,label);return;}throw new Exception("FAIL: "+label);}
  void Sql(string sql){using var db=new SqliteConnection($"Data Source={path}");db.Open();using var q=db.CreateCommand();q.CommandText=sql;q.ExecuteNonQuery();}
  var bytes="%PDF-1.7\nSynthetic test fixture only\n%%EOF"u8.ToArray();
  check(s.ReadProjectHandoff(project).CurrentAgreement==null&&s.ReadProjectSetup(project).State=="NotStarted","new project has no execution or setup evidence");
  check(s.AuthorizeProjectStart(project,hash,0,"",true,"Synthetic")== "AgreementEvidenceMismatch","missing agreement blocks activation");
  Invalid(()=>s.RecordSignedAgreement(project,hash,"test.pdf",bytes,"Synthetic",false),"manual review required");
  Invalid(()=>s.RecordSignedAgreement(project,"wrong","test.pdf",bytes,"Synthetic",true),"agreement binds accepted snapshot");
  Invalid(()=>s.RecordSignedAgreement(project,hash,"../test.pdf",bytes,"Synthetic",true),"agreement rejects path");
  Invalid(()=>s.RecordSignedAgreement(project,hash,"test.pdf",new byte[10],"Synthetic",true),"agreement rejects non-PDF header");
  var a=s.RecordSignedAgreement(project,hash,"test.pdf",bytes,"Synthetic review",true);
  check(s.RecordSignedAgreement(project,hash,"test.pdf",bytes,"Synthetic review",true).Id==a.Id,"agreement registration idempotent");
  check(s.AuthorizeProjectStart(project,hash,a.Id,a.PdfHash,false,"")== "NotAuthorized"&&s.ReadProjectHandoff(project).State=="Pending","declining authorization leaves Pending");
  var b=s.RecordSignedAgreement(project,hash,"replacement.pdf","%PDF-1.7\nReplacement synthetic fixture\n%%EOF"u8.ToArray(),"Synthetic replacement review",true);
  check(s.AuthorizeProjectStart(project,hash,a.Id,a.PdfHash,true,"Synthetic")== "AgreementEvidenceMismatch","stale agreement cannot authorize");
  Invalid(()=>s.RecordSignedAgreement(project,hash,"test.pdf",bytes,"Synthetic",true),"superseded copy cannot silently return");
  check(s.AuthorizeProjectStart(project,hash,b.Id,"wrong",true,"Synthetic")== "AgreementEvidenceMismatch","wrong stored file hash blocked");
  var setup=s.RecordProjectSetupItem(project,"ScopeReviewed",true,"Synthetic review",false,"");
  check(setup.State=="InProgress"&&s.ReadProjectHandoff(project).State=="Pending","setup progress does not activate project");
  Invalid(()=>s.RecordProjectSetupItem(project,"ScopeReviewed",false,"Stale",false,""),"stale setup edit rejected");
  foreach(var item in setup.Items.Where(x=>!x.Complete))s.RecordProjectSetupItem(project,item.Item,true,"Synthetic review",false,"");
  check(s.ReadProjectSetup(project).State=="Complete"&&s.SearchPendingProjects().Single().SetupState=="Complete"&&s.ReadProjectHandoff(project).State=="Pending","complete setup not start authorization");
  ConflictReviewTests.Clear(s,project);
  Sql("CREATE TRIGGER test_start_abort BEFORE UPDATE OF state ON projects BEGIN SELECT RAISE(ABORT,'synthetic failure'); END");
  try{s.AuthorizeProjectStart(project,hash,b.Id,b.PdfHash,true,"Synthetic");throw new Exception("FAIL: rollback");}catch(SqliteException){check(true,"start failure injected");}
  check(!s.ReadProjectHandoff(project).HasAuthorization&&s.ReadProjectHandoff(project).State=="Pending","authorization and state rollback atomically");
  Sql("DROP TRIGGER test_start_abort");
  var peer=new LocalPractice(path);check(peer.Login("admin",pw),"handoff peer login");
  var starts=await Task.WhenAll(Task.Run(()=>s.AuthorizeProjectStart(project,hash,b.Id,b.PdfHash,true,"Synthetic")),Task.Run(()=>peer.AuthorizeProjectStart(project,hash,b.Id,b.PdfHash,true,"Synthetic")));
  check(starts.Count(x=>x=="Active")==1&&starts.Count(x=>x=="AlreadyActive")==1,"concurrent start exactly once");
  check(s.ReadProjectHandoff(project).HasAuthorization&&s.SearchPendingProjects().Single().State=="Active","authorized state persisted");
  Invalid(()=>s.RecordSignedAgreement(project,hash,"new.pdf",bytes,"Synthetic",true),"active project cannot silently replace agreement");
  Sql("UPDATE role_permissions SET allowed=0 WHERE permission_code='Agreements.View'");
  Denied(()=>s.ReadProjectHandoff(project),"restricted agreement evidence not returned");
  Sql("UPDATE role_permissions SET allowed=0 WHERE permission_code='Projects.Setup'");
  Denied(()=>s.RecordProjectSetupItem(project,"ScopeReviewed",false,"Synthetic",true,"Synthetic review"),"setup permission checked");
  check(s.ReadProjectSetup(project).State=="Complete","denied setup mutation leaves state");
 }
}
