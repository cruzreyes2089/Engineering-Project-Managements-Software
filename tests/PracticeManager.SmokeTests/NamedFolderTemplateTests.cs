using Microsoft.Data.Sqlite;
using PracticeManager.Core;
using PracticeManager.Persistence;
internal static class NamedFolderTemplateTests
{
 public static void Run(string root,string pw,Action<bool,string> check)
 {
  var path=Path.Combine(root,"named-template.db");var s=new LocalPractice(path);s.Bootstrap("Synthetic firm","admin",pw,"AA");
  check(s.Login("admin",pw),"named template admin");
  void Reject<T>(Action action,string label) where T:Exception
  {try{action();}catch(T){check(true,label);return;}throw new Exception("FAIL: "+label);}
  check(s.ReadNamedFolderChoices().Single().Revision==0,"CRC is an available preset");
  foreach(var bad in new[]{new[]{"Design/Child"},new[]{"Design","design/Child"},new[]{"Design","Design/../x"},new[]{"Design","Design/Billing Administration"},new[]{"Design","Design/Child","Design/child"}})
   Reject<ArgumentException>(()=>s.SaveNamedFolderChoice("Synthetic",bad,0,true),"unsafe nested template rejected");
  Reject<ArgumentException>(()=>s.SaveNamedFolderChoice("Suggested preset",new[]{"Design"},0,true),"new reserved preset name rejected");
  var first=s.SaveNamedFolderChoice("Residential",new[]{"Design","Design/Drawings","Design/Calculations","Delivery"},0,true);
  var second=s.SaveNamedFolderChoice("Commercial",new[]{"Reports","Reference"},0,true);
  check(s.ReadNamedFolderChoices().Count==3,"multiple named templates available");
  Reject<ArgumentException>(()=>s.SaveNamedFolderChoice("residential",new[]{"Other"},0,true),"duplicate template name rejected");
  var client=s.SaveClient(new Client(0,"","Business / Company","Synthetic Client","","","","","","","","","","","Active"));
  long Project(string title){var p=s.CreateProposal(new(client,title,"Site","Scope",100m));var h=ConflictReviewTests.Issue(s,p,1);return s.RecordProposalDecision(p,h,true,"Synthetic").ProjectId!.Value;}
  var project=Project("One");var other=Project("Two");
  check(!s.ReadProjectFolderPreview(project).Selection.Assigned,"preset suggestion is not assignment");
  Reject<ArgumentException>(()=>s.SelectProjectFolderTemplate(project,first.Revision,true,0,false),"explicit selection confirmation required");
  var selected=s.SelectProjectFolderTemplate(project,first.Revision,true,0,true);
  var preview=s.ReadProjectFolderPreview(project);
  check(preview.Selection==selected&&preview.Production.First().Children.Count==2&&preview.Plan.Entries.Any(x=>x.RelativeDirectory.EndsWith("/Design/Drawings"))&&preview.Plan.IncludeCorrespondence,"nested template bound to project");
  var draft=s.ReadProjectFolderCustomization(project);
  check(draft.SelectionId==selected.Id&&draft.Paths.SequenceEqual(first.Paths),"project customization starts from selected template");
  Reject<ArgumentException>(()=>s.ReadProjectFolderCustomization(other),"unassigned project requires explicit base template");
  Reject<ArgumentException>(()=>s.SaveProjectFolderCustomization(project,selected.Id,new[]{"Design"},false,false),"project customization requires confirmation");
  Reject<ArgumentException>(()=>s.SaveProjectFolderCustomization(project,selected.Id,new[]{"Design","Design/../Private"},false,true),"unsafe project override denied");
  var edited=s.SaveProjectFolderCustomization(project,selected.Id,new[]{"Design","Design/Details","Send Outs"},false,true);
  var customPreview=s.ReadProjectFolderPreview(project);
  check(edited.Customized&&edited.TemplateRevision==first.Revision&&customPreview.TemplateRevision.StartsWith("Project-Production-",StringComparison.Ordinal)&&
   customPreview.Production.First().Children.Single().Name=="Details"&&!customPreview.Plan.IncludeCorrespondence&&customPreview.Restricted.Count==4,
   "project override preserves template provenance and separate restricted boundary");
  check(s.ReadNamedFolderChoices().Single(x=>x.Revision==first.Revision).Paths.SequenceEqual(first.Paths)&&!s.ReadProjectFolderPreview(other).Selection.Assigned,
   "project override does not change catalog or other project");
  Reject<ArgumentException>(()=>s.SaveProjectFolderCustomization(project,selected.Id,new[]{"Other"},true,true),"stale project customization rejected");
  check(s.SaveProjectFolderCustomization(project,edited.Id,new[]{"Design","Design/Details","Send Outs"},false,true)==edited,"identical project override no-op");
  var storage=s.ReviewProjectStorageDraft(project,@"C:\Synthetic Production",@"D:\Synthetic Restricted",true);
  check(storage.TemplateRevision==customPreview.TemplateRevision&&storage.ProductionDirectories==4&&storage.RestrictedDirectories==5,"storage review uses exact saved customization and option");
  var changed=s.SaveNamedFolderChoice("Residential",new[]{"New Design"},first.Revision,true);
  check(s.ReadProjectFolderPreview(project).Plan.Fingerprint==customPreview.Plan.Fingerprint,"existing project retains exact custom plan after business template update");
  Reject<ArgumentException>(()=>s.SelectProjectFolderTemplate(other,first.Revision,false,0,true),"stale catalog choice rejected");
  var next=s.SelectProjectFolderTemplate(other,changed.Revision,false,0,true);
  check(s.ReadProjectFolderPreview(other).Production.Single().Name=="New Design"&&next.Assigned,"new project selects current revision");
  Reject<ArgumentException>(()=>s.SelectProjectFolderTemplate(project,second.Revision,false,0,true),"stale project selection rejected");
  var preset=s.SelectProjectFolderTemplate(project,0,false,edited.Id,true);
  check(preset.Assigned&&!preset.Customized&&s.ReadProjectFolderPreview(project).Production.Count==9&&!s.ReadProjectFolderPreview(project).Plan.IncludeCorrespondence,"explicit CRC selection replaces logical plan");
  check(s.ReadProjectDocuments(project).Count==0&&!s.ReadProjectFolderPreview(project).Provisioned,"no external provisioning or document records");
  using(var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=path,Pooling=false,ForeignKeys=true}.ToString()))
  {db.Open();using var q=db.CreateCommand();q.CommandText="DELETE FROM project_folder_selections";Reject<SqliteException>(()=>q.ExecuteNonQuery(),"project template history retained");q.CommandText="UPDATE named_folder_templates SET name='Changed'";Reject<SqliteException>(()=>q.ExecuteNonQuery(),"named template revisions immutable");}
  using(var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=path,Pooling=false,ForeignKeys=true}.ToString()))
  {
   db.Open();using var q=db.CreateCommand();q.CommandText="SELECT count(*) FROM project_folder_selections WHERE override_paths IS NOT NULL";
   check(Convert.ToInt64(q.ExecuteScalar())==1,"original custom selection retained after template reset");
   q.CommandText="UPDATE role_permissions SET allowed=0 WHERE permission_code='Documents.Restricted'";q.ExecuteNonQuery();
   Reject<UnauthorizedAccessException>(()=>s.ReadProjectFolderCustomization(project),"customization settings permission enforced");
   Reject<UnauthorizedAccessException>(()=>s.SaveProjectFolderCustomization(project,preset.Id,new[]{"Other"},false,true),"customization write permission enforced");
   q.CommandText="UPDATE role_permissions SET allowed=1 WHERE permission_code='Documents.Restricted'";q.ExecuteNonQuery();
  }
  using(var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=path,Pooling=false,ForeignKeys=true}.ToString())){
   db.Open();using var q=db.CreateCommand();q.CommandText="INSERT INTO named_folder_templates(firm_id,name,paths,recorded_by,recorded_utc) VALUES(1,'Suggested preset','[\"Legacy Design\"]',1,'2026-01-01T00:00:00Z')";q.ExecuteNonQuery();
  }
  check(s.ReadNamedFolderChoices().Count(x=>x.Name=="Suggested preset")==2&&s.ReadNamedFolderChoices().Single(x=>x.Name=="Suggested preset"&&x.Revision>0).Paths.Single()=="Legacy Design","legacy reserved name remains readable by distinct revision");
  s.Logout();var reopened=new LocalPractice(path);check(reopened.Login("admin",pw)&&reopened.ReadProjectFolderPreview(project).Selection==preset,"selection survives restart");
 }
}
