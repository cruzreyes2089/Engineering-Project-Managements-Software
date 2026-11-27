using Microsoft.Data.Sqlite;
using PracticeManager.Core;
using PracticeManager.Persistence;
internal static class ProjectHandoffTests
{
 public static async Task Run(string path,string pw,Action<bool,string> check)
 {
  var s=new LocalPractice(path);s.Bootstrap("Synthetic Handoff Firm","admin",pw,"CL");check(s.Login("admin",pw),"handoff fixture login");
  var c=s.SaveClient(new Client(0,"","Business / Company","Synthetic Client","","","","","","","","","","","Active"));
  var id=s.CreateProposal(new(c,"Synthetic","Site","Scope",100m));var hash=ConflictReviewTests.Issue(s,id,1);
  var project=s.RecordProposalDecision(id,hash,true,"Synthetic").ProjectId!.Value;
  void Denied(Action a,string label){try{a();}catch(UnauthorizedAccessException){check(true,label);return;}throw new Exception("FAIL: "+label);}
  void Invalid(Action a,string label){try{a();}catch(ArgumentException){check(true,label);return;}throw new Exception("FAIL: "+label);}
  void Sql(string sql){using var db=new SqliteConnection($"Data Source={path}");db.Open();using var q=db.CreateCommand();q.CommandText=sql;q.ExecuteNonQuery();}
  check(s.ReadProjectOverview(project)==s.SearchPendingProjects().Single(),"exact project workspace overview matches register");
  Invalid(()=>s.ReadProjectOverview(long.MaxValue),"unknown project workspace denied");
  Sql("UPDATE role_permissions SET allowed=0 WHERE permission_code='Projects.View'");
  Denied(()=>s.ReadProjectOverview(project),"workspace requires project view");
  Sql("UPDATE role_permissions SET allowed=1 WHERE permission_code='Projects.View'");
  Sql("UPDATE role_permissions SET allowed=0 WHERE permission_code='Clients.View'");
  Denied(()=>s.ReadProjectOverview(project),"workspace requires client view");
  Sql("UPDATE role_permissions SET allowed=1 WHERE permission_code='Clients.View'");
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
  var designer=s.SaveClient(new Client(0,"","Architect / Designer","Synthetic Designer","","","","","","","","","","","Active"));
  var contact=s.SaveContact(new Contact(0,designer,"Synthetic","Contact","","","",true));
  Invalid(()=>s.RegisterProjectParty(project,designer,contact,"Client","Wrong client",true),"project client role bound to engagement");
  Invalid(()=>s.RegisterProjectParty(project,c,contact,"Contractor","Wrong contact",true),"cross-client contact blocked");
  Invalid(()=>s.RegisterProjectParty(project,designer,contact,"Designer","Not confirmed",false),"party confirmation required");
  var party=s.RegisterProjectParty(project,designer,contact,"Designer","Synthetic design participant",true);
  check(s.ReadProjectParties(project).Single().ContactName=="Synthetic Contact"&&s.ReadProjectSetup(project).State=="InProgress"&&!s.ReadProjectSetup(project).Items.Single(x=>x.Item=="ProjectContacts").Complete,"party registration invalidates completed contact checklist");
  Invalid(()=>s.RegisterProjectParty(project,designer,contact,"Designer","Duplicate",true),"duplicate active party blocked");
  var row=s.SearchClients("").Single(x=>x.Id==designer);s.SaveClient(row with{Company="Changed synthetic name"});
  check(s.ReadProjectParties(project).Single().PartyName=="Synthetic Designer","party identity snapshot retained after CRM edit");
  check(s.RetireProjectParty(project,party,"Synthetic retired",true)=="Retired"&&s.ReadProjectParties(project).Count==0&&s.ReadProjectParties(project,true).Single().State=="Retired","retirement retains historical party");
  check(s.RetireProjectParty(project,party,"Repeat",true)=="AlreadyRetired","retirement cannot repeat");
  var currentParty=s.RegisterProjectParty(project,designer,contact,"Designer","New participation",true);check(currentParty!=party&&s.ReadProjectParties(project,true).Count==2,"new participation retains prior history");
  var contactsItem=s.ReadProjectSetup(project).Items.Single(x=>x.Item=="ProjectContacts");
  s.RecordProjectSetupItem(project,"ProjectContacts",true,"Synthetic reviewed parties",contactsItem.Complete,contactsItem.Note);
  Sql("UPDATE role_permissions SET allowed=0 WHERE permission_code='Contacts.View'");
  Denied(()=>s.ReadProjectParties(project),"contact grant required to view parties");
  Sql("UPDATE role_permissions SET allowed=1 WHERE permission_code='Contacts.View'");
  var screen=new ConflictReviewInput("Synthetic jurisdiction","No","No","No","No","Synthetic reviewed facts");
  Invalid(()=>s.RecordConflictReview(project,null,screen,true),"party review requires exact current revision");
  var first=s.RecordConflictReview(project,null,screen,true,s.ReadPartyEvidenceRevision(project));
  check(first.Disposition=="Hold","recorded project screening reports effective hold for missing declaration");
  check(s.ReadProjectConflictStatus(project)=="Hold"&&s.AuthorizeProjectStart(project,hash,b.Id,b.PdfHash,true,"Synthetic")=="ConflictReviewRequired","missing required designer declaration holds project");
  var document=s.RegisterProjectDocument(project,Guid.NewGuid(),null,"Restricted","Contracts","declaration.pdf",bytes);
  var d=s.ReadProjectDocuments(project).Single(x=>x.Id==document);
  Invalid(()=>s.RecordPartyDeclaration(project,party,null,document,d.Hash,"ReviewedNoConflict","Retired",true),"retired participant declaration blocked");
  Invalid(()=>s.RecordPartyDeclaration(project,currentParty,null,document,"bad","ReviewedNoConflict","Wrong hash",true),"declaration bound to exact document");
  Invalid(()=>s.RecordPartyDeclaration(project,currentParty,null,document,d.Hash,"ReviewedNoConflict","Not reviewed",false),"declaration review requires confirmation");
  var declared=s.RecordPartyDeclaration(project,currentParty,null,document,d.Hash,"ReviewedNoConflict","Synthetic declaration review",true);
  check(s.ReadProjectConflictStatus(project)=="Hold","declaration cannot silently clear stale project screening");
  var cleared=s.RecordConflictReview(project,first.Id,screen,true,s.ReadPartyEvidenceRevision(project));
  check(cleared.Disposition=="Clear","recorded project screening reports effective clearance after fresh evidence");
  check(s.ReadProjectConflictStatus(project)=="Clear","current declaration and fresh screening clear project");
  var revised=s.RegisterProjectDocument(project,Guid.NewGuid(),document,"Restricted","Contracts","declaration-r2.pdf","%PDF-1.7\nRevised synthetic declaration\n%%EOF"u8.ToArray());
  check(s.ReadPartyDeclarations(project,currentParty).Single().CurrentStatus=="Superseded"&&s.ReadProjectConflictStatus(project)=="Hold","superseded declaration PDF blocks new work");
  var d2=s.ReadProjectDocuments(project).Single(x=>x.Id==revised);
  Invalid(()=>s.RecordPartyDeclaration(project,currentParty,null,revised,d2.Hash,"ReviewedNoConflict","Stale",true),"stale declaration review blocked");
  s.RecordPartyDeclaration(project,currentParty,declared,revised,d2.Hash,"Concern","Synthetic concern",true);
  var concern=s.RecordConflictReview(project,cleared.Id,screen,true,s.ReadPartyEvidenceRevision(project));
  check(concern.Disposition=="Hold","recorded project screening reports effective hold for participant concern");
  check(s.ReadProjectConflictStatus(project)=="Hold","declared concern cannot be cleared by no answers");
  s.RecordPartyDeclaration(project,currentParty,s.ReadPartyDeclarations(project,currentParty).First().Id,revised,d2.Hash,"ReviewedNoConflict","Synthetic resolution",true);
  s.RecordConflictReview(project,concern.Id,screen,true,s.ReadPartyEvidenceRevision(project));
  check(s.ReadProjectConflictStatus(project)=="Clear","fresh review after concern resolution");
  Sql("UPDATE role_permissions SET allowed=0 WHERE permission_code='Documents.Restricted'");
  Denied(()=>s.ReadPartyDeclarations(project,currentParty),"restricted declaration evidence protected");
  Sql("UPDATE role_permissions SET allowed=1 WHERE permission_code='Documents.Restricted'");
  Sql("CREATE TRIGGER test_start_abort BEFORE UPDATE OF state ON projects BEGIN SELECT RAISE(ABORT,'synthetic failure'); END");
  try{s.AuthorizeProjectStart(project,hash,b.Id,b.PdfHash,true,"Synthetic");throw new Exception("FAIL: rollback");}catch(SqliteException){check(true,"start failure injected");}
  check(!s.ReadProjectHandoff(project).HasAuthorization&&s.ReadProjectHandoff(project).State=="Pending","authorization and state rollback atomically");
  Sql("DROP TRIGGER test_start_abort");
  var peer=new LocalPractice(path);check(peer.Login("admin",pw),"handoff peer login");
  var starts=await Task.WhenAll(Task.Run(()=>s.AuthorizeProjectStart(project,hash,b.Id,b.PdfHash,true,"Synthetic")),Task.Run(()=>peer.AuthorizeProjectStart(project,hash,b.Id,b.PdfHash,true,"Synthetic")));
  check(starts.Count(x=>x=="Active")==1&&starts.Count(x=>x=="AlreadyActive")==1,"concurrent start exactly once");
  check(s.ReadProjectHandoff(project).HasAuthorization&&s.SearchPendingProjects().Single().State=="Active","authorized state persisted");
  check(s.ReadProjectOverview(project).State=="Active"&&s.ReadProjectOverview(project).SetupState=="Complete","project workspace reflects current authorization and setup");
  Invalid(()=>s.RecordSignedAgreement(project,hash,"new.pdf",bytes,"Synthetic",true),"active project cannot silently replace agreement");
  Sql("UPDATE role_permissions SET allowed=0 WHERE permission_code='Agreements.View'");
  Denied(()=>s.ReadProjectHandoff(project),"restricted agreement evidence not returned");
  Sql("UPDATE role_permissions SET allowed=0 WHERE permission_code='Projects.Setup'");
  Denied(()=>s.RecordProjectSetupItem(project,"ScopeReviewed",false,"Synthetic",true,"Synthetic review"),"setup permission checked");
  check(s.ReadProjectSetup(project).State=="Complete","denied setup mutation leaves state");
 }
}
