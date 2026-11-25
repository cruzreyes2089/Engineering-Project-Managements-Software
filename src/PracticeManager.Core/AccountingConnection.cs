using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace PracticeManager.Core;

public sealed record AccountingProbeResult(string Outcome,int? StatusCode=null,string? JsonShape=null)
{
    public bool Success => Outcome=="Connected";
}
public interface IAccountingConnectionProbe
{
    Task<AccountingProbeResult> TestAsync(string endpoint,string secret,CancellationToken cancellationToken=default);
}
/// <summary>Development-only, read-only API2 connectivity probe; never persists credentials or customer data.</summary>
public sealed class ManagerDesktopProbe : IAccountingConnectionProbe
{
    private const int MaxResponseBytes = 1_048_576;
    public async Task<AccountingProbeResult> TestAsync(string endpoint,string secret,CancellationToken cancellationToken=default)
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
        using var request=new HttpRequestMessage(HttpMethod.Get,$"http://127.0.0.1:{port}/api2/customers");
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
            return kind is JsonValueKind.Array or JsonValueKind.Object
                ?new("Connected",code,kind.ToString()):new("InvalidResponse",code);
        }
        catch(OperationCanceledException){return new(cancellationToken.IsCancellationRequested?"Cancelled":"Timeout");}
        catch(JsonException){return new("InvalidResponse",200);}
        catch(HttpRequestException){return new("ConnectionFailed");}
        catch(IOException){return new("ConnectionFailed");}
    }
}
