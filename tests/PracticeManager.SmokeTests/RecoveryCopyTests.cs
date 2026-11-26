using Microsoft.Data.Sqlite;
using PracticeManager.Core;
using PracticeManager.Persistence;
using System.Security.Cryptography;
static class RecoveryCopyTests
{
 public static void Run(string path,string password,Action<bool,string> check)
 {
  var s=new LocalPractice(path);
  var target=Path.Combine(Path.GetDirectoryName(path)!,"recovery-copy.db");
  s.Bootstrap("Synthetic recovery firm","admin",password,"CL");
  try{s.CreateRecoveryCopy(target);throw new Exception("Unauthenticated copy allowed");}catch(UnauthorizedAccessException){check(!File.Exists(target),"unauthenticated recovery copy denied");}
  check(s.Login("admin",password)&&s.Can("Administration.RecoveryCopy"),"administrator recovery grant");
  var client=s.SaveClient(new Client(0,"","Business / Company","Synthetic recovery client","","","","","","","","","","","Active"));
  using(var db=new SqliteConnection($"Data Source={path};Pooling=False")){
   db.Open();using var q=db.CreateCommand();
   q.CommandText="INSERT INTO accounting_profiles(user_id,endpoint,protected_value,last_success_utc,revision) SELECT id,'http://127.0.0.1:1/api2',X'010203','2026-11-26T00:00:00Z','synthetic' FROM users WHERE username='admin'";
   q.ExecuteNonQuery();
  }
  var result=s.CreateRecoveryCopy(target);
  check(result.Bytes>0&&result.Sha256==Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(target))).ToLowerInvariant(),"recovery copy file hash verified");
  using(var db=new SqliteConnection($"Data Source={target};Mode=ReadOnly;Pooling=False")){
   db.Open();using var q=db.CreateCommand();
   q.CommandText="SELECT count(*) FROM clients WHERE id="+client;check(Convert.ToInt64(q.ExecuteScalar())==1,"recovery copy retains committed client");
   q.CommandText="SELECT count(*) FROM accounting_profiles";check(Convert.ToInt64(q.ExecuteScalar())==0,"recovery copy removes accounting profiles");
   q.CommandText="SELECT reason FROM accounting_lifecycle_hold WHERE id=1";check((string?)q.ExecuteScalar()=="Restore","recovery copy has sticky accounting lifecycle hold");
   q.CommandText="SELECT count(*) FROM audit_events WHERE action='RecoveryCopyIsolated'";check(Convert.ToInt64(q.ExecuteScalar())==1,"recovery copy isolation audited");
  }
  using(var db=new SqliteConnection($"Data Source={path};Pooling=False")){
   db.Open();using var q=db.CreateCommand();
   q.CommandText="SELECT count(*) FROM accounting_profiles";check(Convert.ToInt64(q.ExecuteScalar())==1,"recovery copy does not remove live profiles");
   q.CommandText="SELECT count(*) FROM accounting_lifecycle_hold";check(Convert.ToInt64(q.ExecuteScalar())==0,"recovery copy does not hold source");
  }
  try{s.CreateRecoveryCopy(target);throw new Exception("Overwrite allowed");}catch(ArgumentException){check(result.Sha256==Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(target))).ToLowerInvariant(),"recovery copy never overwrites");}
  using(var db=new SqliteConnection($"Data Source={path};Pooling=False")){
   db.Open();using var q=db.CreateCommand();q.CommandText="UPDATE role_permissions SET allowed=0 WHERE permission_code='Administration.RecoveryCopy'";q.ExecuteNonQuery();
  }
  var other=Path.Combine(Path.GetDirectoryName(path)!,"denied-copy.db");
  try{s.CreateRecoveryCopy(other);throw new Exception("Revoked grant allowed");}catch(UnauthorizedAccessException){check(!File.Exists(other),"revoked recovery grant denied");}
 }
}
