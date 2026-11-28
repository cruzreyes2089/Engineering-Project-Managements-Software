using Microsoft.Data.Sqlite;
namespace PracticeManager.Persistence;

public sealed record PreferredFolderTemplate(long Revision,long? TemplateRevision,bool CurrentTemplate)
{
 public bool Available=>TemplateRevision.HasValue&&CurrentTemplate;
}
public sealed partial class LocalPractice
{
 private static void UpgradeToV53(SqliteConnection c,SqliteTransaction t)
 {
  Exec(c,t,@"CREATE TRIGGER folder_template_rule_retired BEFORE INSERT ON folder_template_rules
    BEGIN SELECT RAISE(ABORT,'Category recommendations retired'); END;
   CREATE TABLE preferred_folder_templates(
    id INTEGER PRIMARY KEY,firm_id INTEGER NOT NULL REFERENCES firms(id) CHECK(firm_id=1),
    template_id INTEGER REFERENCES named_folder_templates(id),
    enabled INTEGER NOT NULL CHECK(enabled IN(0,1)),
    recorded_by INTEGER NOT NULL REFERENCES users(id),recorded_utc TEXT NOT NULL,
    CHECK(enabled=1 OR template_id IS NULL));
   CREATE TRIGGER preferred_folder_no_update BEFORE UPDATE ON preferred_folder_templates BEGIN SELECT RAISE(ABORT,'Preference retained'); END;
   CREATE TRIGGER preferred_folder_no_delete BEFORE DELETE ON preferred_folder_templates BEGIN SELECT RAISE(ABORT,'Preference retained'); END;
   PRAGMA user_version=53;");
 }
 private static PreferredFolderTemplate CurrentPreferredTemplate(SqliteConnection c,SqliteTransaction t)
 {
  long id,template;
  using(var q=Command(c,t,"SELECT id,coalesce(template_id,0),enabled FROM preferred_folder_templates WHERE firm_id=1 ORDER BY id DESC LIMIT 1"))
  using(var reader=q.ExecuteReader())
  {
   if(!reader.Read())return new(0,null,false);
   id=reader.GetInt64(0);template=reader.GetInt64(1);
   if(reader.GetInt64(2)==0)return new(id,null,false);
  }
  var choice=ReadChoice(c,t,template);
  bool current=template==0||Convert.ToInt64(Scalar(c,t,"SELECT max(id) FROM named_folder_templates WHERE firm_id=1 AND name=$n COLLATE NOCASE",("$n",choice.Name)))==template;
  return new(id,template,current);
 }
 public PreferredFolderTemplate ReadPreferredFolderTemplate()
 {
  using var c=Open();using var t=c.BeginTransaction();RequireStorageSettings(c,t);
  return CurrentPreferredTemplate(c,t);
 }
 public PreferredFolderTemplate SavePreferredFolderTemplate(long? templateRevision,long expectedRevision,bool confirmed)
 {
  using var c=Open();using var t=c.BeginTransaction(deferred:false);var actor=RequireStorageSettings(c,t);
  if(!confirmed)throw new ArgumentException("Confirm the preferred template change.");
  var previous=CurrentPreferredTemplate(c,t);
  if(previous.Revision!=expectedRevision)throw new ArgumentException("Preference changed. Reload.");
  if(templateRevision is long revision)
  {
   var choice=ReadChoice(c,t,revision);
   if(revision>0&&Convert.ToInt64(Scalar(c,t,"SELECT max(id) FROM named_folder_templates WHERE firm_id=1 AND name=$n COLLATE NOCASE",("$n",choice.Name)))!=revision)
    throw new ArgumentException("Choose the current template revision.");
  }
  if(previous.TemplateRevision==templateRevision)return previous;
  Exec(c,t,"INSERT INTO preferred_folder_templates(firm_id,template_id,enabled,recorded_by,recorded_utc) VALUES(1,$i,$e,$a,$u)",
   ("$i",templateRevision is >0?(object?)templateRevision.Value:null),("$e",templateRevision.HasValue?1:0),("$a",actor),("$u",UtcNow()));
  var id=Last(c,t);Audit(c,t,actor,"RecordPreferredFolderTemplate","Firm",1);t.Commit();
  return new(id,templateRevision,templateRevision.HasValue);
 }
}
