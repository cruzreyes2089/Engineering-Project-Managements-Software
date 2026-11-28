using System.Text.Json;
using Microsoft.Data.Sqlite;
using PracticeManager.Core;
using PracticeManager.Persistence;
internal static class AccountingDirectoryTests
{
 public static async Task Run(string path,string pw,Action<bool,string> check)
 {
  var s=new LocalPractice(path);s.Bootstrap("Synthetic Directory Firm","admin",pw,"CL");check(s.Login("admin",pw)&&s.CanSyncDirectory(),"directory administrator grant");
  var key=Guid.NewGuid().ToString("N");var marker=Guid.NewGuid();var one=Guid.NewGuid();var two=Guid.NewGuid();var inc=Guid.NewGuid();var exp=Guid.NewGuid();
  var customers=new[]{new AccountingCustomer(one,"Synthetic A","Paid"),new AccountingCustomer(two,"Synthetic B","Paid")};
  string Marker()=>JsonSerializer.Serialize(new{business=new{name="Synthetic"},skip=0,pageSize=2,totalRecords=1,textCustomFields=new[]{new{key=marker,name=BusinessMarker.TestName}}});
  string Page(string request){
   var offset=request.Contains("skip=1&")?1:0;
   return JsonSerializer.Serialize(new{business=new{name="Synthetic"},skip=offset,pageSize=1,totalRecords=customers.Length,customers=customers.Skip(offset).Take(1).Select(x=>new{key=x.Key,name=x.Name,status=x.Status})});
  }
  string Accounts()=>JsonSerializer.Serialize(new{business=new{name="Synthetic"},totalRecords=2,chartOfAccounts=new[]{new{key=inc,name="Synthetic Income",code="4000"},new{key=exp,name="Synthetic Expense",code="6000"}}});
  bool unstable=false;int pageReads=0;
  string Reply(string req){
   if(req.StartsWith("GET /api2/text-custom-fields?"))return Marker();
   if(req.StartsWith("GET /api2/chart-of-accounts?"))return Accounts();
   if(req.StartsWith("GET /api2/customers?")){
    pageReads++;
    if(unstable&&pageReads%2==0)return JsonSerializer.Serialize(new{business=new{name="Synthetic"},skip=0,pageSize=1,totalRecords=1,customers=new[]{new{key=one,name="Unexpected",status="Paid"}}});
    return Page(req);
   }
   return "{\"customers\":[]}";
  }
  await AccountingProbeTests.WithServer(200,"{}",async(url,_)=>{
   check((await s.SaveAccountingProfileAsync(url,key)).Success,"directory connection saved");
   check((await s.SyncCustomerDirectoryAsync()).Outcome=="MarkerNotEnrolled","directory requires marker");
   check((await s.EnrollOrVerifyMarkerAsync(true)).Success,"directory marker enrolled");
   check(s.ReadCachedDirectory().Outcome=="NotSynchronized","no initial directory cache");
   var r=await s.SyncCustomerDirectoryAsync();
   check(r.Outcome=="Synchronized"&&r.Report?.Customers.Count==2&&r.Report.Customers.All(x=>x.Present),"two-page directory synchronized");
   var rev=r.Report!.Revision;
   var reopened=new LocalPractice(path);check(reopened.Login("admin",pw)&&reopened.ReadCachedDirectory().Report?.Customers.Count==2,"directory cache survives restart");
   var good=new AccountingAccount(inc,"Synthetic Income","4000");
   check((await s.SaveProvisionalAccountRoleAsync(rev,"ServiceIncome",good)).Outcome=="RoleSaved","explicit role saved");
   check((await s.SaveProvisionalAccountRoleAsync(rev,"OfficeExpense",good)).Outcome=="RoleConflict","same account cannot fill two roles");
   check((await s.SaveProvisionalAccountRoleAsync(rev,"OfficeExpense",new AccountingAccount(exp,"Changed","6000"))).Outcome=="AccountChanged","changed account refused");
   check((await s.SaveProvisionalAccountRoleAsync(rev,"OfficeExpense",new AccountingAccount(exp,"Synthetic Expense","6000"))).Report?.Roles.Count==2,"two distinct account roles saved");
   check(s.RemoveProvisionalAccountRole(rev,"OfficeExpense").Report?.Roles.Count==1,"local role removed");
   customers=new[]{new AccountingCustomer(one,"Renamed Synthetic","Paid")};
   var next=await s.SyncCustomerDirectoryAsync();
   check(next.Report?.Customers.Count==2&&next.Report.Customers.Count(x=>!x.Present)==1&&next.Report.Customers.Single(x=>x.Present).Name=="Renamed Synthetic","rename refreshed and missing customer retained");
   unstable=true;pageReads=0;
   check((await s.SyncCustomerDirectoryAsync()).Outcome=="DirectoryChanged","unstable scan rejected");
   check(s.ReadCachedDirectory().Report?.Customers.Single(x=>x.Present).Name=="Renamed Synthetic","failed scan preserves previous cache");
   check(s.SearchClients("").Count==0,"sync never creates CRC clients");
   s.RemoveAccountingProfile();check(s.ReadCachedDirectory().Outcome=="NotConfigured","removed profile hides cache");
  },23,responseFor:Reply);
  using(var db=new SqliteConnection($"Data Source={path}")){db.Open();using var q=db.CreateCommand();q.CommandText="UPDATE role_permissions SET allowed=0 WHERE permission_code='Accounting.Directory.Sync'";q.ExecuteNonQuery();}
  check(!s.CanSyncDirectory(),"revoked directory permission denied");
  try{s.ReadCachedDirectory();throw new Exception("FAIL: revoked directory");}catch(UnauthorizedAccessException){check(true,"revoked directory read blocked");}
 }
}
