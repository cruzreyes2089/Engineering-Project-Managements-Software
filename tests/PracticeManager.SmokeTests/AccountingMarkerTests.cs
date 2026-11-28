using System.Text.Json;
using Microsoft.Data.Sqlite;
using PracticeManager.Core;
using PracticeManager.Persistence;
internal static class AccountingMarkerTests
{
 public static async Task Run(string path,string password,Action<bool,string> check)
 {
  var s=new LocalPractice(path);s.Bootstrap("Synthetic Marker Firm","admin",password,"CL");check(s.Login("admin",password),"marker admin login");
  check(s.CanManageBusinessMarker()&&!s.ReadBusinessMarker().Enrolled,"new marker permission and empty identity");
  var token=Guid.NewGuid().ToString("N");var id=Guid.NewGuid();var other=Guid.NewGuid();var name=BusinessMarker.TestName;
  string Envelope(params object[] rows)=>JsonSerializer.Serialize(new{business=new{name="Synthetic"},skip=0,pageSize=2,totalRecords=rows.Length,textCustomFields=rows});
  string Marker(Guid k)=>Envelope(new{key=k,name,placement="Customers"});
  string Reply(string req,Guid k)=>req.StartsWith("GET /api2/text-custom-fields?",StringComparison.Ordinal)?Marker(k):"{\"customers\":[]}";
  var requests=new List<string>();
  await AccountingProbeTests.WithServer(200,"{}",async(url,_)=>{
   check((await s.SaveAccountingProfileAsync(url,token)).Success,"marker setup profile saved");
   check((await s.EnrollOrVerifyMarkerAsync(false)).Outcome=="MarkerNotEnrolled","implicit enrollment denied");
   check((await s.EnrollOrVerifyMarkerAsync(true)).Success,"existing definition enrolled");
   check(s.ReadBusinessMarker().Enrolled,"marker persisted");
   var reopened=new LocalPractice(path);check(reopened.Login("admin",password),"marker restart login");
   check(reopened.ReadBusinessMarker().Enrolled&&(await reopened.EnrollOrVerifyMarkerAsync(false)).Success,"marker verified after restart");
   check((await reopened.TestSavedAccountingProfileAsync()).Success,"guarded saved read");
   check(requests.Count==5&&requests.Count(x=>x.StartsWith("GET /api2/text-custom-fields?term="+Uri.EscapeDataString(name)+"&skip=0&pageSize=2 HTTP/1.1",StringComparison.Ordinal))==3,"marker route bounded and read-only");
  },5,requests.Add,req=>Reply(req,id));
  var old=s.ReadAccountingProfile();
  await AccountingProbeTests.WithServer(200,"{}",async(url,_)=>{
   check((await s.SaveAccountingProfileAsync(url,token)).Outcome=="MarkerMismatch","different marker blocks replacement");
   check(s.ReadAccountingProfile()==old,"wrong marker preserves profile");
  },2,responseFor:req=>Reply(req,other));
  await AccountingProbeTests.WithServer(200,"{}",async(url,_)=>{
   check((await s.SaveAccountingProfileAsync(url,token)).Success,"same marker accepts replacement");
   check((await s.EnrollOrVerifyMarkerAsync(false)).Success,"marker reverified after replacement");
  },3,responseFor:req=>Reply(req,id));
  var probe=new ManagerDesktopProbe();
  await AccountingProbeTests.WithServer(200,Envelope(),async(url,_)=>check((await probe.FindMarkerAsync(url,token,name)).Outcome=="MarkerMissing","missing marker rejected"));
  await AccountingProbeTests.WithServer(200,Envelope(new{key=id,name},new{key=other,name}),async(url,_)=>check((await probe.FindMarkerAsync(url,token,name)).Outcome=="MarkerAmbiguous","duplicate marker rejected"));
  await AccountingProbeTests.WithServer(200,Envelope(new{key="bad",name}),async(url,_)=>check((await probe.FindMarkerAsync(url,token,name)).Outcome=="InvalidMarkerResponse","malformed key rejected"));
  await AccountingProbeTests.WithServer(200,Envelope(new{key=id,name=name+" different"}),async(url,_)=>check((await probe.FindMarkerAsync(url,token,name)).Outcome=="MarkerMissing","partial marker name rejected"));
  check((await probe.FindMarkerAsync("http://127.0.0.1:1/api2",token,"wrong")).Outcome=="InvalidMarker","invalid marker request rejected");
  var nested=JsonSerializer.Serialize(new{salesInvoices=new[]{new{invoiceAmount=new{value=100.00m,currency="USD"},balanceDue=new{value=60.00m},unrelated=new{privateField="private"}}}});
  await AccountingProbeTests.WithServer(200,nested,async(url,_)=>{
   var r=await probe.InspectResourceAsync(url,token,AccountingDiscoveryResource.SalesInvoices);
   check(r.SchemaSummary?.Contains("invoiceAmount.value: Number")==true&&r.SchemaSummary.Contains("balanceDue.value: Number"),"money field schema expanded");
   check(r.SchemaSummary?.Contains("USD")==false&&!r.SchemaSummary.Contains("privateField")&&!r.SchemaSummary.Contains("100.00"),"nested schema values and unrelated fields omitted");
  });
  s.RemoveAccountingProfile();check(s.ReadBusinessMarker().Enrolled,"removal retains identity marker");
  check((await s.EnrollOrVerifyMarkerAsync(false)).Outcome=="NotConfigured","removed profile cannot verify");
  using(var db=new SqliteConnection($"Data Source={path}")){db.Open();using var q=db.CreateCommand();q.CommandText="UPDATE role_permissions SET allowed=0 WHERE permission_code='Accounting.Identity.Manage'";q.ExecuteNonQuery();}
  check(!s.CanManageBusinessMarker(),"revoked marker permission denied");
  try{s.ReadBusinessMarker();throw new Exception("FAIL: revoked marker read");}catch(UnauthorizedAccessException){check(true,"revoked marker read blocked");}
 }
}
