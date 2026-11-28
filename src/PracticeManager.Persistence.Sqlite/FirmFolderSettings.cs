using System.Text.Json;
using Microsoft.Data.Sqlite;
using PracticeManager.Core;
namespace PracticeManager.Persistence;

public sealed partial class LocalPractice
{
 private static void UpgradeToV49(SqliteConnection c,SqliteTransaction t)
 {
  Exec(c,t,@"CREATE TABLE firm_folder_templates(
   id INTEGER PRIMARY KEY,firm_id INTEGER NOT NULL REFERENCES firms(id) CHECK(firm_id=1),
   production_names TEXT NOT NULL CHECK(length(production_names) BETWEEN 3 AND 16000),
   recorded_by INTEGER NOT NULL REFERENCES users(id),recorded_utc TEXT NOT NULL);
   CREATE TRIGGER firm_folder_template_no_update BEFORE UPDATE ON firm_folder_templates
    BEGIN SELECT RAISE(ABORT,'Folder template retained'); END;
   CREATE TRIGGER firm_folder_template_no_delete BEFORE DELETE ON firm_folder_templates
    BEGIN SELECT RAISE(ABORT,'Folder template retained'); END;
   PRAGMA user_version=49;");
 }
 private static ProjectFolderLayout CurrentFolderLayout(SqliteConnection c,SqliteTransaction t)
 {
  using var q=Command(c,t,"SELECT id,production_names FROM firm_folder_templates WHERE firm_id=1 ORDER BY id DESC LIMIT 1");
  using var reader=q.ExecuteReader();
  if(!reader.Read())return ProjectFolderLayout.Default();
  var names=JsonSerializer.Deserialize<string[]>(reader.GetString(1))??throw new InvalidOperationException("Folder layout unavailable.");
  return ProjectFolderLayout.Custom(reader.GetInt64(0),names);
 }
 public ProjectFolderLayout ReadFirmFolderLayout()
 {
  using var c=Open();using var t=c.BeginTransaction();RequireStorageSettings(c,t);
  return CurrentFolderLayout(c,t);
 }
 public ProjectFolderLayout SaveFirmFolderLayout(IReadOnlyList<string> names,long expectedRevision,bool explicitlyConfirmed)
 {
  using var c=Open();using var t=c.BeginTransaction(deferred:false);
  var actor=RequireStorageSettings(c,t);
  if(!explicitlyConfirmed)throw new ArgumentException("Confirm the proposed folder template.");
  var validated=ProjectFolderLayout.ValidateNames(names);
  var current=CurrentFolderLayout(c,t);
  if(current.RevisionId!=expectedRevision)throw new ArgumentException("Template changed. Reload before saving.");
  if(current.Names.SequenceEqual(validated))return current;
  Exec(c,t,"INSERT INTO firm_folder_templates(firm_id,production_names,recorded_by,recorded_utc) VALUES(1,$n,$a,$u)",
   ("$n",JsonSerializer.Serialize(validated)),("$a",actor),("$u",UtcNow()));
  var id=Last(c,t);Audit(c,t,actor,"RecordFolderTemplate","Firm",1);t.Commit();
  return ProjectFolderLayout.Custom(id,validated);
 }
}
