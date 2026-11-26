using PracticeManager.Core;
namespace PracticeManager.Persistence;
public sealed record ProvisionalCustomerLink(long ClientId,Guid CustomerKey);
public sealed record ProvisionalLinkResult(string Outcome,IReadOnlyList<ProvisionalCustomerLink>? Links=null);
public sealed partial class LocalPractice
{
 public bool CanManageCustomerLinks()=>false;
 public Task<ProvisionalLinkResult> ReadProvisionalLinksAsync(string revision,string business,int skip,CancellationToken ct=default)=>Task.FromResult(new ProvisionalLinkResult("NotImplemented"));
 public Task<ProvisionalLinkResult> SaveProvisionalLinkAsync(string revision,string business,int skip,long clientId,Guid customerKey,CancellationToken ct=default)=>Task.FromResult(new ProvisionalLinkResult("NotImplemented"));
 public void RemoveProvisionalLink(string revision,long clientId)=>throw new NotImplementedException();
}
