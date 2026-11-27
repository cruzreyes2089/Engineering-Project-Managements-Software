using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using PracticeManager.Persistence;

internal static class FirmStoragePreferenceTests
{
 public static void Run(string root,string pw,Action<bool,string> check)
 {
  var folder=Path.Combine(root,"firm-storage-preferences");Directory.CreateDirectory(folder);
  var path=Path.Combine(folder,"business-a.db");var otherPath=Path.Combine(folder,"business-b.db");
  var a=new LocalPractice(path);var b=new LocalPractice(otherPath);
  a.Bootstrap("Synthetic Business A","admin",pw,"AA");b.Bootstrap("Synthetic Business B","admin",pw,"BB");
  void Reject<T>(Action action,string label) where T:Exception
  {try{action();}catch(T){check(true,label);return;}throw new Exception("FAIL: "+label);}
  Reject<UnauthorizedAccessException>(()=>a.ReadFirmStoragePreference(),"anonymous storage preference read denied");
  check(a.Login("admin",pw)&&b.Login("admin",pw),"separate synthetic firms authenticated");
  check(!a.ReadFirmStoragePreference().Configured&&!b.ReadFirmStoragePreference().Configured,"business storage has no hardcoded default");
  Reject<ArgumentException>(()=>a.SaveFirmStoragePreference(@"D:\Synthetic A\Projects",@"D:\Synthetic A\Restricted",0,false),"storage preference confirmation required");
  var first=a.SaveFirmStoragePreference(@"D:\Synthetic A\Projects",@"D:\Synthetic A\Restricted",0,true);
  check(first.Configured&&!first.FilesystemVerified&&!first.ProvisioningAuthorized&&!b.ReadFirmStoragePreference().Configured,"storage preference isolated per database, not activated");
  var second=b.SaveFirmStoragePreference(@"C:\Synthetic B\Jobs",@"E:\Synthetic B\Private",0,true);
  check(second.ProductionRoot!=first.ProductionRoot&&a.ReadFirmStoragePreference()==first,"second business has independently configured storage");
  var edited=a.SaveFirmStoragePreference(@"F:\Synthetic A\Production",@"F:\Synthetic A\Finance",first.Revision,true);
  check(edited.Revision>first.Revision&&a.ReadFirmStoragePreference()==edited&&b.ReadFirmStoragePreference()==second,"business storage preference editable without changing another business");
  Reject<ArgumentException>(()=>a.SaveFirmStoragePreference(@"C:\Synthetic",@"D:\Synthetic",first.Revision,true),"stale preference update rejected");
  Reject<ArgumentException>(()=>a.SaveFirmStoragePreference(@"C:\Synthetic",@"C:\Synthetic\Private",edited.Revision,true),"overlapping preference roots rejected");
  check(a.SaveFirmStoragePreference(edited.ProductionRoot,edited.RestrictedRoot,edited.Revision,true)==edited,"identical preference save idempotent");
  using(var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=path,Pooling=false,ForeignKeys=true}.ToString()))
  {
   db.Open();using var q=db.CreateCommand();
   q.CommandText="SELECT count(*) FROM firm_storage_preferences";check(Convert.ToInt64(q.ExecuteScalar())==2,"storage preference history retains revisions");
   q.CommandText="SELECT count(*) FROM audit_events WHERE action='RecordStoragePreference'";check(Convert.ToInt64(q.ExecuteScalar())==2,"storage preference changes audited");
   q.CommandText="DELETE FROM firm_storage_preferences";Reject<SqliteException>(()=>q.ExecuteNonQuery(),"storage preference history immutable");
   q.CommandText="UPDATE role_permissions SET allowed=0 WHERE permission_code='Documents.Restricted'";q.ExecuteNonQuery();
   Reject<UnauthorizedAccessException>(()=>a.ReadFirmStoragePreference(),"restricted preference read denied after revocation");
   Reject<UnauthorizedAccessException>(()=>a.SaveFirmStoragePreference(edited.ProductionRoot,edited.RestrictedRoot,edited.Revision,true),"restricted preference write denied after revocation");
   q.CommandText="UPDATE role_permissions SET allowed=1 WHERE permission_code='Documents.Restricted'";q.ExecuteNonQuery();
  }
  a.Logout();var reopened=new LocalPractice(path);check(reopened.Login("admin",pw)&&reopened.ReadFirmStoragePreference()==edited,"saved storage preferences survive application restart");
  var backup=reopened.CreateRecoveryCopy(Path.Combine(folder,"storage-recovery.db"));
  var restore=reopened.RestoreRecoveryCopyToNewFile(backup.Path,backup.Sha256,Path.Combine(folder,"storage-restored.db"));
  var isolated=new LocalPractice(restore.Path);
  check(isolated.Login("admin",pw)&&isolated.ReadFirmStoragePreference()==edited&&!isolated.ReadFirmStoragePreference().ProvisioningAuthorized,"isolated recovery preserves inactive business preferences");
  isolated.Logout();reopened.Logout();b.Logout();
  // Explicit previous-schema fixture: the new table is absent in a schema 47 database.
  var old=Path.Combine(folder,"schema47.db");File.Copy(path,old);
  using(var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=old,Pooling=false}.ToString()))
  {db.Open();using var q=db.CreateCommand();q.CommandText="DROP TABLE project_folder_selections; DROP TABLE named_folder_templates; DROP TABLE firm_folder_templates; DROP TABLE firm_storage_preferences; PRAGMA user_version=47";q.ExecuteNonQuery();}
  var hash=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(old))).ToLowerInvariant();
  var rehearsal=UpgradeRehearsal.Run(old,hash,true);
  check(rehearsal.SourceSchema==47&&rehearsal.TargetSchema==50&&rehearsal.SourceUnchanged,"schema 47 to current storage preference upgrade rehearsal");
  var upgraded=new LocalPractice(old);
  check(upgraded.Login("admin",pw)&&!upgraded.ReadFirmStoragePreference().Configured,"upgraded business has no invented storage preferences");
  upgraded.Logout();
 }
}
