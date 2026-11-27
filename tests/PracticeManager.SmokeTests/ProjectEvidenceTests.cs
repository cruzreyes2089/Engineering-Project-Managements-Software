using Microsoft.Data.Sqlite;
using PracticeManager.Core;
using PracticeManager.Persistence;
internal static class ProjectEvidenceTests
{
 public static void Run(string path,string pw,Action<bool,string> check)
 {
  var s=new LocalPractice(path);s.Bootstrap("Synthetic Evidence Firm","admin",pw,"CL");check(s.Login("admin",pw),"project evidence admin login");
  void Invalid(Action a,string label){try{a();}catch(ArgumentException){check(true,label);return;}throw new Exception("FAIL: "+label);}
  void Denied(Action a,string label){try{a();}catch(UnauthorizedAccessException){check(true,label);return;}throw new Exception("FAIL: "+label);}
  void Sql(string sql){using var db=new SqliteConnection($"Data Source={path}");db.Open();using var q=db.CreateCommand();q.CommandText=sql;q.ExecuteNonQuery();}
  var client=s.SaveClient(new Client(0,"","Business / Company","Synthetic Client","","","","","","","","","","","Active"));
  (long Id,string Hash) Project(string title){var p=s.CreateProposal(new(client,title,"Site","Scope",100m));var h=ConflictReviewTests.Issue(s,p,1);return(s.RecordProposalDecision(p,h,true,"Synthetic").ProjectId!.Value,h);}
  var p=Project("First");var other=Project("Other");var bytes="Synthetic document"u8.ToArray();var key=Guid.NewGuid();
  var folders=s.ReadProjectFolderPreview(p.Id);
  check(folders.ProjectId==p.Id&&folders.TemplateRevision=="CRC-Production-2"&&!folders.Provisioned&&!folders.FilesystemInspected,"folder preview bound to project without disk activity");
  check(folders.Production.Select(x=>x.Name).SequenceEqual(new[]{"Architectural","Calculations","Construction Administration","Plancheck Comment Responses","QA-QC","Reference Documents","Send Outs","Site Pictures","Structural DWGs"}),"approved default production folder names");
  check(folders.Production.All(x=>x.Children.Count==0)&&s.ReadProjectFolderPreview(p.Id,true).Production.All(x=>x.Children.Count==0),"no default or optional MEP subdirectories");
  var draft=s.ReviewProjectStorageDraft(p.Id,@"c:\Synthetic Production",@"D:\Synthetic Restricted");
  check(draft.ProductionRoot==@"C:\Synthetic Production"&&draft.RestrictedRoot==@"D:\Synthetic Restricted"&&
   draft.ProductionDirectories==10&&draft.RestrictedDirectories==5&&!draft.Registered&&!draft.FilesystemVerified&&!draft.ProvisioningAuthorized,
   "storage draft validates lexical paths without registering or accessing storage");
  check(draft.Fingerprint==s.ReviewProjectStorageDraft(p.Id,@"C:\Synthetic Production",@"D:\Synthetic Restricted").Fingerprint&&
   draft.Fingerprint!=s.ReviewProjectStorageDraft(p.Id,@"C:\Synthetic Production",@"D:\Synthetic Restricted",true).Fingerprint,
   "storage draft fingerprint binds proposed paths and selected template");
  foreach(var invalid in new[]{@"\\server\share",@"C:\",@"C:relative",@"C:\a\..\b",@"C:\a\.",@"C:\a\\b",@"C:\a:stream",@"C:\NUL.txt",@"C:\COM¹",@"C:\SHORT~1",@"C:\a ",@"C:\a."})
   Invalid(()=>s.ReviewProjectStorageDraft(p.Id,invalid,@"D:\Synthetic Restricted"),"ambiguous or unsafe storage root syntax denied");
  Invalid(()=>s.ReviewProjectStorageDraft(p.Id,@"C:\Project",@"c:\project"),"identical storage roots denied");
  Invalid(()=>s.ReviewProjectStorageDraft(p.Id,@"C:\Project",@"C:\Project\Restricted"),"nested restricted root denied");
  Invalid(()=>s.ReviewProjectStorageDraft(p.Id,@"C:\Project\Production",@"C:\Project"),"nested production root denied");
  check(s.ReviewProjectStorageDraft(p.Id,@"C:\Project",@"C:\Projects").ProductionDirectories==10,"sibling prefix is not a nested root");
  Sql("UPDATE role_permissions SET allowed=0 WHERE permission_code='Administration.Manage'");
  Denied(()=>s.ReviewProjectStorageDraft(p.Id,@"C:\Production",@"C:\Restricted"),"storage draft requires administration");
  Sql("UPDATE role_permissions SET allowed=1 WHERE permission_code='Administration.Manage'");
  var plan=folders.Plan;
  check(plan.ProjectId==p.Id&&plan.ProjectNumber==folders.ProjectNumber&&plan.TemplateRevision==folders.TemplateRevision&&!plan.Provisioned&&!plan.StorageApproved,"logical folder plan preserves identity and no storage authorization");
  check(plan.Entries.Count==15&&plan.Entries.Count(x=>x.Boundary=="Production")==10&&plan.Entries.Count(x=>x.Boundary=="Restricted")==5,"logical folder plan uses separate roots");
  check(plan.Entries.Any(x=>x.Boundary=="Production"&&x.RelativeDirectory==folders.ProjectNumber+"/Reference Documents")&&
    plan.Entries.All(x=>!x.RelativeDirectory.Contains("/MEP",StringComparison.OrdinalIgnoreCase)),"logical plan keeps reference documents flat");
  check(plan.Fingerprint.Length==64&&s.ReadProjectFolderPreview(p.Id).Plan.Fingerprint==plan.Fingerprint,"logical plan fingerprint stable");
  check(s.ReadProjectFolderPreview(p.Id,true).Plan.Fingerprint!=plan.Fingerprint&&s.ReadProjectFolderPreview(other.Id).Plan.Fingerprint!=plan.Fingerprint,"logical plan fingerprint binds selection and project");
  foreach(var name in new[]{"../PRJ-1","PRJ/1","PRJ\\1","C:PRJ-1","PRJ-1.","PRJ-1 ","-PRJ1","PRJ1"})
   Invalid(()=>ProjectFolderPlanning.Build(p.Id,name,false,false),"unsafe project folder identity rejected");
  Invalid(()=>ProjectFolderPlanning.Build(0,folders.ProjectNumber,false,false),"invalid folder plan project identity rejected");
  check(folders.Restricted.Count==4&&folders.Production.All(x=>x.Name!="Billing Administration"),"restricted layout separate from production");
  check(s.ReadProjectFolderPreview(p.Id,true).Production.Count(x=>x.Name=="Correspondence")==1&&s.ReadProjectFolderPreview(p.Id).Production.All(x=>x.Name!="Correspondence"),"optional correspondence does not modify default");
  check(s.ReadProjectFolderPreview(other.Id).ProjectNumber!=folders.ProjectNumber,"folder preview preserves project context");
  Invalid(()=>s.ReadProjectFolderPreview(long.MaxValue),"folder preview rejects nonexistent project");
  check(s.ReadProjectDocuments(p.Id).Count==0,"folder preview creates no document records");
  Sql("UPDATE role_permissions SET allowed=0 WHERE permission_code='Documents.View'");
  Denied(()=>s.ReadProjectFolderPreview(p.Id),"folder preview requires document view");
  Sql("UPDATE role_permissions SET allowed=1 WHERE permission_code='Documents.View'; UPDATE role_permissions SET allowed=0 WHERE permission_code='Projects.View'");
  Denied(()=>s.ReadProjectFolderPreview(p.Id),"folder preview requires project view");
  Sql("UPDATE role_permissions SET allowed=1 WHERE permission_code='Projects.View'; UPDATE role_permissions SET allowed=0 WHERE permission_code='Clients.View'");
  Denied(()=>s.ReadProjectFolderPreview(p.Id),"folder preview requires client view");
  Sql("UPDATE role_permissions SET allowed=1 WHERE permission_code='Clients.View'");
  Invalid(()=>s.RegisterProjectDocument(p.Id,Guid.NewGuid(),null,"Production","Reports","../report.txt",bytes),"document traversal rejected");
  Invalid(()=>s.RegisterProjectDocument(p.Id,Guid.NewGuid(),null,"Production","Reports","CON.txt",bytes),"reserved name rejected");
  Invalid(()=>s.RegisterProjectDocument(p.Id,Guid.NewGuid(),null,"Production","Reports","program.exe",bytes),"executable document rejected");
  var id=s.RegisterProjectDocument(p.Id,key,null,"Production","Reports","report.txt",bytes);
  check(s.RegisterProjectDocument(p.Id,key,null,"Production","Reports","report.txt",bytes)==id,"document idempotent by operation and hash");
  Invalid(()=>s.RegisterProjectDocument(p.Id,key,null,"Production","Reports","report.txt","Changed"u8.ToArray()),"document operation conflict");
  var old=s.ReadProjectDocuments(p.Id).Single();
  var successor=s.RegisterProjectDocument(p.Id,Guid.NewGuid(),id,"Production","Reports","report.txt","Revision"u8.ToArray());
  check(s.ReadProjectDocuments(p.Id).Single().Id==successor&&s.ReadProjectDocuments(p.Id,false).Count==2,"current document and history separate");
  Invalid(()=>s.RegisterProjectDocument(p.Id,Guid.NewGuid(),id,"Production","Reports","report.txt",bytes),"stale version cannot branch");
  Invalid(()=>s.RegisterProjectDocument(other.Id,Guid.NewGuid(),successor,"Production","Reports","report.txt",bytes),"cross-project version denied");
  var restricted=s.RegisterProjectDocument(p.Id,Guid.NewGuid(),null,"Restricted","Contracts","agreement.txt",bytes);
  var rd=s.ReadProjectDocuments(p.Id).Single(x=>x.Id==restricted);
  check(s.RetrieveProjectDocument(p.Id,id,old.Hash).Bytes.SequenceEqual(bytes),"old immutable document verified");
  Denied(()=>s.RetrieveProjectDocument(other.Id,id,old.Hash),"cross-project retrieval denied");
  Sql("UPDATE role_permissions SET allowed=0 WHERE permission_code='Documents.Restricted'");
  check(s.ReadProjectDocuments(p.Id,false).All(x=>x.Id!=restricted),"restricted document omitted from list");
  check(s.ReadProjectFolderPreview(p.Id).Restricted.Count==0&&s.ReadProjectFolderPreview(p.Id).Production.Count==9,"restricted folder projection omitted when grant revoked");
  Denied(()=>s.ReviewProjectStorageDraft(p.Id,@"C:\Production",@"C:\Restricted"),"storage draft requires restricted grant");
  var publicPlan=s.ReadProjectFolderPreview(p.Id).Plan;
  check(publicPlan.Entries.Count==10&&publicPlan.Entries.All(x=>x.Boundary=="Production")&&publicPlan.Fingerprint!=plan.Fingerprint,"restricted logical paths and fingerprint omitted from ordinary projection");
  Denied(()=>s.RetrieveProjectDocument(p.Id,restricted,rd.Hash),"restricted document direct retrieval denied");
  Denied(()=>s.RegisterProjectDocument(p.Id,Guid.NewGuid(),null,"Restricted","Contracts","secret.txt",bytes),"restricted write denied");
  Invalid(()=>s.RecordProjectLifecycle(p.Id,null,true,"Synthetic pending closure",true),"pending project cannot close");
  var before=s.ReadProjectCloseoutReview(p.Id);check(before.ProjectState=="Pending"&&before.Phases==0&&before.OpenTasks==0&&before.OpenQualityItems==0&&before.PendingScopeDecisions==0,"closeout review describes empty pending project without authorizing closure");
  var q=s.CreateQualityItem(p.Id,"Synthetic QA","Correction","Review calculation");s.AddQualityNote(p.Id,q,"Synthetic review note");
  check(s.ReadQualityNotes(p.Id,q).Count==1&&s.ReadQualityItems(p.Id).Single().State=="Open","quality history retained");
  check(s.ReadProjectCloseoutReview(p.Id).OpenQualityItems==1&&s.ReadProjectCloseoutReview(other.Id).OpenQualityItems==0,"closeout QA count is project scoped");
  Invalid(()=>s.AddQualityNote(other.Id,q,"wrong project"),"quality scope enforced");
  check(s.CloseQualityItem(p.Id,q,"Resolved")=="Closed"&&s.CloseQualityItem(p.Id,q,"Changed")=="AlreadyClosed","quality closure immutable");
  Invalid(()=>s.AddQualityNote(p.Id,q,"After closure"),"closed quality history read-only");
  var change=s.ProposeScopeChange(p.Id,"Extra work","Synthetic additional scope");
  check(s.ReadProjectCloseoutReview(p.Id).OpenQualityItems==0&&s.ReadProjectCloseoutReview(p.Id).PendingScopeDecisions==1,"closeout counts unresolved scope but excludes closed QA");
  Sql("UPDATE role_permissions SET allowed=0 WHERE permission_code='Reports.Operations'");
  Denied(()=>s.ReadProjectCloseoutReview(p.Id),"closeout report requires operations grant");
  Sql("UPDATE role_permissions SET allowed=1 WHERE permission_code='Reports.Operations'");
  Invalid(()=>s.DecideScopeChange(p.Id,change,true,"Reviewed",true),"pending project cannot authorize additional work");
  var a=s.RecordSignedAgreement(p.Id,p.Hash,"signed.pdf","%PDF-1.7\nSynthetic\n%%EOF"u8.ToArray(),"Reviewed",true);ConflictReviewTests.Clear(s,p.Id);s.AuthorizeProjectStart(p.Id,p.Hash,a.Id,a.PdfHash,true,"Synthetic");
  Invalid(()=>s.DecideScopeChange(p.Id,change,true,"Reviewed",false),"explicit client review required");
  check(s.DecideScopeChange(p.Id,change,true,"Written synthetic approval",true)=="Authorized"&&s.ReadScopeChanges(p.Id).Single().State=="Authorized","scope approval recorded separately");
  check(s.DecideScopeChange(p.Id,change,false,"Changed",false)=="AlreadyAuthorized","scope authorization not silently reversed");
  var after=s.ReadProjectCloseoutReview(p.Id);check(after.ProjectState=="Active"&&after.PendingScopeDecisions==0&&after.ConflictStatus=="Clear","closeout reflects resolved scope and current project state");
  Invalid(()=>s.ReadProjectCloseoutReview(long.MaxValue),"closeout rejects nonexistent project");
 }
}
