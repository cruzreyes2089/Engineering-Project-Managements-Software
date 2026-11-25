namespace PracticeManager.Core;
public sealed record AccountingProbeResult(string Outcome,int? StatusCode=null,string? JsonShape=null)
{
    public bool Success => Outcome=="Connected";
}
public interface IAccountingConnectionProbe
{
    Task<AccountingProbeResult> TestAsync(string endpoint,string secret,CancellationToken cancellationToken=default);
}
public sealed class ManagerDesktopProbe : IAccountingConnectionProbe
{
    public Task<AccountingProbeResult> TestAsync(string endpoint,string secret,CancellationToken cancellationToken=default)
        => Task.FromResult(new AccountingProbeResult("NotImplemented"));
}
