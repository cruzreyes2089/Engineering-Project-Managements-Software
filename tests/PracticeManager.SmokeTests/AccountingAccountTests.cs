using PracticeManager.Core;
using PracticeManager.Persistence;
using System.Text.Json;
internal static class AccountingAccountTests
{
 public static async Task Run(string path,string password,Action<bool,string> check)
 {
  var a=Guid.NewGuid();var b=Guid.NewGuid();var key=Guid.NewGuid().ToString("N");var name=Guid.NewGuid().ToString("N");var probe=new ManagerDesktopProbe();
  object Row(Guid id,string? code)=>new {key=id,name,code};
  string Page(object[] rows,int? count=null)=>JsonSerializer.Serialize(new {business=new {name="Synthetic Account Business"},totalRecords=count??rows.Length,chartOfAccounts=rows});
  var body=Page(new[]{Row(a,null),Row(b,"4000")});
  await AccountingProbeTests.WithServer(200,body,async(url,observed)=>{
   var r=await probe.ReadAccountsAsync(url,key);check(r.Success&&r.Accounts!=null,"chart of accounts parsed");
   check(r.Accounts!.Rows.Count==2&&r.Accounts.Rows[0].Key==a&&r.Accounts.Rows[1].Key==b,"distinct account UUIDs retained despite duplicate names");
   check(r.Accounts.Rows[0].Code==null&&r.Accounts.Rows[1].Code=="4000","null and text account codes supported");
   check(!r.ToString().Contains(name)&&!r.ToString().Contains(a.ToString()),"account diagnostics redact values");
   check((await observed).StartsWith("GET /api2/chart-of-accounts?fields=Key&fields=Code&fields=Name HTTP/1.1",StringComparison.Ordinal),"account reader fixed GET");
  });
  await AccountingProbeTests.WithServer(200,Page(Array.Empty<object>()),async(url,_)=>check((await probe.ReadAccountsAsync(url,key)).Accounts?.Rows.Count==0,"empty accounts supported"));
  foreach(var invalid in new[]{"{}",Page(new[]{Row(a,null)},2),Page(new[]{Row(a,null),Row(a,"1")}),Page(new[]{Row(Guid.Empty,null)}),Page(new object[]{new {key=a,name,code=123}}),Page(new object[]{new {key=a,name}}),Page(new object[]{new {key=a,name="" ,code=(string?)null}})})
   await AccountingProbeTests.WithServer(200,invalid,async(url,_)=>{var r=await probe.ReadAccountsAsync(url,key);check(!r.Success&&r.Accounts==null,"invalid account envelope or row rejected");});
  check((await probe.ReadAccountsAsync("http://example.invalid:12345/api2",key)).Outcome=="InvalidEndpoint","external account host rejected");
  var s=new LocalPractice(path);check(s.Login("admin",password),"account administrator login");
  await AccountingProbeTests.WithServer(200,body,async(url,_)=>{
   check((await s.SaveAccountingProfileAsync(url,key)).Success,"account fixture profile saved");
   var result=await s.ReadSavedAccountsAsync();check(result.Success&&result.ProfileRevision!=null&&result.Accounts?.Rows.Count==2,"account preview uses saved profile");
   s.RemoveAccountingProfile();
  },2);
  check((await s.ReadSavedAccountsAsync()).Outcome=="NotConfigured","removed profile blocks account preview");
  check(s.Login("employee",password),"account employee login");
  try{await s.ReadSavedAccountsAsync();throw new Exception("FAIL: account employee permission");}catch(UnauthorizedAccessException){check(true,"account employee denied");}
 }
}
