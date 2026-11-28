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
  var bytes="%PDF-1.7\nSynthetic test fixture only\n%%EOF"u8.ToArray();
  check(s.ReadProjectHandoff(project).CurrentAgreement==null&&s.ReadProjectSetup(project).State=="NotStarted","new project has no execution or setup evidence");
  check(s.AuthorizeProjectStart(project,hash,0,"",true,"Synthetic")== "AgreementEvidenceMismatch","missing agreement blocks activation");
  Invalid(()=>s.RecordSignedAgreement(project,hash,"test.pdf",bytes,"Synthetic",false),"manual review required");
  Invalid(()=>s.RecordSignedAgreement(project,"wrong","test.pdf",bytes,"Synthetic",true),"agreement binds accepted snapshot");
  Invalid(()=>s.RecordSignedAgreement(project,hash,"../test.pdf",bytes,"Synthetic",true),"agreement rejects path");
  Invalid(()=>s.RecordSignedAgreement(project,hash,"test.pdf",new byte[10],"Synthetic",true),"agreement rejects non-PDF header");
  var a=s.RecordReviewedAgreement(project,hash,null,"test.pdf",bytes,"Synthetic review",true);
  check(s.RecordSignedAgreement(project,hash,"test.pdf",bytes,"Synthetic review",true).Id==a.Id,"agreement registration idempotent");
  check(s.AuthorizeProjectStart(project,hash,a.Id,a.PdfHash,false,"")== "NotAuthorized"&&s.ReadProjectHandoff(project).State=="Pending","declining authorization leaves Pending");
  var replacement="%PDF-1.7\nReplacement synthetic fixture\n%%EOF"u8.ToArray();
  Invalid(()=>s.RecordReviewedAgreement(project,hash,null,"replacement.pdf",replacement,"Stale empty agreement",true),"reviewed agreement refuses stale initial attachment");
  var b=s.RecordReviewedAgreement(project,hash,a.Id,"replacement.pdf",replacement,"Synthetic replacement review",true);
  Invalid(()=>s.RecordReviewedAgreement(project,hash,a.Id,"third.pdf","%PDF-1.7\nThird synthetic fixture\n%%EOF"u8.ToArray(),"Stale predecessor",true),"reviewed agreement refuses stale replacement");
  check(s.ReadProjectHandoff(project).CurrentAgreement==b,"stale replacement leaves current agreement intact");
  check(s.RecordReviewedAgreement(project,hash,b.Id,"replacement.pdf",replacement,"Repeated current review",true).Id==b.Id,"reviewed agreement same current PDF remains idempotent");
  check(s.AuthorizeProjectStart(project,hash,a.Id,a.PdfHash,true,"Synthetic")== "AgreementEvidenceMismatch","stale agreement cannot authorize");
  Invalid(()=>s.RecordSignedAgreement(project,hash,"test.pdf",bytes,"Synthetic",true),"superseded copy cannot silently return");
  check(s.AuthorizeProjectStart(project,hash,b.Id,"wrong",true,"Synthetic")== "AgreementEvidenceMismatch","wrong stored file hash blocked");
  var initial=s.ReadProjectSetup(project);
  check(s.SaveProjectSetupChecklist(project,initial.Items,initial.Items).State=="NotStarted","unchanged batch checklist does not create setup evidence");
  var badBatch=initial.Items.Select((x,i)=>i==0?x with{Complete=true,Note="Synthetic first change"}:i==1?x with{Complete=true,Note=""}:x).ToArray();
  Invalid(()=>s.SaveProjectSetupChecklist(project,initial.Items,badBatch),"batch requires notes for changed rows");
  check(s.ReadProjectSetup(project).Items.SequenceEqual(initial.Items),"failed batch rolls back every changed row");
  var setup=s.RecordProjectSetupItem(project,"ScopeReviewed",true,"Synthetic review",false,"");
  Invalid(()=>s.SaveProjectSetupChecklist(project,initial.Items,initial.Items),"batch rejects stale checklist snapshot");
  check(setup.State=="InProgress"&&s.ReadProjectHandoff(project).State=="Pending","setup progress does not activate project");
  Invalid(()=>s.RecordProjectSetupItem(project,"ScopeReviewed",false,"Stale",false,""),"stale setup edit rejected");
  var desired=setup.Items.Select(x=>x.Complete?x:x with{Complete=true,Note="Synthetic combined review"}).ToArray();
  check(s.SaveProjectSetupChecklist(project,setup.Items,desired).Items.SequenceEqual(desired),"one save records all changed checklist items and preserves existing note");
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
  var renewed=s.RecordConflictReview(project,concern.Id,screen,true,s.ReadPartyEvidenceRevision(project));
  check(s.ReadProjectConflictStatus(project)=="Clear","fresh review after concern resolution");
  var priorReview=s.ReadPartyDeclarations(project,currentParty).First();
  var priorRevision=s.ReadPartyEvidenceRevision(project);var priorDocs=s.ReadProjectDocuments(project,false).Count;
  var attachment="%PDF-1.7\nNew synthetic declaration\n%%EOF"u8.ToArray();
  Invalid(()=>s.AttachPartyDeclaration(project,currentParty,priorReview.Id,Guid.NewGuid(),"new-declaration.pdf",attachment,false),"attachment requires explicit confirmation");
  Invalid(()=>s.AttachPartyDeclaration(project,currentParty,null,Guid.NewGuid(),"new-declaration.pdf",attachment,true),"attachment rejects stale declaration review");
  check(s.ReadProjectDocuments(project,false).Count==priorDocs&&s.ReadProjectConflictStatus(project)=="Clear","rejected attachment leaves evidence and clearance unchanged");
  Sql("CREATE TRIGGER test_attachment_abort BEFORE INSERT ON party_declaration_reviews WHEN NEW.decision='Unverified' BEGIN SELECT RAISE(ABORT,'synthetic failure'); END");
  try{s.AttachPartyDeclaration(project,currentParty,priorReview.Id,Guid.NewGuid(),"new-declaration.pdf",attachment,true);throw new Exception("FAIL: attachment rollback");}catch(SqliteException){check(s.ReadProjectDocuments(project,false).Count==priorDocs&&s.ReadPartyEvidenceRevision(project)==priorRevision&&s.ReadProjectConflictStatus(project)=="Clear","attachment and unverified event roll back together");}
  Sql("DROP TRIGGER test_attachment_abort");
  var attached=s.AttachPartyDeclaration(project,currentParty,priorReview.Id,Guid.NewGuid(),"new-declaration.pdf",attachment,true);
  var unverified=s.ReadPartyDeclarations(project,currentParty).First();
  check(unverified.DocumentId==attached&&unverified.CurrentStatus=="Unverified"&&s.ReadProjectConflictStatus(project)=="Hold"&&s.ReadPartyEvidenceRevision(project)==priorRevision+1,"attachment immediately holds previous clearance without claiming signed review");
  check(s.AuthorizeProjectStart(project,hash,b.Id,b.PdfHash,true,"Synthetic")=="ConflictReviewRequired","unverified attachment prevents start");
  s.RecordPartyDeclaration(project,currentParty,unverified.Id,attached,unverified.DocumentHash,"ReviewedNoConflict","Reviewed replacement",true);
  check(s.ReadProjectConflictStatus(project)=="Hold","review of attachment alone cannot restore clearance");
  s.RecordConflictReview(project,renewed.Id,screen,true,s.ReadPartyEvidenceRevision(project));
  check(s.ReadProjectConflictStatus(project)=="Clear","renewed project screening restores clearance");

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
  check(s.ReadProjectDashboardCounts()==(0L,1L),"dashboard reflects authorized active project");
  check(s.ReadProjectDocuments(project,false).All(x=>x.Classification=="Restricted"&&x.Category=="Contracts"),"project start does not require ordinary production document registration; required declarations remain protected");
  Invalid(()=>s.RecordSignedAgreement(project,hash,"new.pdf",bytes,"Synthetic",true),"active project cannot silently replace agreement");
  Sql("UPDATE role_permissions SET allowed=0 WHERE permission_code='Agreements.View'");
  Denied(()=>s.ReadProjectHandoff(project),"restricted agreement evidence not returned");
  Sql("UPDATE role_permissions SET allowed=0 WHERE permission_code='Projects.Setup'");
  Denied(()=>s.RecordProjectSetupItem(project,"ScopeReviewed",false,"Synthetic",true,"Synthetic review"),"setup permission checked");
  var finalSetup=s.ReadProjectSetup(project);Denied(()=>s.SaveProjectSetupChecklist(project,finalSetup.Items,finalSetup.Items),"batch checklist requires current setup permission");
  check(s.ReadProjectSetup(project).State=="Complete","denied setup mutation leaves state");
 }
}
