using PracticeManager.Core;
namespace PracticeManager.Persistence;
public sealed record AccountingProfile(string Endpoint,string LastSuccessUtc);
public sealed partial class LocalPractice
{
 public AccountingProfile? ReadAccountingProfile()=>throw new NotImplementedException();
 public Task<AccountingProbeResult> SaveAccountingProfileAsync(string endpoint,string key,CancellationToken ct=default)=>Task.FromResult(new AccountingProbeResult("NotImplemented"));
 public Task<AccountingProbeResult> TestSavedAccountingProfileAsync(CancellationToken ct=default)=>Task.FromResult(new AccountingProbeResult("NotImplemented"));
 public void RemoveAccountingProfile()=>throw new NotImplementedException();
}
