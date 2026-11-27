using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using PracticeManager.Core;
using PracticeManager.Persistence;
internal static class FirmFolderTemplateTests
{
 public static void Run(string root,string pw,Action<bool,string> check)
 {
  var path=Path.Combine(root,"folder-firm-a.db");var other=Path.Combine(root,"folder-firm-b.db");
  var a=new LocalPractice(path);var b=new LocalPractice(other);
  a.Bootstrap("Synthetic A","admin",pw,"AA");b.Bootstrap("Synthetic B","admin",pw,"BB");
  void Reject<T>(Action action,string label) where T:Exception
  {try{action();}catch(T){check(true,label);return;}throw new Exception("FAIL: "+label);}
  Reject<UnauthorizedAccessException>(()=>a.ReadFirmFolderLayout(),"anonymous template read denied");
  check(a.Login("admin",pw)&&b.Login("admin",pw),"separate template businesses authenticated");
  var initial=a.ReadFirmFolderLayout();
  check(initial.RevisionId==0&&initial.Names.Count==9&&initial.Names.Contains("QA-QC")&&!initial.Names.Contains("MEP"),"standard preset preserved");
  var client=a.SaveClient(new Client(0,"","Business / Company","Synthetic Client","","","","","","","","","","","Active"));
  var proposal=a.CreateProposal(new(client,"Synthetic project","Site","Scope",100m));
  var hash=ConflictReviewTests.Issue(a,proposal,1);var project=a.RecordProposalDecision(proposal,hash,true,"Synthetic").ProjectId!.Value;
  var original=a.ReadProjectFolderPreview(project);
  string[] custom=["Design","Field Reports","Reference Documents","Deliverables"];
  Reject<ArgumentException>(()=>a.SaveFirmFolderLayout(custom,0,false),"template change requires confirmation");
  foreach(var bad in new[]{"../Design","A\\B","A/B","NUL.txt","COM¹","A:stream","A."," A","Correspondence","Billing Administration"})
   Reject<ArgumentException>(()=>a.SaveFirmFolderLayout(new[]{bad},0,true),"unsafe or reserved folder label rejected");
  Reject<ArgumentException>(()=>a.SaveFirmFolderLayout(new[]{"Design","design"},0,true),"case-insensitive duplicate template label rejected");
  Reject<ArgumentException>(()=>a.SaveFirmFolderLayout(Array.Empty<string>(),0,true),"empty template rejected");
  var saved=a.SaveFirmFolderLayout(custom,0,true);
  check(saved.RevisionId>0&&saved.Names.SequenceEqual(custom)&&b.ReadFirmFolderLayout().RevisionId==0,"custom template scoped to one business");
  custom[0]="Mutated input";
  check(a.ReadFirmFolderLayout().Names[0]=="Design"&&saved.Names[0]=="Design","template names snapshot immutable");
  var updated=a.ReadProjectFolderPreview(project,true);
  check(updated.Production.Select(x=>x.Name).SequenceEqual(new[]{"Design","Field Reports","Reference Documents","Deliverables","Correspondence"})&&
    updated.Restricted.Count==4&&updated.Plan.Fingerprint!=original.Plan.Fingerprint&&updated.TemplateRevision==saved.Revision,
    "project preview uses saved custom template and keeps restricted structure");
  var draft=a.ReviewProjectStorageDraft(project,@"C:\Synthetic Jobs",@"D:\Synthetic Private",true);
  check(draft.TemplateRevision==updated.TemplateRevision&&draft.ProductionDirectories==6&&draft.RestrictedDirectories==5,"storage draft uses same custom template");
  Reject<ArgumentException>(()=>a.SaveFirmFolderLayout(new[]{"Other"},0,true),"stale template save rejected");
  check(a.SaveFirmFolderLayout(saved.Names,saved.RevisionId,true).RevisionId==saved.RevisionId,"identical template save is a no-op");
  using(var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=path,Pooling=false,ForeignKeys=true}.ToString()))
  {
   db.Open();using var q=db.CreateCommand();
   q.CommandText="UPDATE role_permissions SET allowed=0 WHERE permission_code='Documents.Restricted'";q.ExecuteNonQuery();
   Reject<UnauthorizedAccessException>(()=>a.ReadFirmFolderLayout(),"template settings require restricted grant");
   Reject<UnauthorizedAccessException>(()=>a.SaveFirmFolderLayout(saved.Names,saved.RevisionId,true),"template change denied without restricted grant");
   var ordinary=a.ReadProjectFolderPreview(project);
   check(ordinary.Production.Count==4&&ordinary.Restricted.Count==0&&ordinary.Plan.Entries.All(x=>x.Boundary=="Production"),"custom production visible without restricted metadata");
   q.CommandText="UPDATE role_permissions SET allowed=1 WHERE permission_code='Documents.Restricted'";q.ExecuteNonQuery();
   q.CommandText="DELETE FROM firm_folder_templates";Reject<SqliteException>(()=>q.ExecuteNonQuery(),"template history immutable");
  }
  var reset=a.SaveFirmFolderLayout(initial.Names,saved.RevisionId,true);
  check(reset.RevisionId>saved.RevisionId&&reset.Names.SequenceEqual(initial.Names)&&a.ReadProjectFolderPreview(project).Production.Count==9,"restore standard preset creates new revision");
  check(a.ReadProjectDocuments(project).Count==0&&!a.ReadProjectFolderPreview(project).Provisioned,"template change creates no document records or physical folders");
  a.Logout();var reopened=new LocalPractice(path);check(reopened.Login("admin",pw)&&reopened.ReadFirmFolderLayout().RevisionId==reset.RevisionId,"custom template persists across reopen");
  var backup=reopened.CreateRecoveryCopy(Path.Combine(root,"folder-template-recovery.db"));
  var restore=reopened.RestoreRecoveryCopyToNewFile(backup.Path,backup.Sha256,Path.Combine(root,"folder-template-restored.db"));
  var restored=new LocalPractice(restore.Path);
  check(restored.Login("admin",pw)&&restored.ReadFirmFolderLayout().RevisionId==reset.RevisionId&&!restored.ReadProjectFolderPreview(project).Provisioned,"recovery retains inactive template history");
  var old=Path.Combine(root,"folder-template-schema48.db");File.Copy(path,old);
  using(var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=old,Pooling=false}.ToString()))
  {db.Open();using var q=db.CreateCommand();q.CommandText="DROP TABLE firm_folder_templates; PRAGMA user_version=48";q.ExecuteNonQuery();}
  var digest=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(old))).ToLowerInvariant();
  var rehearsal=UpgradeRehearsal.Run(old,digest,true);
  check(rehearsal.SourceSchema==48&&rehearsal.TargetSchema==49&&rehearsal.SourceUnchanged,"schema 48 to 49 template rehearsal");
  var upgraded=new LocalPractice(old);check(upgraded.Login("admin",pw)&&upgraded.ReadFirmFolderLayout().RevisionId==0,"upgrade uses default without inventing template settings");
  upgraded.Logout();restored.Logout();reopened.Logout();b.Logout();
 }
}
