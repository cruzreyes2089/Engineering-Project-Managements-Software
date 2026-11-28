using Microsoft.Data.Sqlite;
namespace PracticeManager.Persistence;

// Isolated additive candidate. No normal constructor or bootstrap integration.
// Existing permissions remain unchanged; services require current administrator
// and relevant document permissions independently of this schema.
internal static class DocumentRegisterMigrationCandidate
{
 static readonly string[] Resources=["PracticeManager.FolderConfigurationCandidate.sql","PracticeManager.DocumentRegisterCandidate.sql"];
 const string Actions="('RecordFolderConfiguration','RecordProjectFolderReference','RecordTransmittalEvent')";
 static SqliteCommand Q(SqliteConnection db,SqliteTransaction t,string sql)
 {var q=db.CreateCommand();q.Transaction=t;q.CommandText=sql;return q;}
 static long Count(SqliteConnection db,SqliteTransaction t,string sql)
 {using var q=Q(db,t,sql);return Convert.ToInt64(q.ExecuteScalar());}
 static void Exec(SqliteConnection db,SqliteTransaction t,string sql)
 {using var q=Q(db,t,sql);q.ExecuteNonQuery();}
 static void Additions(SqliteConnection db,SqliteTransaction t)
 {
  foreach(var name in Resources)
  {
   using var stream=typeof(DocumentRegisterMigrationCandidate).Assembly.GetManifestResourceStream(name)??throw new InvalidOperationException("Document migration resource missing.");
   using var reader=new StreamReader(stream);Exec(db,t,reader.ReadToEnd());
  }
 }
 static Dictionary<string,(string Table,string Sql)> Objects(SqliteConnection db,SqliteTransaction t)
 {
  using var q=Q(db,t,"SELECT type,name,tbl_name,coalesce(sql,'') FROM sqlite_schema");using var r=q.ExecuteReader();
  var result=new Dictionary<string,(string,string)>(StringComparer.Ordinal);
  while(r.Read())result.Add(r.GetString(0)+":"+r.GetString(1),(r.GetString(2),r.GetString(3)));return result;
 }
 static Dictionary<string,(string Table,string Sql)> Expected()
 {
  using var db=new SqliteConnection("Data Source=:memory:;Foreign Keys=True;Pooling=False");db.Open();
  using var t=db.BeginTransaction(deferred:false);
  Exec(db,t,@"CREATE TABLE projects(id INTEGER PRIMARY KEY);CREATE TABLE users(id INTEGER PRIMARY KEY);
   CREATE TABLE audit_events(id INTEGER PRIMARY KEY,actor_id INTEGER,action TEXT,entity TEXT,entity_id INTEGER,occurred_at TEXT);
   CREATE TABLE project_documents(id INTEGER PRIMARY KEY,project_id INTEGER,supersedes_id INTEGER,classification TEXT,category TEXT,file_name TEXT,file_hash TEXT)");
  var before=Objects(db,t);Additions(db,t);
  return Objects(db,t).Where(x=>!before.ContainsKey(x.Key)).ToDictionary(x=>x.Key,x=>x.Value,StringComparer.Ordinal);
 }
 internal static void VerifyLayout(SqliteConnection db,SqliteTransaction t)
 {
  if(Count(db,t,"PRAGMA user_version")!=49)throw new ArgumentException("Document layout requires candidate schema 49.");
  var expected=Expected();var actual=Objects(db,t);
  var tables=expected.Values.Select(x=>x.Table).Where(x=>x!="audit_events").ToHashSet(StringComparer.Ordinal);
  if(expected.Any(x=>!actual.TryGetValue(x.Key,out var value)||value!=x.Value)||
     actual.Any(x=>tables.Contains(x.Value.Table)&&!expected.ContainsKey(x.Key)))
   throw new InvalidOperationException("Document register schema layout mismatch.");
 }
 internal static void VerifyPreservation(SqliteConnection db,SqliteTransaction t,string prior)
 {
  VerifyLayout(db,t);
  if(DatabasePreservationWitness.Capture(db,t,49,false,Expected().Keys.ToHashSet(StringComparer.Ordinal))!=prior)
   throw new InvalidOperationException("Document migration changed existing schema or row evidence.");
 }
 internal static string Apply(SqliteConnection db,SqliteTransaction t)
 {
  if(Count(db,t,"PRAGMA user_version")!=48||Count(db,t,"PRAGMA foreign_keys")!=1)
   throw new ArgumentException("Initialized schema-48 candidate and foreign keys required.");
  if(Count(db,t,"SELECT count(*) FROM firms WHERE id=1")!=1)throw new ArgumentException("Initialized firm required.");
  EffortBudgetMigrationCandidate.VerifyLayout(db,t);var budget=EffortBudgetStore.VerifyDatabase(db,t);
  var expected=Expected();var old=Objects(db,t);
  if(old.Keys.Any(expected.ContainsKey)||Count(db,t,"SELECT count(*) FROM audit_events WHERE action IN "+Actions)!=0)
   throw new ArgumentException("Unexpected pre-existing document register evidence.");
  var before=DatabasePreservationWitness.Capture(db,t,48,false);
  Additions(db,t);
  var additions=Objects(db,t).Where(x=>!old.ContainsKey(x.Key)).ToDictionary(x=>x.Key,x=>x.Value,StringComparer.Ordinal);
  if(additions.Count!=expected.Count||expected.Any(x=>!additions.TryGetValue(x.Key,out var value)||value!=x.Value))
   throw new InvalidOperationException("Unexpected document register additions.");
  Exec(db,t,"PRAGMA user_version=49");
  VerifyPreservation(db,t,before);
  EffortBudgetMigrationCandidate.VerifyLayout(db,t);
  if(EffortBudgetStore.VerifyDatabase(db,t)!=budget)throw new InvalidOperationException("Budget history changed during document migration.");
  var witness=DocumentRegisterVerification.Verify(db,t);
  if(witness.Configurations!=0||witness.FolderRevisions!=0||witness.Transmittals!=0||witness.Documents!=0||witness.Events!=0)
   throw new InvalidOperationException("New document history must be empty.");
  return before;
 }
}
