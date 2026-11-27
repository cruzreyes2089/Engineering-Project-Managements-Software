using Microsoft.Data.Sqlite;
using PracticeManager.Core;
using PracticeManager.Persistence;
internal static class ProjectStorageBindingTests
{
 public static void Run(string root,string pw,Action<bool,string> check)
 {
  var path=Path.Combine(root,"project-storage-binding.db");var s=new LocalPractice(path);s.Bootstrap("Synthetic firm","admin",pw,"AA");
  check(s.Login("admin",pw),"storage binding administrator authenticated");
  void Reject<T>(Action action,string label) where T:Exception
  {try{action();}catch(T){check(true,label);return;}throw new Exception("FAIL: "+label);}
  var client=s.SaveClient(new Client(0,"","Business / Company","Synthetic Client","","","","","","","","","","","Active"));
  long Project(string title){var p=s.CreateProposal(new(client,title,"Site","Scope",100m));var h=ConflictReviewTests.Issue(s,p,1);return s.RecordProposalDecision(p,h,true,"Synthetic").ProjectId!.Value;}
  var project=Project("One");var other=Project("Two");
  check(s.ReadProjectStorageBinding(project)==null,"project has no invented storage assignment");
  Reject<ArgumentException>(()=>s.ReviewProjectStorageBinding(project),"business preferences required");
  var pref=s.SaveFirmStoragePreference(@"C:\Synthetic Production",@"D:\Synthetic Restricted",0,true);
  Reject<ArgumentException>(()=>s.ReviewProjectStorageBinding(project),"explicit template selection required");
  var selected=s.SelectProjectFolderTemplate(project,0,false,0,true);
  var reviewed=s.ReviewProjectStorageBinding(project);
  ProjectStorageBinding Save(ProjectStorageBindingReview x,bool confirmed=true)=>s.SaveProjectStorageBinding(x.ProjectId,x.ExpectedBinding,x.PreferenceRevision,x.SelectionId,x.Draft.Fingerprint,confirmed);
  Reject<ArgumentException>(()=>Save(reviewed,false),"binding confirmation required");
  var binding=Save(reviewed);
  check(binding.CurrentSelection&&binding.CurrentPreference&&!binding.FilesystemVerified&&!binding.ProvisioningAuthorized&&binding.ProductionRoot==pref.ProductionRoot,"exact roots and plan saved inactive");
  check(s.ReadProjectStorageBinding(other)==null,"binding isolated to selected project");
  Reject<ArgumentException>(()=>s.SaveProjectStorageBinding(other,0,pref.Revision,selected.Id,reviewed.Draft.Fingerprint,true),"cross-project review denied");
  check(Save(s.ReviewProjectStorageBinding(project))==binding,"current exact assignment replay no-op");
  var changed=s.SaveFirmStoragePreference(@"E:\New Production",@"F:\New Restricted",pref.Revision,true);
  var held=s.ReadProjectStorageBinding(project)!;
  check(!held.CurrentPreference&&held.CurrentSelection&&held.ProductionRoot==pref.ProductionRoot&&held.Revision==binding.Revision,"business preference change never redirects saved project");
  Reject<ArgumentException>(()=>Save(reviewed),"stale binding review denied");
  var newReview=s.ReviewProjectStorageBinding(project);
  var edited=s.SaveProjectFolderCustomization(project,selected.Id,new[]{"Design","Design/Details"},false,true);
  check(!s.ReadProjectStorageBinding(project)!.CurrentSelection,"changed folder plan requires fresh storage review");
  Reject<ArgumentException>(()=>Save(newReview),"changed plan invalidates storage review");
  var final=Save(s.ReviewProjectStorageBinding(project));
  check(final.Revision>binding.Revision&&final.PreferenceRevision==changed.Revision&&final.SelectionId==edited.Id&&final.ProductionRoot==changed.ProductionRoot,"explicit new inactive binding retains history");
  using(var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=path,Pooling=false,ForeignKeys=true}.ToString()))
  {
   db.Open();using var q=db.CreateCommand();
   q.CommandText="SELECT count(*) FROM project_storage_bindings";check(Convert.ToInt64(q.ExecuteScalar())==2,"binding history retained");
   q.CommandText="DELETE FROM project_storage_bindings";Reject<SqliteException>(()=>q.ExecuteNonQuery(),"binding history immutable");
   q.CommandText=$"INSERT INTO project_storage_bindings(project_id,preference_id,selection_id,draft_hash,recorded_by,recorded_utc) VALUES({other},{pref.Revision},{selected.Id},'{binding.DraftFingerprint}',1,'Synthetic')";
   Reject<SqliteException>(()=>q.ExecuteNonQuery(),"database rejects foreign project selection");
   q.CommandText="UPDATE role_permissions SET allowed=0 WHERE permission_code='Documents.Restricted'";q.ExecuteNonQuery();
   Reject<UnauthorizedAccessException>(()=>s.ReadProjectStorageBinding(project),"binding roots restricted");
   Reject<UnauthorizedAccessException>(()=>s.ReviewProjectStorageBinding(project),"binding review permission enforced");
   Reject<UnauthorizedAccessException>(()=>Save(newReview),"binding write permission enforced");
   q.CommandText="UPDATE role_permissions SET allowed=1 WHERE permission_code='Documents.Restricted'";q.ExecuteNonQuery();
  }
  check(!s.ReadProjectFolderPreview(project).Provisioned&&s.ReadProjectDocuments(project).Count==0,"binding creates no folders or document records");
  s.Logout();var reopened=new LocalPractice(path);check(reopened.Login("admin",pw)&&reopened.ReadProjectStorageBinding(project)==final,"binding survives restart");
  var backup=reopened.CreateRecoveryCopy(Path.Combine(root,"binding-recovery.db"));
  var restore=reopened.RestoreRecoveryCopyToNewFile(backup.Path,backup.Sha256,Path.Combine(root,"binding-restored.db"));
  var restored=new LocalPractice(restore.Path);
  check(restored.Login("admin",pw)&&restored.ReadProjectStorageBinding(project)==final&&!restored.ReadProjectStorageBinding(project)!.ProvisioningAuthorized,"recovery preserves inactive binding only");
  restored.Logout();reopened.Logout();
 }
}
