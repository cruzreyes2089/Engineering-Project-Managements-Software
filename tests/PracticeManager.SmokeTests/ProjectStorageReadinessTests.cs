using Microsoft.Data.Sqlite;
using PracticeManager.Core;
using PracticeManager.Persistence;
internal static class ProjectStorageReadinessTests
{
 public static void Run(string root,string pw,Action<bool,string> check)
 {
  var owned=Path.Combine(root,"storage-observation");Directory.CreateDirectory(owned);
  var path=Path.Combine(owned,"observation.db");var s=new LocalPractice(path);s.Bootstrap("Synthetic observation firm","admin",pw,"AA");
  check(s.Login("admin",pw),"storage observation admin authenticated");
  void Reject<T>(Action action,string label) where T:Exception
  {try{action();}catch(T){check(true,label);return;}throw new Exception("FAIL: "+label);}
  var client=s.SaveClient(new Client(0,"","Business / Company","Synthetic Client","","","","","","","","","","","Active"));
  var p=s.CreateProposal(new(client,"Synthetic project","Site","Scope",100m));var h=ConflictReviewTests.Issue(s,p,1);
  var project=s.RecordProposalDecision(p,h,true,"Synthetic").ProjectId!.Value;
  Reject<ArgumentException>(()=>s.ObserveProjectStorage(project,0,true),"readiness requires saved binding");
  if(!OperatingSystem.IsWindows()){Console.WriteLine("NOT QUALIFIED: Windows storage observation unavailable.");return;}
  var prod=Directory.CreateDirectory(Path.Combine(owned,"Production")).FullName;
  var restricted=Directory.CreateDirectory(Path.Combine(owned,"Restricted")).FullName;
  var pref=s.SaveFirmStoragePreference(prod,restricted,0,true);
  var selected=s.SelectProjectFolderTemplate(project,0,false,0,true);
  var review=s.ReviewProjectStorageBinding(project);
  var binding=s.SaveProjectStorageBinding(project,review.ExpectedBinding,review.PreferenceRevision,review.SelectionId,review.Draft.Fingerprint,true);
  Reject<ArgumentException>(()=>s.ObserveProjectStorage(project,binding.Revision,false),"inspection requires explicit confirmation");
  Reject<ArgumentException>(()=>s.ObserveProjectStorage(project,binding.Revision+1,true),"stale observation revision denied");
  var observed=s.ObserveProjectStorage(project,binding.Revision,true);
  var pp=Path.Combine(prod,review.Draft.ProjectNumber);
  var rp=Path.Combine(restricted,review.Draft.ProjectNumber);
  check(observed.Status=="NoExistingProjectDirectories"&&observed.Missing==15&&observed.Existing==0&&observed.Blocked==0&&observed.Fingerprint?.Length==64&&
   !observed.FilesystemVerified&&!observed.EffectiveAccessQualified&&!observed.ProvisioningAuthorized,"read-only observation of empty roots");
  check(!Directory.Exists(pp)&&!Directory.Exists(rp),"observation creates no project directories");
  check(s.ObserveProjectStorage(project,binding.Revision,true)==observed,"unchanged metadata observation deterministic");
  Directory.CreateDirectory(pp);
  var existing=s.ObserveProjectStorage(project,binding.Revision,true);
  check(existing.Status=="ExistingDirectoriesReviewRequired"&&existing.Existing==1&&existing.Missing==14&&existing.Fingerprint!=observed.Fingerprint,"existing project directory never silently adopted");
  var obstruction=Path.Combine(pp,"QA-QC");File.WriteAllText(obstruction,"retain");
  var collision=s.ObserveProjectStorage(project,binding.Revision,true);
  check(collision.Status=="PathBlocked"&&collision.Blocked==1&&File.ReadAllText(obstruction)=="retain","planned file collision reported without mutation");
  File.Delete(obstruction);
  var target=Directory.CreateDirectory(Path.Combine(owned,"Unrelated")).FullName;
  var link=Path.Combine(pp,"Architectural");bool made=false;
  try
  {
   try{Directory.CreateSymbolicLink(link,target);made=true;}
   catch(IOException){}catch(UnauthorizedAccessException){}catch(PlatformNotSupportedException){}
   if(made)
   {
    check(s.ObserveProjectStorage(project,binding.Revision,true).Status=="PathBlocked","reparse directory reported as blocked");
    check(!Directory.EnumerateFileSystemEntries(target).Any(),"redirected target untouched");
   }
   else Console.WriteLine("NOT QUALIFIED: storage observation symlink unavailable.");
  }
  finally{if(made)Directory.Delete(link,false);}
  Directory.Delete(restricted,false);
  check(s.ObserveProjectStorage(project,binding.Revision,true).Status=="RestrictedRoot:Missing","missing restricted root reported");
  Directory.CreateDirectory(restricted);
  using(var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=path,Pooling=false,ForeignKeys=true}.ToString()))
  {
   db.Open();using var q=db.CreateCommand();q.CommandText="UPDATE role_permissions SET allowed=0 WHERE permission_code='Documents.Restricted'";q.ExecuteNonQuery();
   Reject<UnauthorizedAccessException>(()=>s.ObserveProjectStorage(project,binding.Revision,true),"observation permission rechecked");
   q.CommandText="UPDATE role_permissions SET allowed=1 WHERE permission_code='Documents.Restricted'";q.ExecuteNonQuery();
  }
  _=s.SaveProjectFolderCustomization(project,selected.Id,new[]{"Design"},false,true);
  Reject<ArgumentException>(()=>s.ObserveProjectStorage(project,binding.Revision,true),"changed project plan blocks observation");
  check(s.ReadProjectDocuments(project).Count==0&&!s.ReadProjectStorageBinding(project)!.ProvisioningAuthorized,"observation has no document or activation side effects");
 }
}
