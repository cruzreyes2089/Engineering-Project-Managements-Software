using Microsoft.Data.Sqlite;
using PracticeManager.Core;
namespace PracticeManager.Persistence;

// A durable proposed assignment, not a storage registration or permission to provision.
public sealed record ProjectStorageBinding(long Revision,long ProjectId,long PreferenceRevision,long SelectionId,
 string ProductionRoot,string RestrictedRoot,string DraftFingerprint,bool CurrentSelection,bool CurrentPreference)
{
 public bool FilesystemVerified=>false;
 public bool ProvisioningAuthorized=>false;
 public override string ToString()=>"[Inactive project storage binding]";
}
public sealed record ProjectStorageBindingReview(long ProjectId,long ExpectedBinding,long PreferenceRevision,long SelectionId,ProjectStorageDraft Draft)
{
 public override string ToString()=>"[Project storage binding review]";
}
public sealed partial class LocalPractice
{
 private static void UpgradeToV54(SqliteConnection c,SqliteTransaction t)
 {
  Exec(c,t,@"CREATE TABLE project_storage_bindings(
   id INTEGER PRIMARY KEY,
   project_id INTEGER NOT NULL REFERENCES projects(id),
   preference_id INTEGER NOT NULL REFERENCES firm_storage_preferences(id),
   selection_id INTEGER NOT NULL REFERENCES project_folder_selections(id),
   draft_hash TEXT NOT NULL CHECK(length(draft_hash)=64 AND draft_hash NOT GLOB '*[^0-9a-f]*'),
   recorded_by INTEGER NOT NULL REFERENCES users(id),recorded_utc TEXT NOT NULL);
   CREATE INDEX project_storage_binding_project ON project_storage_bindings(project_id,id);
   CREATE TRIGGER project_storage_binding_scope BEFORE INSERT ON project_storage_bindings
    WHEN NOT EXISTS(SELECT 1 FROM project_folder_selections WHERE id=NEW.selection_id AND project_id=NEW.project_id)
    BEGIN SELECT RAISE(ABORT,'Project selection mismatch'); END;
   CREATE TRIGGER project_storage_binding_no_update BEFORE UPDATE ON project_storage_bindings BEGIN SELECT RAISE(ABORT,'Binding retained'); END;
   CREATE TRIGGER project_storage_binding_no_delete BEFORE DELETE ON project_storage_bindings BEGIN SELECT RAISE(ABORT,'Binding retained'); END;
   PRAGMA user_version=54;");
 }
 static ProjectStorageBinding? ReadBinding(SqliteConnection c,SqliteTransaction t,long project)
 {
  long id,pref,selection;string p,r,hash;
  using(var q=Command(c,t,@"SELECT b.id,b.preference_id,b.selection_id,p.production_root,p.restricted_root,b.draft_hash
   FROM project_storage_bindings b JOIN firm_storage_preferences p ON p.id=b.preference_id AND p.firm_id=1
   WHERE b.project_id=$p ORDER BY b.id DESC LIMIT 1",("$p",project)))
  using(var reader=q.ExecuteReader())
  {
   if(!reader.Read())return null;
   id=reader.GetInt64(0);pref=reader.GetInt64(1);selection=reader.GetInt64(2);p=reader.GetString(3);r=reader.GetString(4);hash=reader.GetString(5);
  }
  var row=ProjectRow(c,t,project);
  ProjectFolderSelection stored;
  using(var q=Command(c,t,"SELECT coalesce(template_id,0),correspondence,override_paths IS NOT NULL FROM project_folder_selections WHERE id=$i AND project_id=$p",("$i",selection),("$p",project)))
  using(var reader=q.ExecuteReader())
  {
   if(!reader.Read())throw new InvalidOperationException("Stored project selection unavailable.");
   stored=new ProjectFolderSelection(selection,reader.GetInt64(0),reader.GetInt64(1)==1){Customized=reader.GetInt64(2)==1};
  }
  var draft=ProjectStorageDrafting.Build(p,r,project,row.Number,stored.IncludeCorrespondence,SelectedLayout(c,t,stored));
  if(draft.Fingerprint!=hash)throw new InvalidOperationException("Stored storage binding evidence mismatch.");
  return new(id,project,pref,selection,draft.ProductionRoot,draft.RestrictedRoot,hash,
   CurrentFolderSelection(c,t,project).Id==selection,CurrentStoragePreference(c,t).Revision==pref);
 }
 static ProjectStorageBindingReview BuildBindingReview(SqliteConnection c,SqliteTransaction t,long project)
 {
  var row=ProjectRow(c,t,project);
  var pref=CurrentStoragePreference(c,t);
  if(!pref.Configured)throw new ArgumentException("Save business storage preferences first.");
  var selection=CurrentFolderSelection(c,t,project);
  if(!selection.Assigned)throw new ArgumentException("Choose and confirm a project folder template first.");
  var draft=ProjectStorageDrafting.Build(pref.ProductionRoot,pref.RestrictedRoot,project,row.Number,selection.IncludeCorrespondence,SelectedLayout(c,t,selection));
  return new(project,ReadBinding(c,t,project)?.Revision??0,pref.Revision,selection.Id,draft);
 }
 public ProjectStorageBinding? ReadProjectStorageBinding(long project)
 {
  using var c=Open();using var t=c.BeginTransaction();RequireStorageSettings(c,t);RequireDocuments(c,t,false);
  _=ProjectRow(c,t,project);return ReadBinding(c,t,project);
 }
 public ProjectStorageBindingReview ReviewProjectStorageBinding(long project)
 {
  using var c=Open();using var t=c.BeginTransaction();RequireStorageSettings(c,t);RequireDocuments(c,t,false);
  return BuildBindingReview(c,t,project);
 }
 public ProjectStorageBinding SaveProjectStorageBinding(long project,long expectedBinding,long expectedPreference,long expectedSelection,string expectedFingerprint,bool confirmed)
 {
  using var c=Open();using var t=c.BeginTransaction(deferred:false);var actor=RequireStorageSettings(c,t);RequireDocuments(c,t,false);
  if(!confirmed)throw new ArgumentException("Confirm the inactive project storage binding.");
  var reviewed=BuildBindingReview(c,t,project);
  if(reviewed.ExpectedBinding!=expectedBinding||reviewed.PreferenceRevision!=expectedPreference||
     reviewed.SelectionId!=expectedSelection||reviewed.Draft.Fingerprint!=expectedFingerprint)
   throw new ArgumentException("Storage or project folder plan changed. Review again.");
  var current=ReadBinding(c,t,project);
  if(current!=null&&current.PreferenceRevision==expectedPreference&&current.SelectionId==expectedSelection&&current.DraftFingerprint==expectedFingerprint)return current;
  Exec(c,t,"INSERT INTO project_storage_bindings(project_id,preference_id,selection_id,draft_hash,recorded_by,recorded_utc) VALUES($p,$r,$s,$h,$a,$u)",
   ("$p",project),("$r",expectedPreference),("$s",expectedSelection),("$h",expectedFingerprint),("$a",actor),("$u",UtcNow()));
  var saved=ReadBinding(c,t,project)??throw new InvalidOperationException("Storage binding readback failed.");
  Audit(c,t,actor,"RecordProjectStorageBinding","Project",project);t.Commit();return saved;
 }
}
