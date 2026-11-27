using PracticeManager.Core;
using Microsoft.Data.Sqlite;
using PracticeManager.Persistence;
internal static class ProjectFolderRegisterStoreTests
{
 public static void Run(string root,Action<bool,string> check)
 {
  var basePath=Path.Combine(root,"folder-register-fixture");Directory.CreateDirectory(basePath);
  var external=Path.Combine(basePath,"projects");var database=Path.Combine(basePath,"db");Directory.CreateDirectory(external);Directory.CreateDirectory(database);
  Directory.CreateDirectory(Path.Combine(external,"A","Drawings"));Directory.CreateDirectory(Path.Combine(external,"B"));
  using var db=new SqliteConnection("Data Source=:memory:;Foreign Keys=True;Pooling=False");db.Open();
  void Sql(string sql){using var q=db.CreateCommand();q.CommandText=sql;q.ExecuteNonQuery();}
  Sql(@"CREATE TABLE projects(id INTEGER PRIMARY KEY);CREATE TABLE users(id INTEGER PRIMARY KEY);
  CREATE TABLE audit_events(id INTEGER PRIMARY KEY,actor_id INTEGER REFERENCES users(id),action TEXT,entity TEXT,entity_id INTEGER,occurred_at TEXT);
  CREATE TABLE project_documents(id INTEGER PRIMARY KEY,project_id INTEGER REFERENCES projects(id),supersedes_id INTEGER REFERENCES project_documents(id),classification TEXT,category TEXT,file_name TEXT,file_hash TEXT);
  INSERT INTO projects VALUES(1),(2);INSERT INTO users VALUES(1),(2)");
  foreach(var resource in new[]{"SyntheticFolderConfiguration.sql","SyntheticDocumentRegister.sql"})
  {using var stream=typeof(ProjectFolderRegisterStoreTests).Assembly.GetManifestResourceStream(resource)!;using var reader=new StreamReader(stream);Sql(reader.ReadToEnd());}
  var configs=new FolderConfigurationStore(db,(_,_)=>1,Path.Combine(database,"synthetic.db"));
  var config=configs.Record(Guid.NewGuid(),null,external,ProjectFolderTemplate.Defaults,"Synthetic root",true).Entry;
  bool allowed=true;
  var store=new ProjectFolderRegisterStore(db,(_,p,w)=>allowed&&p is 1 or 2?1:throw new UnauthorizedAccessException(),Path.Combine(database,"synthetic.db"));
  var op=Guid.NewGuid();
  var first=store.Record(1,op,null,config.Hash,"Registered","A","Synthetic review",true);
  check(!first.Replayed&&store.Read(1).Single()==first.Entry,"native folder candidate records exact metadata and audit");
  check(store.Record(1,op,null,config.Hash,"Registered","A","Synthetic review",true).Replayed,"identical folder operation replay");
  void Invalid(Action a,string label){try{a();}catch(ArgumentException){check(true,label);return;}throw new Exception("FAIL: "+label);}
  Invalid(()=>store.Record(1,op,null,config.Hash,"Registered","B","Synthetic review",true),"changed folder replay refused");
  Invalid(()=>store.Record(1,Guid.NewGuid(),null,config.Hash,"Registered","B","Synthetic stale",true),"stale folder revision refused");
  try{store.Record(2,Guid.NewGuid(),null,config.Hash,"Registered","A/Drawings","Synthetic overlap",true);throw new Exception("FAIL: overlapping folder");}
  catch(SqliteException e) when(e.Message.Contains("Overlapping current project folder",StringComparison.Ordinal)){check(store.Read(2).Count==0,"native overlapping folder rejection rolls back audit");}
  var retired=store.Record(1,Guid.NewGuid(),first.Entry.Hash,config.Hash,"Retired",first.Entry.PathKey,"Synthetic retirement",true);
  var next=store.Record(2,Guid.NewGuid(),null,config.Hash,"Registered","A/Drawings","Synthetic reassignment",true);
  check(retired.Entry.State=="Retired"&&next.Entry.State=="Registered"&&store.Read(1).Count==2,"retirement preserves history and permits subsequent assignment");
  check(first.Entry.ConfigurationHash==config.Hash&&first.Entry.DisplayPath==Path.Combine(external,"A"),"folder record binds original spelling and configuration revision");
  var revised=configs.Record(Guid.NewGuid(),config.Hash,external,ProjectFolderTemplate.Defaults.Append(new ProjectFolderTemplateItem("Custom",true)).ToArray(),"Synthetic revision",true).Entry;
  Invalid(()=>store.Record(1,Guid.NewGuid(),retired.Entry.Hash,config.Hash,"Registered","B","Stale root",true),"stale configuration cannot register folder");
  check(store.Record(1,Guid.NewGuid(),retired.Entry.Hash,revised.Hash,"Registered","B","New configuration",true).Entry.ConfigurationHash==revised.Hash,"current configuration binds new assignment");
  allowed=false;
  try{store.Read(1);throw new Exception("FAIL: unauthorized folder read");}catch(UnauthorizedAccessException){check(true,"folder store rechecks caller permission");}
  try{store.Record(1,op,null,config.Hash,"Registered","A","Synthetic review",true);throw new Exception("FAIL: unauthorized replay");}catch(UnauthorizedAccessException){check(true,"folder replay requires current authorization");}
  allowed=true;
  Sql("DROP TRIGGER project_folder_revisions_no_update;UPDATE project_folder_revisions SET reason='Tampered' WHERE project_id=1 AND revision=1");
  try{store.Read(1);throw new Exception("FAIL: folder tamper");}catch(InvalidOperationException){check(true,"folder store detects tampered history");}
  check(Directory.Exists(Path.Combine(external,"A","Drawings"))&&!Directory.Exists(Path.Combine(external,"QA-QC")),"folder register does not change filesystem");
 }
}
