using Microsoft.Data.Sqlite;
namespace PracticeManager.Persistence;

// Internal candidate only; the ordinary LocalPractice constructor is unchanged.
// A future activation must qualify prior-source, startup, recovery and desktop use.
internal static class EffortBudgetMigrationCandidate
{
 const string Codes="('Projects.Budget.View','Projects.Budget.Record')";
 static readonly string[] Grants=["Projects.Budget.View","Projects.Budget.Record"];
 static string Ddl()
 {
  using var stream=typeof(EffortBudgetMigrationCandidate).Assembly.GetManifestResourceStream("PracticeManager.EffortBudgetCandidate.sql")??throw new InvalidOperationException("Budget migration resource missing.");
  using var reader=new StreamReader(stream);return reader.ReadToEnd();
 }
 static SqliteCommand Q(SqliteConnection db,SqliteTransaction t,string sql)
 {var q=db.CreateCommand();q.Transaction=t;q.CommandText=sql;return q;}
 static long Count(SqliteConnection db,SqliteTransaction t,string sql)
 {using var q=Q(db,t,sql);return Convert.ToInt64(q.ExecuteScalar());}
 static void Exec(SqliteConnection db,SqliteTransaction t,string sql)
 {using var q=Q(db,t,sql);q.ExecuteNonQuery();}
 static Dictionary<string,(string Table,string Sql)> Objects(SqliteConnection db,SqliteTransaction t)
 {
  using var q=Q(db,t,"SELECT type,name,tbl_name,coalesce(sql,'') FROM sqlite_schema");using var r=q.ExecuteReader();
  var objects=new Dictionary<string,(string,string)>(StringComparer.Ordinal);
  while(r.Read())objects.Add(r.GetString(0)+":"+r.GetString(1),(r.GetString(2),r.GetString(3)));
  return objects;
 }
 static Dictionary<string,(string Table,string Sql)> ExpectedAdditions()
 {
  using var db=new SqliteConnection("Data Source=:memory:;Foreign Keys=True;Pooling=False");db.Open();
  using var t=db.BeginTransaction(deferred:false);
  Exec(db,t,"CREATE TABLE projects(id INTEGER PRIMARY KEY);CREATE TABLE users(id INTEGER PRIMARY KEY);CREATE TABLE audit_events(id INTEGER PRIMARY KEY,actor_id INTEGER,action TEXT,entity TEXT,entity_id INTEGER)");
  var before=Objects(db,t);Exec(db,t,Ddl());var after=Objects(db,t);
  return after.Where(x=>!before.ContainsKey(x.Key)).ToDictionary(x=>x.Key,x=>x.Value,StringComparer.Ordinal);
 }
 internal static string Apply(SqliteConnection db,SqliteTransaction t)
 {
  if(Count(db,t,"PRAGMA user_version")!=47||Count(db,t,"PRAGMA foreign_keys")!=1)
   throw new ArgumentException("Closed initialized schema-47 fixture and foreign keys required.");
  if(Count(db,t,"SELECT count(*) FROM firms WHERE id=1")!=1||
     Count(db,t,"SELECT count(*) FROM roles WHERE name='Administrator'")!=1)
   throw new ArgumentException("Initialized single firm and administrator role required.");
  var expected=ExpectedAdditions();var old=Objects(db,t);
  if(old.Keys.Any(expected.ContainsKey)||
     Count(db,t,"SELECT count(*) FROM permissions WHERE code IN "+Codes)!=0||
     Count(db,t,"SELECT count(*) FROM role_permissions WHERE permission_code IN "+Codes)!=0||
     Count(db,t,"SELECT count(*) FROM audit_events WHERE action IN ('RecordEffortBudgetRevision','RecordEffortForecast')")!=0)
   throw new ArgumentException("Unexpected pre-existing budget evidence.");
  var before=DatabasePreservationWitness.Capture(db,t,47,false);
  Exec(db,t,Ddl());
  foreach(var grant in Grants)
  {
   using var q=Q(db,t,"INSERT INTO permissions(code) VALUES($g);INSERT INTO role_permissions(role_id,permission_code,allowed) SELECT id,$g,1 FROM roles WHERE name='Administrator'");
   q.Parameters.AddWithValue("$g",grant);q.ExecuteNonQuery();
  }
  var after=Objects(db,t);
  var additions=after.Where(x=>!old.ContainsKey(x.Key)).ToDictionary(x=>x.Key,x=>x.Value,StringComparer.Ordinal);
  if(additions.Count!=expected.Count||expected.Any(x=>!additions.TryGetValue(x.Key,out var value)||value!=x.Value)||
     Count(db,t,"SELECT count(*) FROM permissions WHERE code IN "+Codes)!=2||
     Count(db,t,"SELECT count(*) FROM role_permissions WHERE permission_code IN "+Codes)!=2||
     Count(db,t,"SELECT count(*) FROM role_permissions rp JOIN roles r ON r.id=rp.role_id WHERE r.name='Administrator' AND rp.allowed=1 AND rp.permission_code IN "+Codes)!=2)
   throw new InvalidOperationException("Budget migration additions mismatch.");
  Exec(db,t,"PRAGMA user_version=48");
  var verified=EffortBudgetStore.VerifyDatabase(db,t);
  if(verified.Projects!=0||verified.Revisions!=0||verified.Forecasts!=0)throw new InvalidOperationException("New budget history must be empty.");
  var witness=DatabasePreservationWitness.Capture(db,t,48,false,expected.Keys.ToHashSet(StringComparer.Ordinal),Codes);
  if(witness!=before)throw new InvalidOperationException("Budget migration changed pre-existing database evidence.");
  using(var q=Q(db,t,"PRAGMA foreign_key_check"))using(var r=q.ExecuteReader())if(r.Read())throw new InvalidOperationException("Budget migration foreign-key integrity failed.");
  return witness;
 }
}
