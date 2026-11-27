using Microsoft.Data.Sqlite;
using PracticeManager.Core;
using PracticeManager.Persistence;
internal static class PreferredFolderTemplateTests
{
 public static void Run(string root,string pw,Action<bool,string> check)
 {
  var path=Path.Combine(root,"preferred-template.db");var s=new LocalPractice(path);s.Bootstrap("Synthetic firm","admin",pw,"AA");
  void Reject<T>(Action action,string label) where T:Exception
  {try{action();}catch(T){check(true,label);return;}throw new Exception("FAIL: "+label);}
  Reject<UnauthorizedAccessException>(()=>s.ReadPreferredFolderTemplate(),"anonymous preferred template denied");
  check(s.Login("admin",pw),"preferred template administrator authenticated");
  check(s.ReadNamedFolderChoices().Single().Name=="Suggested preset"&&!s.ReadPreferredFolderTemplate().Available,"neutral preset and no mandatory preference");
  Reject<ArgumentException>(()=>s.SaveNamedFolderChoice("Suggested preset",new[]{"Design"},0,true),"suggested preset name reserved");
  var first=s.SaveNamedFolderChoice("Business layout",new[]{"Design","Design/Drawings"},0,true);
  Reject<ArgumentException>(()=>s.SavePreferredFolderTemplate(first.Revision,0,false),"preference confirmation required");
  var preferred=s.SavePreferredFolderTemplate(first.Revision,0,true);
  check(preferred.Available&&preferred.TemplateRevision==first.Revision,"preferred business template recorded");
  var client=s.SaveClient(new Client(0,"","Business / Company","Synthetic Client","","","","","","","","","","","Active"));
  var p=s.CreateProposal(new(client,"Synthetic project","Site","Scope",100m));var h=ConflictReviewTests.Issue(s,p,1);var project=s.RecordProposalDecision(p,h,true,"Synthetic").ProjectId!.Value;
  check(!s.ReadProjectFolderPreview(project).Selection.Assigned,"preference does not assign project");
  var selection=s.SelectProjectFolderTemplate(project,first.Revision,false,0,true);
  var fingerprint=s.ReadProjectFolderPreview(project).Plan.Fingerprint;
  var revised=s.SaveNamedFolderChoice(first.Name,new[]{"New Design"},first.Revision,true);
  check(!s.ReadPreferredFolderTemplate().Available,"changed preferred revision requires review");
  Reject<ArgumentException>(()=>s.SavePreferredFolderTemplate(first.Revision,preferred.Revision,true),"obsolete preference revision denied");
  var updated=s.SavePreferredFolderTemplate(revised.Revision,preferred.Revision,true);
  Reject<ArgumentException>(()=>s.SavePreferredFolderTemplate(null,preferred.Revision,true),"stale preference update denied");
  var preset=s.SavePreferredFolderTemplate(0,updated.Revision,true);
  check(preset.Available&&preset.TemplateRevision==0,"suggested preset may be explicitly preferred");
  var cleared=s.SavePreferredFolderTemplate(null,preset.Revision,true);
  check(!cleared.Available&&cleared.TemplateRevision==null&&s.SavePreferredFolderTemplate(null,cleared.Revision,true)==cleared,"preference may be cleared");
  check(s.ReadProjectFolderPreview(project).Selection==selection&&s.ReadProjectFolderPreview(project).Plan.Fingerprint==fingerprint,"preference changes preserve assigned project");
  using(var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=path,Pooling=false,ForeignKeys=true}.ToString()))
  {
   db.Open();using var q=db.CreateCommand();
   q.CommandText="SELECT count(*) FROM preferred_folder_templates";check(Convert.ToInt64(q.ExecuteScalar())==4,"preferred history retained");
   q.CommandText="DELETE FROM preferred_folder_templates";Reject<SqliteException>(()=>q.ExecuteNonQuery(),"preferred history immutable");
   q.CommandText="INSERT INTO folder_template_rules(firm_id,category,enabled,recorded_by,recorded_utc) VALUES(1,'Retired',1,1,'Synthetic')";
   Reject<SqliteException>(()=>q.ExecuteNonQuery(),"retired category rules cannot be created");
   q.CommandText="UPDATE role_permissions SET allowed=0 WHERE permission_code='Documents.Restricted'";q.ExecuteNonQuery();
   Reject<UnauthorizedAccessException>(()=>s.ReadPreferredFolderTemplate(),"preferred template permission enforced");
   Reject<UnauthorizedAccessException>(()=>s.SavePreferredFolderTemplate(0,cleared.Revision,true),"preferred update permission enforced");
   q.CommandText="UPDATE role_permissions SET allowed=1 WHERE permission_code='Documents.Restricted'";q.ExecuteNonQuery();
  }
  s.Logout();var reopened=new LocalPractice(path);check(reopened.Login("admin",pw)&&reopened.ReadPreferredFolderTemplate()==cleared,"preference survives restart");
  // Rehearse upgrading a schema 52 copy containing legacy category data.
  var old=Path.Combine(root,"preference-schema52.db");File.Copy(path,old);
  using(var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=old,Pooling=false,ForeignKeys=true}.ToString()))
  {
   db.Open();using var q=db.CreateCommand();
   q.CommandText="DROP TABLE project_storage_bindings; DROP TABLE preferred_folder_templates; DROP TRIGGER folder_template_rule_retired; INSERT INTO folder_template_rules(firm_id,category,enabled,recorded_by,recorded_utc) VALUES(1,'Legacy category',1,1,'Synthetic'); PRAGMA user_version=52";q.ExecuteNonQuery();
  }
  var upgraded=new LocalPractice(old);check(upgraded.Login("admin",pw)&&!upgraded.ReadPreferredFolderTemplate().Available&&upgraded.ReadProjectFolderPreview(project).Plan.Fingerprint==fingerprint,"schema 52 upgrade retains project selection without inventing preference");
  using(var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=old,Pooling=false,ForeignKeys=true}.ToString()))
  {
   db.Open();using var q=db.CreateCommand();q.CommandText="SELECT count(*) FROM folder_template_rules";check(Convert.ToInt64(q.ExecuteScalar())==1,"legacy category history preserved");
   q.CommandText="INSERT INTO folder_template_rules(firm_id,category,enabled,recorded_by,recorded_utc) VALUES(1,'New category',1,1,'Synthetic')";Reject<SqliteException>(()=>q.ExecuteNonQuery(),"upgraded category history read-only");
  }
 }
}
