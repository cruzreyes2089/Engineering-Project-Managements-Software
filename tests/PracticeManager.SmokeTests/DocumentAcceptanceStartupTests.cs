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
  var external=Path.Combine(local,"synthetic-external");Directory.CreateDirectory(external);Directory.CreateDirectory(Path.Combine(external,"Synthetic Project"));
  var cfg=s.RecordCandidateFolderConfiguration(Guid.NewGuid(),null,external,ProjectFolderTemplate.Defaults,"Synthetic template",true).Entry;
  var client=s.SaveClient(new Client(0,"","Business / Company","Synthetic client","","","","","","","","","","","Active"));
  var proposal=s.CreateProposal(new(client,"Synthetic project","Site","Scope",100m));var h=ConflictReviewTests.Issue(s,proposal,1);
  var project=s.RecordProposalDecision(proposal,h,true,"Synthetic").ProjectId!.Value;
  var folder=s.RecordCandidateProjectFolder(project,Guid.NewGuid(),null,cfg.Hash,"Registered","Synthetic Project","Synthetic",true).Entry;
  var agreement=s.RecordSignedAgreement(project,h,"signed.pdf","%PDF-1.7\nSynthetic\n%%EOF"u8.ToArray(),"Reviewed",true);
  ConflictReviewTests.Clear(s,project);s.AuthorizeProjectStart(project,h,agreement.Id,agreement.PdfHash,true,"Synthetic");
  var doc=s.RegisterProjectDocument(project,Guid.NewGuid(),null,"Production","Drawings","drawing.pdf","%PDF-1.7\nSynthetic\n%%EOF"u8.ToArray());
  var d=s.ReadProjectDocuments(project).Single(x=>x.Id==doc);
  var intent=DocumentTransmittalReview.Prepare(project,"Synthetic recipient","Review",new[]{new ReviewedIssueDocument(d.Id,d.Hash,d.FileName,d.Category,d.Bytes)});
  var record=s.PrepareCandidateTransmittal(Guid.NewGuid(),intent,"Synthetic preparation","Reviewed",true).Record;
  s.Logout();
  var reopened=DevelopmentWorkspace.Resolve(local,new[]{"--document-acceptance-session",id});
  check(reopened.IsDocumentAcceptance&&reopened.DatabasePath==workspace.DatabasePath,"document acceptance reopens exact marked workspace");
  var next=LocalPractice.OpenDocumentAcceptance(local,id,false);
  check(next.Login("admin",pw)&&next.ReadCandidateFolderConfiguration().Single().Hash==cfg.Hash&&next.ReadCandidateProjectFolders(project).Single()==folder&&next.ReadCandidateTransmittal(project,record.Id).Intent.ReviewHash==record.Intent.ReviewHash,"document acceptance restart preserves populated register");
  next.Logout();
  void Invalid(Action a,string label){try{a();}catch(ArgumentException){check(true,label);return;}catch(InvalidOperationException){check(true,label);return;}throw new Exception("FAIL: "+label);}
  Invalid(()=>new LocalPractice(workspace.DatabasePath),"ordinary constructor refuses document acceptance schema");
  Invalid(()=>LocalPractice.OpenDocumentAcceptance(local,id,true),"document acceptance cannot overwrite existing workspace");
  Invalid(()=>DevelopmentWorkspace.Resolve(local,new[]{"--budget-acceptance-session",id}),"budget mode cannot open document acceptance");
  using(var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=workspace.DatabasePath,Pooling=false}.ToString()))
  {db.Open();using var q=db.CreateCommand();q.CommandText="DROP TRIGGER project_folder_config_no_update";q.ExecuteNonQuery();}
  Invalid(()=>LocalPractice.OpenDocumentAcceptance(local,id,false),"document startup refuses altered register layout");
 }
}
