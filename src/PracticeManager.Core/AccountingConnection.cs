using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace PracticeManager.Core;

public sealed record AccountingProbeResult(string Outcome,int? StatusCode=null,string? JsonShape=null,string? SchemaSummary=null,AccountingCustomerPage? CustomerPage=null,string? ProfileRevision=null,AccountingAccountRegister? Accounts=null)
{
    public bool Success => Outcome=="Connected";
}
public enum AccountingDiscoveryResource { IdentityFields, ChartOfAccounts, SalesInvoices, Receipts, Payments }
public interface IAccountingConnectionProbe
{
    Task<AccountingProbeResult> TestAsync(string endpoint,string secret,CancellationToken cancellationToken=default);
}
/// <summary>Development-only, read-only API2 connectivity probe; never persists credentials or customer data.</summary>
public sealed class ManagerDesktopProbe : IAccountingConnectionProbe
{
    public Task<AccountingProbeResult> InspectCustomersAsync(string endpoint,string secret,CancellationToken cancellationToken=default)
        =>RequestAsync(endpoint,secret,true,cancellationToken);
    public Task<AccountingProbeResult> ReadCustomerPageAsync(string endpoint,string secret,int skip,CancellationToken cancellationToken=default)
        =>skip<0||skip>int.MaxValue-20?Task.FromResult(new AccountingProbeResult("InvalidPage")):RequestAsync(endpoint,secret,false,cancellationToken,skip);
    public Task<AccountingProbeResult> InspectResourceAsync(string endpoint,string secret,AccountingDiscoveryResource resource,CancellationToken cancellationToken=default)
        => Enum.IsDefined(resource)?RequestAsync(endpoint,secret,true,cancellationToken,resource:resource):Task.FromResult(new AccountingProbeResult("InvalidResource"));
    public Task<AccountingProbeResult> ReadAccountsAsync(string endpoint,string secret,CancellationToken cancellationToken=default)
        =>RequestAsync(endpoint,secret,false,cancellationToken,resource:AccountingDiscoveryResource.ChartOfAccounts,readAccounts:true);
    private const int MaxResponseBytes = 1_048_576;
    public Task<AccountingProbeResult> TestAsync(string endpoint,string secret,CancellationToken cancellationToken=default)
        =>RequestAsync(endpoint,secret,false,cancellationToken);

    private async Task<AccountingProbeResult> RequestAsync(string endpoint,string secret,bool inspect,CancellationToken cancellationToken,int? customerSkip=null,AccountingDiscoveryResource? resource=null,bool readAccounts=false)
    {
        var match=Regex.Match(endpoint?.Trim()??"", @"\Ahttp://127\.0\.0\.1:([0-9]{1,5})/api2/?\z",RegexOptions.CultureInvariant);
        if(!match.Success || !int.TryParse(match.Groups[1].Value,out var port) || port<1 || port>65535)
            return new("InvalidEndpoint");
        if(string.IsNullOrWhiteSpace(secret) || secret.Length>4096 || secret.Any(c=>c<33||c>126))
            return new("MissingSecret");
        if(cancellationToken.IsCancellationRequested)return new("Cancelled");
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        var ct=deadline.Token;
        // Exact literal loopback host; no proxies, credentials from URL, cookies or redirects.
        using var handler=new HttpClientHandler{AllowAutoRedirect=false,UseProxy=false,UseCookies=false,AutomaticDecompression=DecompressionMethods.None};
        using var client=new HttpClient(handler){Timeout=Timeout.InfiniteTimeSpan};
        // Fixed resource allowlist. No arbitrary URLs, form requests, or write methods.
        var path=resource switch
        {
            AccountingDiscoveryResource.IdentityFields=>"text-custom-fields?skip=0&pageSize=2",
            AccountingDiscoveryResource.ChartOfAccounts=>"chart-of-accounts?fields=Key&fields=Code&fields=Name",
            AccountingDiscoveryResource.SalesInvoices=>"sales-invoices?skip=0&pageSize=2",
            AccountingDiscoveryResource.Receipts=>"receipts?skip=0&pageSize=2",
            AccountingDiscoveryResource.Payments=>"payments?skip=0&pageSize=2",
            null=>"customers"+(customerSkip is int offset?"?skip="+offset.ToString(System.Globalization.CultureInfo.InvariantCulture)+"&pageSize=20":inspect?"?skip=0&pageSize=2":""),
            _=>throw new ArgumentOutOfRangeException(nameof(resource))
        };
        using var request=new HttpRequestMessage(HttpMethod.Get,$"http://127.0.0.1:{port}/api2/"+path);
        request.Headers.Add("X-API-KEY",secret);
        request.Headers.Accept.ParseAdd("application/json");
        try
        {
            using var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,ct).ConfigureAwait(false);
            var code=(int)response.StatusCode;
            if(code is 401 or 403)return new("Unauthorized",code);
            if(code==404)return new("NotFound",code);
            if(code>=300&&code<400)return new("RedirectBlocked",code);
            if(code!=200)return new("HttpError",code);
            if(response.Content.Headers.ContentLength>MaxResponseBytes)return new("ResponseTooLarge",code);
            using var stream=await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var buffer=new MemoryStream();
            var chunk=new byte[8192];
            while(true)
            {
                var n=await stream.ReadAsync(chunk.AsMemory(),ct).ConfigureAwait(false);
                if(n==0)break;
                if(buffer.Length+n>MaxResponseBytes)return new("ResponseTooLarge",code);
                buffer.Write(chunk,0,n);
            }
            buffer.Position=0;
            using var json=await JsonDocument.ParseAsync(buffer,cancellationToken:ct).ConfigureAwait(false);
            var kind=json.RootElement.ValueKind;
            if(readAccounts)
            {
                var accounts=AccountingAccountParser.Parse(json.RootElement);
                return accounts==null?new("InvalidAccountRegister",code):new("Connected",code,"Object",Accounts:accounts);
            }
            if(customerSkip is int requested)
            {
                var page=AccountingCustomerPageParser.Parse(json.RootElement,requested);
                return page==null?new("InvalidCustomerPage",code):new("Connected",code,"Object",CustomerPage:page);
            }
            return kind is JsonValueKind.Array or JsonValueKind.Object
                ?new("Connected",code,kind.ToString(),inspect?DescribeSchema(json.RootElement,resource):null):new("InvalidResponse",code);
        }
        catch(OperationCanceledException){return new(cancellationToken.IsCancellationRequested?"Cancelled":"Timeout");}
        catch(JsonException){return new("InvalidResponse",200);}
        catch(HttpRequestException){return new("ConnectionFailed");}
        catch(IOException){return new("ConnectionFailed");}
    }

    // Only bounded structural metadata is returned. Never return JSON field values.
    private static string DescribeSchema(JsonElement root,AccountingDiscoveryResource? resource)
    {
        var lines=new List<string>{(resource?.ToString()??"Customer")+" schema (field names and types only)"};
        string Label(string name)=>Regex.IsMatch(name,@"\A[A-Za-z_][A-Za-z0-9_]{0,47}\z",RegexOptions.CultureInvariant)
            && !Guid.TryParse(name,out _) ?name:"[field name omitted]";
        string Type(JsonElement value)=>value.ValueKind==JsonValueKind.String && Guid.TryParse(value.GetString(),out _)
            ?"String (UUID-shaped)":value.ValueKind is JsonValueKind.True or JsonValueKind.False?"Boolean":value.ValueKind.ToString();
        void Array(JsonElement array,string label)
        {
            var count=array.GetArrayLength();
            lines.Add($"{label} — sample rows: {Math.Min(count,2)}"+(count>2?" (sample truncated)":""));
            // Limit inspection to the first two rows and 32 fields per row, never recurse into nested values.
            var fields=new SortedSet<string>(StringComparer.Ordinal);
            foreach(var row in array.EnumerateArray().Take(2))
            {
                if(row.ValueKind==JsonValueKind.Object)
                    foreach(var p in row.EnumerateObject().Take(32))
                        fields.Add($"  {Label(p.Name)}: {Type(p.Value)}");
                else fields.Add($"  Row: {Type(row)}");
            }
            lines.AddRange(fields.Take(48));
            if(fields.Count>48)lines.Add("  Additional fields omitted.");
        }
        if(root.ValueKind==JsonValueKind.Array)Array(root,"Root array");
        else
        {
            var arrays=0;
            foreach(var p in root.EnumerateObject().Take(32))
            {
                var label=Label(p.Name);
                lines.Add($"{label}: {Type(p.Value)}");
                if(p.Value.ValueKind==JsonValueKind.Array && arrays++<3)Array(p.Value,label);
            }
            if(root.EnumerateObject().Count()>32)lines.Add("Additional root fields omitted.");
        }
        return string.Join(Environment.NewLine,lines);
    }
}
