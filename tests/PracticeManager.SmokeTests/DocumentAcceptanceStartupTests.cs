using Microsoft.Data.Sqlite;
using PracticeManager.Core;
using PracticeManager.Persistence;
internal static class DocumentAcceptanceStartupTests
{
 public static void Run(string root,string pw,Action<bool,string> check)
 {
  var local=Path.Combine(root,"document-acceptance-startup");Directory.CreateDirectory(local);
  var workspace=DevelopmentWorkspace.Resolve(local,new[]{"--document-acceptance-new"});var id=workspace.AcceptanceId!;
  check(workspace.IsDocumentAcceptance&&!workspace.IsBudgetAcceptance,"document acceptance uses distinct workspace");
  var s=LocalPractice.OpenDocumentAcceptance(local,id,true);
  s.Bootstrap("Synthetic document acceptance","admin",pw,"CL");
  check(s.Login("admin",pw)&&s.IsDocumentAcceptance&&s.Can("Projects.Budget.Record"),"document acceptance bootstraps both candidate migrations");
  var external=s.PrepareAcceptanceFolderRoot(true);
  var cfg=s.SaveAcceptanceFolderConfiguration(Guid.NewGuid(),null,external,ProjectFolderTemplate.Defaults,"Synthetic template",true);
  var client=s.SaveClient(new Client(0,"","Business / Company","Synthetic client","","","","","","","","","","","Active"));
  var proposal=s.CreateProposal(new(client,"Synthetic project","Site","Scope",100m));var h=ConflictReviewTests.Issue(s,proposal,1);
  var project=s.RecordProposalDecision(proposal,h,true,"Synthetic").ProjectId!.Value;
  var relative=s.PrepareAcceptanceProjectDirectory(project,cfg.Hash,true);
  var folder=s.SaveAcceptanceProjectFolder(project,Guid.NewGuid(),null,cfg.Hash,"Registered",relative,"Synthetic",true);
  var selected=cfg.Template.Where(x=>x.Name is "QA-QC" or "Reference Documents" or "Calculations").ToArray();
  var existing=Path.Combine(folder.DisplayPath,"Calculations");Directory.CreateDirectory(existing);
  var marker=Path.Combine(existing,"preserve.txt");File.WriteAllText(marker,"Synthetic existing content");
  var preview=s.PreviewAcceptanceFolderCreation(project,folder.Hash,cfg.Hash,selected);
  check(preview.Existing.SequenceEqual(new[]{"Calculations"})&&preview.Missing.Count==2,"acceptance creation previews missing and existing template folders");
  var created=s.CreateAcceptanceFolders(project,folder.Hash,cfg.Hash,selected,preview.PlanHash,true);
  check(created.Count==2&&File.ReadAllText(marker)=="Synthetic existing content","acceptance creates only missing template folders");
  var reconciled=s.PreviewAcceptanceFolderCreation(project,folder.Hash,cfg.Hash,selected);
  check(reconciled.Missing.Count==0,"acceptance folder creation reconciles complete template");
  try{s.CreateAcceptanceFolders(project,folder.Hash,cfg.Hash,selected,preview.PlanHash,true);throw new Exception("FAIL: stale acceptance creation");}
  catch(ArgumentException){check(true,"acceptance refuses stale folder creation plan");}
  if(OperatingSystem.IsWindows())check(WorkspaceConfidentiality.HasRestrictedDirectoryAcl(Path.Combine(folder.DisplayPath,"QA-QC")),"acceptance new QA-QC folder has restricted permissions");
  var agreement=s.RecordSignedAgreement(project,h,"signed.pdf","%PDF-1.7\nSynthetic\n%%EOF"u8.ToArray(),"Reviewed",true);
  ConflictReviewTests.Clear(s,project);s.AuthorizeProjectStart(project,h,agreement.Id,agreement.PdfHash,true,"Synthetic");
  var doc=s.RegisterProjectDocument(project,Guid.NewGuid(),null,"Production","Drawings","drawing.pdf","%PDF-1.7\nSynthetic\n%%EOF"u8.ToArray());
  var d=s.ReadProjectDocuments(project).Single(x=>x.Id==doc);
  var intent=DocumentTransmittalReview.Prepare(project,"Synthetic recipient","Review",new[]{new ReviewedIssueDocument(d.Id,d.Hash,d.FileName,d.Category,d.Bytes)});
  var record=s.PrepareAcceptanceTransmittal(Guid.NewGuid(),intent,"Synthetic preparation","Reviewed",true);
  var report=s.ReadAcceptanceDocumentManagementReport(project);
  check(report.ProductionVersions==1&&report.CurrentProductionVersions==1&&report.Preparations==1&&report.Issues==0&&report.EverDelivered==0&&report.HasRegisteredFolder&&report.FolderUsesCurrentConfiguration,"document report distinguishes preparation and folder configuration");
  // Revoke individual live grants without changing the session, then restore the synthetic fixture.
  void Grant(string grant,bool enabled)
  {
   using var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=workspace.DatabasePath,Pooling=false}.ToString());
   db.Open();using var q=db.CreateCommand();
   q.CommandText="UPDATE role_permissions SET allowed=$a WHERE role_id=1 AND permission_code=$g";
   q.Parameters.AddWithValue("$a",enabled?1:0);q.Parameters.AddWithValue("$g",grant);
   if(q.ExecuteNonQuery()!=1)throw new InvalidOperationException("Expected synthetic grant row.");
  }
  void Denied(Action action,string label)
  {try{action();}catch(UnauthorizedAccessException){check(true,label);return;}throw new Exception("FAIL: "+label);}
  void WithRevoked(string grant,Action action)
  {Grant(grant,false);try{action();}finally{Grant(grant,true);}}
  WithRevoked("Documents.View",()=>{
   Denied(()=>s.ReviewAcceptanceProjectFolders(project),"folder history requires current document view grant");
   Denied(()=>s.ReviewAcceptanceTransmittals(project),"transmittal history requires current document view grant");
   Denied(()=>s.ReadAcceptanceDocumentManagementReport(project),"document report requires current document view grant");
  });
  WithRevoked("Documents.Record",()=>{
   Denied(()=>s.PrepareAcceptanceTransmittal(Guid.NewGuid(),intent,"Synthetic preparation","Reviewed",true),"transmittal preparation requires current record grant");
   Denied(()=>s.SaveAcceptanceTransmittalEvent(project,record.Id,Guid.NewGuid(),record.History[0].Event.Hash,"IssueRecorded","Synthetic issue","Reviewed",true),"issue recording requires current record grant");
   Denied(()=>s.CreateAcceptanceFolders(project,folder.Hash,cfg.Hash,selected,reconciled.PlanHash,true),"folder creation requires current record grant");
   check(s.ReviewAcceptanceTransmittals(project).Single().History.Count==1,"read-only authorization retains original transmittal without new events");
  });
  WithRevoked("Administration.Manage",()=>{
   Denied(()=>s.ReviewAcceptanceFolderConfigurations(),"folder configuration requires current administrative grant");
   Denied(()=>s.ReviewAcceptanceProjectFolders(project),"folder register requires current administrative grant");
   Denied(()=>s.ReviewAcceptanceTransmittals(project),"transmittal register requires current administrative grant");
  });
  WithRevoked("Reports.Operations",()=>{
   Denied(()=>s.ReadAcceptanceDocumentManagementReport(project),"document report requires current operations grant");
   check(s.ReviewAcceptanceTransmittals(project).Single().History.Count==1,"report grant is separate from document register read");
  });
  check(s.ReadAcceptanceDocumentManagementReport(project).Preparations==1,"restored grants preserve original preparation");
  var issued=s.SaveAcceptanceTransmittalEvent(project,record.Id,Guid.NewGuid(),record.History[0].Event.Hash,"IssueRecorded","Synthetic issue","Reviewed",true);
  var delivered=s.SaveAcceptanceTransmittalEvent(project,record.Id,Guid.NewGuid(),issued.Event.Hash,"DeliveryRecorded","Synthetic delivery","Reviewed",true);
  s.SaveAcceptanceTransmittalEvent(project,record.Id,Guid.NewGuid(),delivered.Event.Hash,"Withdrawn","Synthetic withdrawal","Reviewed",true);
  var withdrawn=s.ReadAcceptanceDocumentManagementReport(project);
  check(withdrawn.Withdrawals==1&&withdrawn.Deliveries==0&&withdrawn.EverIssued==1&&withdrawn.EverDelivered==1,"withdrawal preserves historical issued and delivered report counts");
  s.Logout();
  var reopened=DevelopmentWorkspace.Resolve(local,new[]{"--document-acceptance-session",id});
  check(reopened.IsDocumentAcceptance&&reopened.DatabasePath==workspace.DatabasePath,"document acceptance reopens exact marked workspace");
  var next=LocalPractice.OpenDocumentAcceptance(local,id,false);
  check(next.Login("admin",pw)&&next.ReviewAcceptanceFolderConfigurations().Single().Hash==cfg.Hash&&next.ReviewAcceptanceProjectFolders(project).Single()==folder&&next.ReviewAcceptanceTransmittals(project).Single().Intent.ReviewHash==record.Intent.ReviewHash,"document acceptance restart preserves populated register");
  next.Logout();
  try{next.ReadAcceptanceDocumentManagementReport(project);throw new Exception("FAIL: signed out report");}catch(UnauthorizedAccessException){check(true,"document report requires current session");}
  try{next.ReviewAcceptanceTransmittals(project);throw new Exception("FAIL: signed out acceptance");}catch(UnauthorizedAccessException){check(true,"acceptance wrapper rejects signed out history");}
  void Invalid(Action a,string label){try{a();}catch(ArgumentException){check(true,label);return;}catch(InvalidOperationException){check(true,label);return;}throw new Exception("FAIL: "+label);}
  Invalid(()=>new LocalPractice(workspace.DatabasePath),"ordinary constructor refuses document acceptance schema");
  Invalid(()=>LocalPractice.OpenDocumentAcceptance(local,id,true),"document acceptance cannot overwrite existing workspace");
  Invalid(()=>DevelopmentWorkspace.Resolve(local,new[]{"--budget-acceptance-session",id}),"budget mode cannot open document acceptance");
  using(var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=workspace.DatabasePath,Pooling=false}.ToString()))
  {db.Open();using var q=db.CreateCommand();q.CommandText="DROP TRIGGER project_folder_config_no_update";q.ExecuteNonQuery();}
  Invalid(()=>LocalPractice.OpenDocumentAcceptance(local,id,false),"document startup refuses altered register layout");
 }
}
