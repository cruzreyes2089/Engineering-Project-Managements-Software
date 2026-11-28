using Microsoft.Data.Sqlite;
using PracticeManager.Core;
using System.Security.Cryptography;
using System.Text;
namespace PracticeManager.Persistence;
public sealed record DirectoryCustomer(string Name,string Status,bool Present,bool Linked)
{
 public override string ToString()=>"[Directory customer]";
}
public sealed record DirectoryAccountRole(string Role,Guid Key,string Name,string? Code)
{
 public override string ToString()=>"[Provisional account role]";
}
public sealed record DirectoryReport(string Revision,string SyncedUtc,IReadOnlyList<DirectoryCustomer> Customers,int MissingLinked,int ActiveUnlinked,IReadOnlyList<DirectoryAccountRole> Roles)
{
 public override string ToString()=>"Directory report [values omitted]";
}
public sealed record DirectoryResult(string Outcome,DirectoryReport? Report=null);
public sealed partial class LocalPractice
{
 private sealed record DirectoryContext(long Actor,long Generation,StoredProfile Profile,Guid Marker);
 private sealed record Scan(string Business,IReadOnlyList<AccountingCustomer> Rows);
 public bool CanSyncDirectory()=>CanManageBusinessMarker()&&CanManageCustomerLinks()&&Can("Accounting.Directory.Sync");
 private long RequireDirectory(SqliteConnection c,SqliteTransaction t)
 {
  var actor=RequireMarker(c,t);RequireMapping(c,t);Require(c,t,"Accounting.Directory.Sync");return actor;
 }
 private (string Outcome,DirectoryContext? Context) GetDirectoryContext()
 {
  using var c=Open();using var t=c.BeginTransaction();
  var actor=RequireDirectory(c,t);var p=ReadProfile(c,t,actor);var m=ReadMarker(c,t);
  return p==null?("NotConfigured",null):m==null?("MarkerNotEnrolled",null):("Connected",new(actor,sessionGeneration,p,m.Value));
 }
 private bool CurrentDirectoryContext(SqliteConnection c,SqliteTransaction t,DirectoryContext d)
 {
  if(RequireDirectory(c,t)!=d.Actor||sessionGeneration!=d.Generation)throw new UnauthorizedAccessException();
  return ReadProfile(c,t,d.Actor)?.Revision==d.Profile.Revision&&ReadMarker(c,t)==d.Marker;
 }
 private string? DirectoryBusiness(SqliteConnection c,SqliteTransaction t,DirectoryContext d)
 {
  using var q=Command(c,t,"SELECT business_name FROM accounting_directory_scopes WHERE user_id=$a AND revision=$r AND marker_key=$m",("$a",d.Actor),("$r",d.Profile.Revision),("$m",d.Marker.ToString("D")));
  return q.ExecuteScalar() as string;
 }
 private static async Task<(string Outcome,Scan? Data)> ScanCustomersAsync(ManagerDesktopProbe probe,string endpoint,string key,CancellationToken ct)
 {
  var rows=new List<AccountingCustomer>();var ids=new HashSet<Guid>();string? business=null;int? total=null;
  // Bound calls as well as rows; never publish a partial directory.
  for(int pageNo=0;pageNo<10;pageNo++){
   var r=await probe.ReadCustomerPageAsync(endpoint,key,rows.Count,ct);if(!r.Success||r.CustomerPage==null)return (r.Outcome,null);
   var page=r.CustomerPage;
   if(string.IsNullOrWhiteSpace(page.BusinessName)||page.TotalRecords>200)return ("DirectoryLimitOrIdentity",null);
   if(total.HasValue&&(total!=page.TotalRecords||business!=page.BusinessName))return ("DirectoryChanged",null);
   total=page.TotalRecords;business=page.BusinessName;
   foreach(var row in page.Customers){if(!ids.Add(row.Key))return ("DirectoryChanged",null);rows.Add(row);}
   if(rows.Count==total)return ("Connected",new(business!,rows.AsReadOnly()));
   if(page.Customers.Count==0)return ("DirectoryChanged",null);
  }
  return ("DirectoryLimitOrIdentity",null);
 }
 private DirectoryReport BuildDirectoryReport(SqliteConnection c,SqliteTransaction t,DirectoryContext d)
 {
  var a=d.Actor;var rev=d.Profile.Revision;
  var time=(string)Scalar(c,t,"SELECT synced_utc FROM accounting_directory_scopes WHERE user_id=$a AND revision=$r",("$a",a),("$r",rev))!;
  var rows=new List<DirectoryCustomer>();
  using(var q=Command(c,t,"SELECT d.name,d.status,d.present,EXISTS(SELECT 1 FROM accounting_customer_links l WHERE l.user_id=d.user_id AND l.revision=d.revision AND l.customer_key=d.customer_key) FROM accounting_customer_directory d WHERE d.user_id=$a AND d.revision=$r ORDER BY d.name,d.customer_key",("$a",a),("$r",rev)))
  using(var rd=q.ExecuteReader())while(rd.Read())rows.Add(new(rd.GetString(0),rd.GetString(1),rd.GetInt64(2)==1,rd.GetInt64(3)==1));
  var missing=Convert.ToInt32(Scalar(c,t,"SELECT count(*) FROM accounting_customer_links l WHERE l.user_id=$a AND l.revision=$r AND NOT EXISTS(SELECT 1 FROM accounting_customer_directory d WHERE d.user_id=l.user_id AND d.revision=l.revision AND d.customer_key=l.customer_key AND d.present=1)",("$a",a),("$r",rev)));
  var unlinked=Convert.ToInt32(Scalar(c,t,"SELECT count(*) FROM clients c WHERE c.status='Active' AND NOT EXISTS(SELECT 1 FROM accounting_customer_links l WHERE l.user_id=$a AND l.revision=$r AND l.client_id=c.id)",("$a",a),("$r",rev)));
  var roles=new List<DirectoryAccountRole>();
  using(var q=Command(c,t,"SELECT role,account_key,account_name,account_code FROM accounting_account_roles WHERE user_id=$a AND revision=$r ORDER BY role",("$a",a),("$r",rev)))
  using(var rd=q.ExecuteReader())while(rd.Read())roles.Add(new(rd.GetString(0),Guid.Parse(rd.GetString(1)),rd.GetString(2),rd.IsDBNull(3)?null:rd.GetString(3)));
  return new(rev,time,rows.AsReadOnly(),missing,unlinked,roles.AsReadOnly());
 }
 public DirectoryResult ReadCachedDirectory()
 {
  var start=GetDirectoryContext();if(start.Context==null)return new(start.Outcome);
  var d=start.Context;
  using var c=Open();using var t=c.BeginTransaction();
  if(!CurrentDirectoryContext(c,t,d))return new("ProfileChanged");
  if(DirectoryBusiness(c,t,d)==null)return new("NotSynchronized");
  return new("Cached",BuildDirectoryReport(c,t,d));
 }
 public async Task<DirectoryResult> SyncCustomerDirectoryAsync(CancellationToken ct=default)
 {
  var start=GetDirectoryContext();if(start.Context==null)return new(start.Outcome);var d=start.Context;
  if(!OperatingSystem.IsWindows())return new("CredentialUnavailable");
  byte[] clear;
  try{clear=ProtectedData.Unprotect(d.Profile.ProtectedValue,Context(d.Actor,d.Profile.Endpoint),DataProtectionScope.CurrentUser);}
  catch(CryptographicException){return new("CredentialUnavailable");}
  Scan? first;
  using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(TimeSpan.FromSeconds(60));var token=deadline.Token;
  try{
   var key=Encoding.UTF8.GetString(clear);var probe=new ManagerDesktopProbe();
   var before=await VerifyMarkerAtAsync(d.Profile.Endpoint,key,d.Marker,token);if(!before.Success)return new(before.Outcome);
   var a=await ScanCustomersAsync(probe,d.Profile.Endpoint,key,token);if(a.Data==null)return new(a.Outcome);first=a.Data;
   var b=await ScanCustomersAsync(probe,d.Profile.Endpoint,key,token);if(b.Data==null)return new(b.Outcome);
   if(first.Business!=b.Data.Business||!first.Rows.OrderBy(x=>x.Key).SequenceEqual(b.Data.Rows.OrderBy(x=>x.Key)))return new("DirectoryChanged");
   var after=await VerifyMarkerAtAsync(d.Profile.Endpoint,key,d.Marker,token);if(!after.Success)return new(after.Outcome);
  }finally{CryptographicOperations.ZeroMemory(clear);}
  using var write=Open();using var tx=write.BeginTransaction(deferred:false);
  if(!CurrentDirectoryContext(write,tx,d))return new("ProfileChanged");
  if(token.IsCancellationRequested)return new("Cancelled");
  var business=DirectoryBusiness(write,tx,d);var linkBusiness=ScopeBusiness(write,tx,d.Actor,d.Profile.Revision);
  if(business!=null&&business!=first.Business||!string.IsNullOrEmpty(linkBusiness)&&linkBusiness!=first.Business)return new("BusinessChanged");
  var now=UtcNow();var rev=d.Profile.Revision;var actor=d.Actor;
  Exec(write,tx,"INSERT INTO accounting_directory_scopes(user_id,revision,marker_key,business_name,synced_utc) VALUES($a,$r,$m,$b,$u) ON CONFLICT(user_id,revision) DO UPDATE SET synced_utc=excluded.synced_utc",("$a",actor),("$r",rev),("$m",d.Marker.ToString("D")),("$b",first.Business),("$u",now));
  Exec(write,tx,"UPDATE accounting_customer_directory SET present=0 WHERE user_id=$a AND revision=$r",("$a",actor),("$r",rev));
  foreach(var row in first.Rows)Exec(write,tx,"INSERT INTO accounting_customer_directory(user_id,revision,customer_key,name,status,present) VALUES($a,$r,$k,$n,$s,1) ON CONFLICT(user_id,revision,customer_key) DO UPDATE SET name=excluded.name,status=excluded.status,present=1",("$a",actor),("$r",rev),("$k",row.Key.ToString("D")),("$n",row.Name),("$s",row.Status));
  Audit(write,tx,actor,"Synchronize","AccountingCustomerDirectory",actor);
  var report=BuildDirectoryReport(write,tx,d);tx.Commit();return new("Synchronized",report);
 }
 public async Task<DirectoryResult> SaveProvisionalAccountRoleAsync(string revision,string role,AccountingAccount chosen,CancellationToken ct=default)
 {
  var start=GetDirectoryContext();if(start.Context==null)return new(start.Outcome);var d=start.Context;
  if(role is not ("ServiceIncome" or "OfficeExpense")||chosen.Key==Guid.Empty)return new("InvalidRole");
  if(revision!=d.Profile.Revision)return new("ProfileChanged");
  var result=await ReadSavedAccountsAsync(ct);
  if(!result.Success||result.Accounts==null)return new(result.Outcome);
  if(result.ProfileRevision!=revision)return new("ProfileChanged");
  var selected=result.Accounts.Rows.SingleOrDefault(x=>x.Key==chosen.Key);
  if(selected!=chosen)return new("AccountChanged");
  using var c=Open();using var t=c.BeginTransaction(deferred:false);
  if(!CurrentDirectoryContext(c,t,d))return new("ProfileChanged");
  if(ct.IsCancellationRequested)return new("Cancelled");
  var business=DirectoryBusiness(c,t,d);
  if(business==null)return new("NotSynchronized");
  if(business!=result.Accounts.BusinessName)return new("BusinessChanged");
  var key=chosen.Key.ToString("D");
  if(Convert.ToInt64(Scalar(c,t,"SELECT count(*) FROM accounting_account_roles WHERE user_id=$a AND revision=$r AND account_key=$k AND role<>$o",("$a",d.Actor),("$r",revision),("$k",key),("$o",role)))!=0)return new("RoleConflict");
  Exec(c,t,"INSERT INTO accounting_account_roles(user_id,revision,role,account_key,account_name,account_code) VALUES($a,$r,$o,$k,$n,$c) ON CONFLICT(user_id,revision,role) DO UPDATE SET account_key=excluded.account_key,account_name=excluded.account_name,account_code=excluded.account_code",("$a",d.Actor),("$r",revision),("$o",role),("$k",key),("$n",chosen.Name),("$c",chosen.Code));
  Audit(c,t,d.Actor,"Assign","ProvisionalAccountRole",d.Actor);
  var report=BuildDirectoryReport(c,t,d);t.Commit();return new("RoleSaved",report);
 }
 public DirectoryResult RemoveProvisionalAccountRole(string revision,string role)
 {
  var start=GetDirectoryContext();if(start.Context==null)return new(start.Outcome);var d=start.Context;
  if(revision!=d.Profile.Revision)return new("ProfileChanged");
  using var c=Open();using var t=c.BeginTransaction(deferred:false);
  if(!CurrentDirectoryContext(c,t,d))return new("ProfileChanged");
  if(DirectoryBusiness(c,t,d)==null)return new("NotSynchronized");
  if(Exec(c,t,"DELETE FROM accounting_account_roles WHERE user_id=$a AND revision=$r AND role=$o",("$a",d.Actor),("$r",revision),("$o",role))>0)Audit(c,t,d.Actor,"Remove","ProvisionalAccountRole",d.Actor);
  var report=BuildDirectoryReport(c,t,d);t.Commit();return new("RoleRemoved",report);
 }
}
