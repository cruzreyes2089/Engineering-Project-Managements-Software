using System.Text.Json;
using Microsoft.Data.Sqlite;
using PracticeManager.Core;
namespace PracticeManager.Persistence;

public sealed record NamedFolderChoice(long Revision,string Name,IReadOnlyList<string> Paths)
{
 public string Display=>Name+(Revision==0?" (preset)":" — revision "+Revision);
 public override string ToString()=>"[Folder template choice]";
}
public sealed record ProjectFolderSelection(long Id,long TemplateRevision,bool IncludeCorrespondence)
{
 public bool Assigned=>Id>0;
 public bool Customized{get;init;}
}
public sealed partial class LocalPractice
{
 private static void UpgradeToV50(SqliteConnection c,SqliteTransaction t)
 {
  Exec(c,t,@"CREATE TABLE named_folder_templates(
   id INTEGER PRIMARY KEY,firm_id INTEGER NOT NULL REFERENCES firms(id) CHECK(firm_id=1),
   name TEXT NOT NULL COLLATE NOCASE CHECK(length(name) BETWEEN 1 AND 60),
   paths TEXT NOT NULL CHECK(length(paths) BETWEEN 3 AND 24000),
   recorded_by INTEGER NOT NULL REFERENCES users(id),recorded_utc TEXT NOT NULL);
   CREATE INDEX named_folder_name ON named_folder_templates(name,id);
   CREATE TABLE project_folder_selections(
   id INTEGER PRIMARY KEY,project_id INTEGER NOT NULL REFERENCES projects(id),
   template_id INTEGER REFERENCES named_folder_templates(id),
   correspondence INTEGER NOT NULL CHECK(correspondence IN(0,1)),
   recorded_by INTEGER NOT NULL REFERENCES users(id),recorded_utc TEXT NOT NULL);
   CREATE INDEX folder_selection_project ON project_folder_selections(project_id,id);
   CREATE TRIGGER named_folder_no_update BEFORE UPDATE ON named_folder_templates BEGIN SELECT RAISE(ABORT,'Template retained'); END;
   CREATE TRIGGER named_folder_no_delete BEFORE DELETE ON named_folder_templates BEGIN SELECT RAISE(ABORT,'Template retained'); END;
   CREATE TRIGGER folder_selection_no_update BEFORE UPDATE ON project_folder_selections BEGIN SELECT RAISE(ABORT,'Selection retained'); END;
   CREATE TRIGGER folder_selection_no_delete BEFORE DELETE ON project_folder_selections BEGIN SELECT RAISE(ABORT,'Selection retained'); END;
   PRAGMA user_version=50;");
 }
 private static void UpgradeToV51(SqliteConnection c,SqliteTransaction t)
 {
  Exec(c,t,@"ALTER TABLE project_folder_selections ADD COLUMN override_paths TEXT CHECK(override_paths IS NULL OR length(override_paths) BETWEEN 3 AND 24000);
   PRAGMA user_version=51;");
 }
 static string NamedLabel(string name)
 {
  if(string.IsNullOrWhiteSpace(name)||name.Length>60||name!=name.Trim()||
     name.Any(x=>!char.IsAsciiLetterOrDigit(x)&&x is not (' ' or '-' or '_'))||
     string.Equals(name,"CRC Engineering",StringComparison.OrdinalIgnoreCase))
   throw new ArgumentException("Use a distinct template name, 1–60 letters, numbers, spaces, hyphens or underscores.");
  return name;
 }
 static NamedFolderChoice ReadChoice(SqliteConnection c,SqliteTransaction t,long revision)
 {
  if(revision==0)return new(0,"CRC Engineering",ProjectFolderLayout.Default().Names);
  using var q=Command(c,t,"SELECT name,paths FROM named_folder_templates WHERE id=$i AND firm_id=1",("$i",revision));
  using var reader=q.ExecuteReader();
  if(!reader.Read())throw new ArgumentException("Template revision unavailable.");
  return new(revision,NamedLabel(reader.GetString(0)),ProjectFolderLayout.ValidatePaths(JsonSerializer.Deserialize<string[]>(reader.GetString(1))??throw new InvalidOperationException("Template paths unavailable.")));
 }
 public IReadOnlyList<NamedFolderChoice> ReadNamedFolderChoices()
 {
  using var c=Open();using var t=c.BeginTransaction();RequireStorageSettings(c,t);
  var result=new List<NamedFolderChoice>{ReadChoice(c,t,0)};
  var ids=new List<long>();
  using(var q=Command(c,t,"SELECT max(id) FROM named_folder_templates WHERE firm_id=1 GROUP BY name ORDER BY name"))
  using(var reader=q.ExecuteReader())while(reader.Read())ids.Add(reader.GetInt64(0));
  foreach(var id in ids)result.Add(ReadChoice(c,t,id));
  return result.AsReadOnly();
 }
 public NamedFolderChoice SaveNamedFolderChoice(string name,IReadOnlyList<string> paths,long expectedRevision,bool confirmed)
 {
  using var c=Open();using var t=c.BeginTransaction(deferred:false);var actor=RequireStorageSettings(c,t);
  if(!confirmed)throw new ArgumentException("Confirm template revision.");
  name=NamedLabel(name);var valid=ProjectFolderLayout.ValidatePaths(paths);
  var latest=Convert.ToInt64(Scalar(c,t,"SELECT coalesce(max(id),0) FROM named_folder_templates WHERE firm_id=1 AND name=$n COLLATE NOCASE",("$n",name)));
  if(latest!=expectedRevision)throw new ArgumentException("Template name exists or revision changed. Reload.");
  if(latest>0)
  {
   var prior=ReadChoice(c,t,latest);
   if(prior.Name==name&&prior.Paths.SequenceEqual(valid))return prior;
  }
  Exec(c,t,"INSERT INTO named_folder_templates(firm_id,name,paths,recorded_by,recorded_utc) VALUES(1,$n,$p,$a,$u)",("$n",name),("$p",JsonSerializer.Serialize(valid)),("$a",actor),("$u",UtcNow()));
  var id=Last(c,t);Audit(c,t,actor,"RecordNamedFolderTemplate","Firm",1);t.Commit();
  return new(id,name,valid);
 }
 static ProjectFolderSelection CurrentFolderSelection(SqliteConnection c,SqliteTransaction t,long project)
 {
  using var q=Command(c,t,"SELECT id,coalesce(template_id,0),correspondence,override_paths IS NOT NULL FROM project_folder_selections WHERE project_id=$p ORDER BY id DESC LIMIT 1",("$p",project));
  using var reader=q.ExecuteReader();
  return reader.Read()?new ProjectFolderSelection(reader.GetInt64(0),reader.GetInt64(1),reader.GetInt64(2)==1){Customized=reader.GetInt64(3)==1}:new(0,0,false);
 }
 static ProjectFolderLayout SelectedLayout(SqliteConnection c,SqliteTransaction t,ProjectFolderSelection selected)
 {
  if(!selected.Assigned)return CurrentFolderLayout(c,t);
  if(selected.Customized)
  {
   var json=Scalar(c,t,"SELECT override_paths FROM project_folder_selections WHERE id=$i",("$i",selected.Id)) as string
    ??throw new InvalidOperationException("Project folder customization unavailable.");
   return ProjectFolderLayout.ProjectOverride(selected.Id,JsonSerializer.Deserialize<string[]>(json)??throw new InvalidOperationException("Project paths unavailable."));
  }
  var choice=ReadChoice(c,t,selected.TemplateRevision);
  return choice.Revision==0?ProjectFolderLayout.Default():ProjectFolderLayout.Named(choice.Revision,choice.Paths);
 }
 public ProjectFolderSelection SelectProjectFolderTemplate(long project,long templateRevision,bool correspondence,long expectedSelection,bool confirmed)
 {
  using var c=Open();using var t=c.BeginTransaction(deferred:false);
  var actor=RequireStorageSettings(c,t);RequireDocuments(c,t,false);
  var row=ProjectRow(c,t,project);
  if(!confirmed)throw new ArgumentException("Confirm the project template selection.");
  var current=CurrentFolderSelection(c,t,project);
  if(current.Id!=expectedSelection)throw new ArgumentException("Project selection changed. Reload.");
  var choice=ReadChoice(c,t,templateRevision);
  if(choice.Revision>0&&Convert.ToInt64(Scalar(c,t,"SELECT max(id) FROM named_folder_templates WHERE firm_id=1 AND name=$n COLLATE NOCASE",("$n",choice.Name)))!=choice.Revision)
   throw new ArgumentException("Template revision changed. Reload.");
  var layout=choice.Revision==0?ProjectFolderLayout.Default():ProjectFolderLayout.Named(choice.Revision,choice.Paths);
  _=ProjectFolderPlanning.Build(project,row.Number,correspondence,true,layout);
  if(current.Assigned&&!current.Customized&&current.TemplateRevision==templateRevision&&current.IncludeCorrespondence==correspondence)return current;
  Exec(c,t,"INSERT INTO project_folder_selections(project_id,template_id,correspondence,recorded_by,recorded_utc) VALUES($p,$i,$c,$a,$u)",
   ("$p",project),("$i",templateRevision==0?(object?)null:templateRevision),("$c",correspondence?1:0),("$a",actor),("$u",UtcNow()));
  var id=Last(c,t);Audit(c,t,actor,"SelectProjectFolderTemplate","Project",project);t.Commit();
  return new(id,templateRevision,correspondence);
 }
}
