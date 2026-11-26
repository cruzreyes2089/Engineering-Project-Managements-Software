using System.Text.Json;
using Microsoft.Data.Sqlite;
using PracticeManager.Core;
using PracticeManager.Persistence;
internal static class AccountingLinkTests
{
 public static async Task Run(string path,string password,Action<bool,string> check)
 {
  var s=new LocalPractice(path);check(s.Login("admin",password),"mapping administrator login");
  check(s.CanManageCustomerLinks(),"dedicated customer mapping permission");
  var a=Guid.NewGuid();var b=Guid.NewGuid();var fixture=Guid.NewGuid().ToString("N");
  const string name="Synthetic Mapping Business";
  string Page(string business)=>JsonSerializer.Serialize(new {business=new {name=business},skip=0,pageSize=20,totalRecords=2,customers=new[]{new {key=a,name="Same Name",status="Paid"},new {key=b,name="Same Name",status="Paid"}}});
  long one=s.SearchClients("").Single().Id;
  // This legacy fixture has no client sequence; add the second client using parameter-free synthetic data.
  using(var db=new SqliteConnection($"Data Source={path}")){db.Open();using var q=db.CreateCommand();q.CommandText="INSERT INTO clients(client_no,client_type,company_name) VALUES('CL-000002','Business / Company','Synthetic Mapping Client')";q.ExecuteNonQuery();}
  long two=s.SearchClients("").Single(x=>x.Id!=one).Id;
  string revision="";
  await AccountingProbeTests.WithServer(200,Page(name),async(url,_)=>{
   check((await s.SaveAccountingProfileAsync(url,fixture)).Success,"mapping fixture connection saved");
   var page=await s.ReadSavedCustomersAsync(0);revision=page.ProfileRevision??"";
   check(page.Success&&revision.Length>0,"customer page bound to saved revision");
   check((await s.SaveProvisionalLinkAsync(revision,name,0,one,a)).Outcome=="Saved","explicit provisional link saved");
   check((await s.SaveProvisionalLinkAsync(revision,name,0,one,a)).Outcome=="AlreadyLinked","identical link idempotent");
   check((await s.SaveProvisionalLinkAsync(revision,name,0,two,a)).Outcome=="Conflict","duplicate external customer blocked");
   check((await s.SaveProvisionalLinkAsync(revision,name,0,one,b)).Outcome=="Conflict","duplicate local client blocked");
   check((await s.SaveProvisionalLinkAsync(revision,name,0,two,Guid.NewGuid())).Outcome=="CustomerUnavailable","missing customer blocked");
   var reopened=new LocalPractice(path);check(reopened.Login("admin",password),"mapping restart login");
   var list=await reopened.ReadProvisionalLinksAsync(revision,name,0);
   check(list.Outcome=="Loaded"&&list.Links?.Single().CustomerKey==a,"local provisional link persists after restart");
   check((await s.ReadProvisionalLinksAsync(revision,"Different Business",0)).Outcome=="BusinessChanged","unexpected business name blocked");
   s.RemoveProvisionalLink(revision,one);
   check((await s.ReadProvisionalLinksAsync(revision,name,0)).Links?.Count==0,"explicit unlink removes local link");
   check((await s.SaveProvisionalLinkAsync(revision,name,0,two,a)).Outcome=="Saved","unlink permits new explicit link");
   check((await s.SaveAccountingProfileAsync(url,fixture)).Success,"replacement rotates profile revision");
   check((await s.ReadProvisionalLinksAsync(revision,name,0)).Outcome=="ProfileChanged","old profile links inaccessible after replacement");
   var fresh=await s.ReadSavedCustomersAsync(0);
   check((await s.ReadProvisionalLinksAsync(fresh.ProfileRevision!,name,0)).Links?.Count==0,"new revision never inherits links");
   s.RemoveAccountingProfile();
   check((await s.ReadProvisionalLinksAsync(fresh.ProfileRevision!,name,0)).Outcome=="ProfileChanged","removed profile links inaccessible");
  },13);
  using(var db=new SqliteConnection($"Data Source={path}")){db.Open();using var q=db.CreateCommand();q.CommandText="UPDATE role_permissions SET allowed=0 WHERE role_id=1 AND permission_code='Accounting.Customers.Map'";q.ExecuteNonQuery();}
  check(!s.CanManageCustomerLinks(),"revoked mapping permission enforced");
  try{await s.SaveProvisionalLinkAsync(revision,name,0,one,a);throw new Exception("FAIL: revoked mapping permission");}catch(UnauthorizedAccessException){check(true,"revoked mapping write denied");}
  check(s.Login("employee",password)&&!s.CanManageCustomerLinks(),"employee mapping denied");
 }
}
