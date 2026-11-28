using Microsoft.Data.Sqlite;
using PracticeManager.Core;
using PracticeManager.Persistence;
internal static class FolderConfigurationTests
{
 public static void Run(string root,Action<bool,string> check)
 {
  var fixture=Path.Combine(root,"folder-configuration-fixture");Directory.CreateDirectory(fixture);
  var external=Path.Combine(fixture,"projects");var data=Path.Combine(fixture,"db");
  WorkspaceConfidentiality.EnsureWorkspaceDirectory(external);Directory.CreateDirectory(data);
  var project=Path.Combine(external,"Synthetic Project");WorkspaceConfidentiality.EnsureWorkspaceDirectory(project);
  var existing=Path.Combine(project,"Calculations");WorkspaceConfidentiality.EnsureWorkspaceDirectory(existing);
  var marker=Path.Combine(existing,"preserve.txt");File.WriteAllText(marker,"Synthetic existing file");
  var dbPath=Path.Combine(data,"synthetic.db");
  using var db=new SqliteConnection("Data Source=:memory:;Foreign Keys=True;Pooling=False");db.Open();
  void Sql(string sql){using var q=db.CreateCommand();q.CommandText=sql;q.ExecuteNonQuery();}
  Sql("CREATE TABLE users(id INTEGER PRIMARY KEY);INSERT INTO users VALUES(1);CREATE TABLE audit_events(id INTEGER PRIMARY KEY,actor_id INTEGER REFERENCES users(id),action TEXT,entity TEXT,entity_id INTEGER,occurred_at TEXT)");
  using(var stream=typeof(FolderConfigurationTests).Assembly.GetManifestResourceStream("SyntheticFolderConfiguration.sql")!)
  using(var reader=new StreamReader(stream))Sql(reader.ReadToEnd());
  bool permitted=true;
  var store=new FolderConfigurationStore(db,(_,_)=>permitted?1:throw new UnauthorizedAccessException(),dbPath);
  var op=Guid.NewGuid();var defaults=ProjectFolderTemplate.Defaults;
  var first=store.Record(op,null,external,defaults,"Synthetic standard",true);
  check(first.Entry.Template.SequenceEqual(defaults)&&store.Read().Count==1,"folder configuration persists exact CRC template and root");
  check(store.Record(op,null,external,defaults,"Synthetic standard",true).Replayed,"configuration exact retry returns original revision");
  void Invalid(Action a,string label){try{a();}catch(ArgumentException){check(true,label);return;}throw new Exception("FAIL: "+label);}
  Invalid(()=>store.Record(Guid.NewGuid(),null,external,defaults,"Stale",true),"stale configuration refused");
  Invalid(()=>store.Record(Guid.NewGuid(),first.Entry.Hash,external,defaults,"Unchanged",true),"unchanged configuration refused");
  var changed=defaults.Select(x=>x.Name=="Correspondence"?x with{IncludedByDefault=true}:x).ToArray();
  var second=store.Record(Guid.NewGuid(),first.Entry.Hash,external,changed,"Synthetic revised template",true);
  check(store.Read().Count==2&&second.Entry.Template.Last().IncludedByDefault,"configuration revisions preserve earlier template");
  permitted=false;
  try{store.Read();throw new Exception("FAIL: configuration access");}catch(UnauthorizedAccessException){check(true,"configuration authorization rechecked");}
  permitted=true;
  var selected=defaults.Where(x=>x.Name is "Calculations" or "QA-QC" or "Reference Documents").ToArray();
  var collision=Path.Combine(project,"Reference Documents");File.WriteAllText(collision,"Synthetic collision");
  Invalid(()=>FolderCreationRehearsal.Preview(second.Entry,"Synthetic Project",dbPath,selected),"folder preflight rejects file collision");
  check(!Directory.Exists(Path.Combine(project,"QA-QC"))&&File.ReadAllText(collision)=="Synthetic collision"&&File.ReadAllText(marker)=="Synthetic existing file","failed preflight leaves existing files and missing directories unchanged");
  File.Delete(collision);
  if(OperatingSystem.IsWindows())
  {
   var unqualified=Path.Combine(project,"QA-QC");Directory.CreateDirectory(unqualified);
   check(!WorkspaceConfidentiality.HasRestrictedDirectoryAcl(unqualified),"synthetic inherited directory is not explicitly qualified");
   Invalid(()=>FolderCreationRehearsal.Preview(second.Entry,"Synthetic Project",dbPath,selected),"folder preflight rejects unqualified existing directory");
   check(Directory.Exists(unqualified)&&!Directory.Exists(collision),"unqualified existing directory is neither removed nor repaired");
   Directory.Delete(unqualified);
  }
  var plan=FolderCreationRehearsal.Preview(second.Entry,"Synthetic Project",dbPath,selected);
  check(plan.Items.Count==3&&plan.Items.Count(x=>x.AlreadyExists)==1&&!Directory.Exists(Path.Combine(project,"QA-QC")),"creation preview distinguishes existing and missing folders without writing");
  Invalid(()=>FolderCreationRehearsal.Execute(second.Entry,"Synthetic Project",dbPath,selected,plan.Hash,false),"creation requires confirmation");
  WorkspaceConfidentiality.EnsureWorkspaceDirectory(Path.Combine(project,"QA-QC"));
  Invalid(()=>FolderCreationRehearsal.Execute(second.Entry,"Synthetic Project",dbPath,selected,plan.Hash,true),"changed filesystem invalidates creation plan");
  var updated=FolderCreationRehearsal.Preview(second.Entry,"Synthetic Project",dbPath,selected);
  var created=FolderCreationRehearsal.Execute(second.Entry,"Synthetic Project",dbPath,selected,updated.Hash,true);
  check(created.SequenceEqual(new[]{"Reference Documents"})&&File.ReadAllText(marker)=="Synthetic existing file","confirmed rehearsal creates only missing folder and preserves existing files");
  if(OperatingSystem.IsWindows())check(WorkspaceConfidentiality.HasRestrictedDirectoryAcl(Path.Combine(project,"Reference Documents")),"new rehearsal folder has restricted ACL");
  var repeat=FolderCreationRehearsal.Preview(second.Entry,"Synthetic Project",dbPath,selected);
  check(FolderCreationRehearsal.Execute(second.Entry,"Synthetic Project",dbPath,selected,repeat.Hash,true).Count==0,"reconciled repeated creation is no-op");
  Sql("DROP TRIGGER project_folder_config_no_update;UPDATE project_folder_configurations SET reason='Tampered' WHERE revision=1");
  try{store.Read();throw new Exception("FAIL: config tamper");}catch(InvalidOperationException){check(true,"configuration digest detects tamper");}
 }
}
