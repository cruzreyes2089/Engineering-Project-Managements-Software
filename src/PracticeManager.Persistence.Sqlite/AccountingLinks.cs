using Microsoft.Data.Sqlite;
using PracticeManager.Core;
namespace PracticeManager.Persistence;
public sealed record ProvisionalCustomerLink(long ClientId,Guid CustomerKey);
public sealed record ProvisionalLinkResult(string Outcome,IReadOnlyList<ProvisionalCustomerLink>? Links=null);

public sealed partial class LocalPractice
{
 public bool CanManageCustomerLinks()=>Can("Accounting.Customers.Map")&&Can("Accounting.Connection.Test")&&Can("Clients.View");
 private long RequireMapping(SqliteConnection c,SqliteTransaction t)
 {
  var actor=Require(c,t,"Accounting.Customers.Map");Require(c,t,"Accounting.Connection.Test");Require(c,t,"Clients.View");return actor;
 }
 private string? ScopeBusiness(SqliteConnection c,SqliteTransaction t,long actor,string revision)
 =>Convert.ToString(Scalar(c,t,"SELECT business_name FROM accounting_link_scopes WHERE user_id=$a AND revision=$r",("$a",actor),("$r",revision)));
 private bool ProfileMatches(SqliteConnection c,SqliteTransaction t,long actor,string revision)
 =>!string.IsNullOrWhiteSpace(revision)&&ReadProfile(c,t,actor)?.Revision==revision;
 private async Task<ProvisionalLinkResult> WithVerifiedLinkPageAsync(string revision,string business,int skip,Func<SqliteConnection,SqliteTransaction,long,AccountingCustomerPage,ProvisionalLinkResult> action,CancellationToken ct)
 {
  var generation=sessionGeneration;long actor;
  using(var c=Open())using(var t=c.BeginTransaction()){
   actor=RequireMapping(c,t);
   if(!ProfileMatches(c,t,actor,revision))return new("ProfileChanged");
  }
  if(string.IsNullOrWhiteSpace(business))return new("BusinessChanged");
  var result=await ReadSavedCustomersAsync(skip,ct);
  using var write=Open();using var tx=write.BeginTransaction(deferred:false);
  if(sessionGeneration!=generation||RequireMapping(write,tx)!=actor)throw new UnauthorizedAccessException();
  if(ct.IsCancellationRequested)return new("Cancelled");
  if(!ProfileMatches(write,tx,actor,revision))return new("ProfileChanged");
  if(!result.Success||result.CustomerPage==null)return new(result.Outcome);
  if(result.ProfileRevision!=revision)return new("ProfileChanged");
  var page=result.CustomerPage;
  if(!string.Equals(page.BusinessName,business,StringComparison.Ordinal))return new("BusinessChanged");
  var scope=ScopeBusiness(write,tx,actor,revision);
  if(!string.IsNullOrEmpty(scope)&&!string.Equals(scope,business,StringComparison.Ordinal))return new("BusinessChanged");
  var outcome=action(write,tx,actor,page);tx.Commit();return outcome;
 }
 public Task<ProvisionalLinkResult> ReadProvisionalLinksAsync(string revision,string business,int skip,CancellationToken ct=default)
 =>WithVerifiedLinkPageAsync(revision,business,skip,(c,t,actor,page)=>{
  var rows=new List<ProvisionalCustomerLink>();
  using var q=Command(c,t,"SELECT client_id,customer_key FROM accounting_customer_links WHERE user_id=$a AND revision=$r ORDER BY client_id",("$a",actor),("$r",revision));
  using var reader=q.ExecuteReader();
  while(reader.Read())rows.Add(new(reader.GetInt64(0),Guid.Parse(reader.GetString(1))));
  return new("Loaded",rows.AsReadOnly());
 },ct);
 public Task<ProvisionalLinkResult> SaveProvisionalLinkAsync(string revision,string business,int skip,long clientId,Guid customerKey,CancellationToken ct=default)
 =>WithVerifiedLinkPageAsync(revision,business,skip,(c,t,actor,page)=>{
  if(customerKey==Guid.Empty||!page.Customers.Any(x=>x.Key==customerKey))return new("CustomerUnavailable");
  if(Convert.ToString(Scalar(c,t,"SELECT status FROM clients WHERE id=$id",("$id",clientId)))!="Active")return new("ClientUnavailable");
  var key=customerKey.ToString("D");
  using(var q=Command(c,t,"SELECT client_id,customer_key FROM accounting_customer_links WHERE user_id=$a AND revision=$r AND (client_id=$id OR customer_key=$k)",("$a",actor),("$r",revision),("$id",clientId),("$k",key)))
  using(var reader=q.ExecuteReader()){
   if(reader.Read())return new(reader.GetInt64(0)==clientId&&reader.GetString(1)==key?"AlreadyLinked":"Conflict");
  }
  Exec(c,t,"INSERT OR IGNORE INTO accounting_link_scopes(user_id,revision,business_name) VALUES($a,$r,$b)",("$a",actor),("$r",revision),("$b",business));
  Exec(c,t,"INSERT INTO accounting_customer_links(user_id,revision,client_id,customer_key) VALUES($a,$r,$id,$k)",("$a",actor),("$r",revision),("$id",clientId),("$k",key));
  Audit(c,t,actor,"Link","ProvisionalAccountingCustomer",clientId);return new("Saved");
 },ct);
 public void RemoveProvisionalLink(string revision,long clientId,Guid customerKey)
 {
  using var c=Open();using var t=c.BeginTransaction(deferred:false);
  var actor=RequireMapping(c,t);
  if(!ProfileMatches(c,t,actor,revision))throw new InvalidOperationException("Saved profile changed.");
  if(Exec(c,t,"DELETE FROM accounting_customer_links WHERE user_id=$a AND revision=$r AND client_id=$id AND customer_key=$k",("$a",actor),("$r",revision),("$id",clientId),("$k",customerKey.ToString("D")))!=1)throw new InvalidOperationException("Link no longer exists.");
  Audit(c,t,actor,"Unlink","ProvisionalAccountingCustomer",clientId);t.Commit();
 }
}
