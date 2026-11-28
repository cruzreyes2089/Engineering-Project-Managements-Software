using Microsoft.Data.Sqlite;
using PracticeManager.Persistence;
using System.Text;
internal static class AccountingProfileTests
{
 public static async Task Run(LocalPractice s,string path,string password,Action<bool,string> check)
 {
  using(var db=new SqliteConnection($"Data Source={path}")){db.Open();using var q=db.CreateCommand();q.CommandText="UPDATE role_permissions SET allowed=1 WHERE role_id=1 AND permission_code='Accounting.Connection.Test'";q.ExecuteNonQuery();}
  check(s.ReadAccountingProfile()==null,"initial profile absent");
  var fixture=Guid.NewGuid().ToString("N");
  var observedRequests=new System.Collections.Concurrent.ConcurrentQueue<string>();
  await AccountingProbeTests.WithServer(200,"{\"items\":[]}",async(url,_)=>{
    var result=await s.SaveAccountingProfileAsync(url,fixture);
    check(result.Success,"validated profile saved");
    check(s.ReadAccountingProfile()?.Endpoint==url,"profile endpoint retained");
    check(!string.IsNullOrEmpty(s.ReadAccountingProfile()?.LastSuccessUtc),"successful test timestamp stored");
    using var db=new SqliteConnection($"Data Source={path}");db.Open();using var q=db.CreateCommand();q.CommandText="SELECT protected_value FROM accounting_profiles WHERE user_id=1";var bytes=(byte[])q.ExecuteScalar()!;
    check(bytes.Length>0&&!Encoding.UTF8.GetString(bytes).Contains(fixture),"profile payload encrypted at rest");
    var restarted=new LocalPractice(path);
    check(restarted.Login("admin",password),"saved connection restart login");
    check((await restarted.TestSavedAccountingProfileAsync()).Success,"saved connection authenticates after restart");
    check(observedRequests.Count==2,"saved connection makes exactly two read requests");
    check(observedRequests.All(r=>r.StartsWith("GET /api2/customers HTTP/1.1",StringComparison.Ordinal)),"both saved profile requests read only");
    check(observedRequests.All(r=>r.Split('\n').Any(line=>line.Trim().Equals("X-API-KEY: "+fixture,StringComparison.OrdinalIgnoreCase))),"saved connection reuses synthetic fixture");
  },2,observedRequests.Enqueue);
  var expected=s.ReadAccountingProfile();
  var reopened=new LocalPractice(path);
  check(reopened.Login("admin",password),"profile restart login");
  check(reopened.ReadAccountingProfile()==expected,"profile persists after restart");
  await AccountingProbeTests.WithServer(401,"{}",async(url,_)=>check(!(await reopened.SaveAccountingProfileAsync(url,fixture)).Success,"failed replacement rejected"));
  check(reopened.ReadAccountingProfile()==expected,"failed replacement preserves previous profile");
  check((await reopened.SaveAccountingProfileAsync("http://example.invalid:12345/api2",fixture)).Outcome=="InvalidEndpoint","external profile rejected");
  check(!(await reopened.TestSavedAccountingProfileAsync()).Success,"closed provider fails saved profile test");
  check(reopened.ReadAccountingProfile()==expected,"failed test preserves success timestamp");
  using(var db=new SqliteConnection($"Data Source={path}")){db.Open();using var q=db.CreateCommand();q.CommandText="UPDATE accounting_profiles SET endpoint=$e WHERE user_id=1";q.Parameters.AddWithValue("$e",expected!.Endpoint.EndsWith("/")?expected.Endpoint.TrimEnd('/') : expected.Endpoint+"/");q.ExecuteNonQuery();}
  check((await reopened.TestSavedAccountingProfileAsync()).Outcome=="CredentialUnavailable","modified endpoint invalidates protected profile");
  using(var db=new SqliteConnection($"Data Source={path}")){db.Open();using var q=db.CreateCommand();q.CommandText="UPDATE accounting_profiles SET endpoint=$e WHERE user_id=1";q.Parameters.AddWithValue("$e",expected!.Endpoint);q.ExecuteNonQuery();q.CommandText="UPDATE role_permissions SET allowed=0 WHERE role_id=1 AND permission_code='Accounting.Connection.Manage'";q.ExecuteNonQuery();}
  check(!reopened.CanManageAccountingConnection()&&reopened.CanTestAccountingConnection(),"independent profile management permission");
  try{reopened.RemoveAccountingProfile();throw new Exception("FAIL: removal without permission");}catch(UnauthorizedAccessException){check(true,"revoked manage grant blocks profile removal");}
  check(reopened.ReadAccountingProfile()==expected,"denied removal preserves profile");
  using(var db=new SqliteConnection($"Data Source={path}")){db.Open();using var q=db.CreateCommand();q.CommandText="UPDATE role_permissions SET allowed=1 WHERE role_id=1 AND permission_code='Accounting.Connection.Manage'";q.ExecuteNonQuery();}

  check(reopened.Login("employee",password),"employee sign in");
  try{reopened.ReadAccountingProfile();throw new Exception("FAIL: unauthorized profile read");}catch(UnauthorizedAccessException){check(true,"employee profile read denied");}
  check(reopened.Login("admin",password),"administrator sign in");
  reopened.RemoveAccountingProfile();
  check(reopened.ReadAccountingProfile()==null,"saved profile removed");
  check((await reopened.TestSavedAccountingProfileAsync()).Outcome=="NotConfigured","removed profile cannot be used");
  using(var db=new SqliteConnection($"Data Source={path}")){db.Open();using var q=db.CreateCommand();q.CommandText="SELECT count(*) FROM audit_events WHERE entity='AccountingProfile' AND action IN ('Save','Remove')";check(Convert.ToInt32(q.ExecuteScalar())==2,"profile changes audited");}
 }
}
