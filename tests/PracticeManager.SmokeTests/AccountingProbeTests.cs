using System.Net;
using System.Net.Sockets;
using System.Text;
using PracticeManager.Core;

internal static class AccountingProbeTests
{
    private const string Key = "synthetic-probe-key";
    public static async Task Run(Action<bool,string> check)
    {
        IAccountingConnectionProbe probe = new ManagerDesktopProbe();
        var rejected = await probe.TestAsync("http://example.invalid:12345/api2", Key);
        check(rejected.Outcome == "InvalidEndpoint", "external accounting host rejected");
        foreach (var url in new[] {"http://127.0.0.1:12345/api2?secret=x","http://127.0.0.1:12345/api2/other","http://localhost:12345/api2","https://127.0.0.1:12345/api2","http://user@127.0.0.1:12345/api2","http://127.0.0.1:0/api2"})
            check((await probe.TestAsync(url,Key)).Outcome=="InvalidEndpoint","unsafe accounting endpoint rejected: "+url);
        check((await probe.TestAsync("http://127.0.0.1:12345/api2","")).Outcome=="MissingSecret","missing accounting secret rejected");
        check((await probe.TestAsync("http://127.0.0.1:12345/api2","bad\r\nkey")).Outcome=="MissingSecret","unsafe accounting header rejected");
        await WithServer(200,"[{\"name\":\"Synthetic customer\"}]",async (url,observed)=>{
            var r=await probe.TestAsync(url,Key);
            check(r.Success && r.StatusCode==200 && r.JsonShape=="Array","read-only customer JSON array accepted");
            var req=await observed;
            check(req.StartsWith("GET /api2/customers HTTP/1.1",StringComparison.Ordinal),"probe performs customer GET only");
            check(req.Split('\n').Any(x=>x.Trim().Equals("X-API-KEY: "+Key,StringComparison.OrdinalIgnoreCase)),"probe sends expected authentication header");
            check(!r.ToString().Contains("Synthetic customer")&&!r.ToString().Contains(Key),"probe does not expose records or secret");
        });
        await WithServer(200,"{\"items\":[]}",async (url,_)=>check((await probe.TestAsync(url+"/",Key)).JsonShape=="Object","JSON object envelope accepted without schema assumption"));
        await WithServer(401,"{\"error\":\"synthetic\"}",async (url,_)=>check((await probe.TestAsync(url,Key)).Outcome=="Unauthorized","401 authentication failure reported"));
        await WithServer(403,"{}",async (url,_)=>check((await probe.TestAsync(url,Key)).Outcome=="Unauthorized","403 permission failure reported"));
        await WithServer(404,"{}",async (url,_)=>check((await probe.TestAsync(url,Key)).Outcome=="NotFound","404 endpoint failure reported"));
        await WithServer(302,"{}",async (url,_)=>check((await probe.TestAsync(url,Key)).Outcome=="RedirectBlocked","redirect blocked"));
        await WithServer(200,"<html>Not JSON</html>",async (url,_)=>check((await probe.TestAsync(url,Key)).Outcome=="InvalidResponse","non-JSON response rejected"));
        await WithServer(200,"null",async (url,_)=>check((await probe.TestAsync(url,Key)).Outcome=="InvalidResponse","non-container JSON rejected"));
        await WithServer(200,"{\"oversize\":\""+new string('a',1_100_000)+"\"}",async (url,_)=>check(!(await probe.TestAsync(url,Key)).Success,"oversize response rejected"));
        using var cts=new CancellationTokenSource();cts.Cancel();
        check((await probe.TestAsync("http://127.0.0.1:12345/api2",Key,cts.Token)).Outcome=="Cancelled","cancelled request does not connect");
    }
    internal static async Task WithServer(int status,string body,Func<string,Task<string>,Task> exercise)
    {
        using var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();
        var port=((IPEndPoint)listener.LocalEndpoint).Port;
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var observed=new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var server=Task.Run(async ()=>{
            try{
                using var client=await listener.AcceptTcpClientAsync(timeout.Token);
                using var stream=client.GetStream();
                using var reader=new StreamReader(stream,Encoding.ASCII,false,1024,true);
                var lines=new List<string>();
                while(true){var line=await reader.ReadLineAsync(timeout.Token);if(string.IsNullOrEmpty(line))break;lines.Add(line);}
                observed.TrySetResult(string.Join("\n",lines));
                var payload=Encoding.UTF8.GetBytes(body);
                var extra=status==302?"Location: http://127.0.0.1:1/api2/customers\r\n":"";
                var headers=Encoding.ASCII.GetBytes($"HTTP/1.1 {status} Test\r\nContent-Type: application/json\r\nContent-Length: {payload.Length}\r\n{extra}Connection: close\r\n\r\n");
                await stream.WriteAsync(headers,timeout.Token);
                await stream.WriteAsync(payload,timeout.Token);
            }catch(Exception e){observed.TrySetException(e);}
        });
        try{await exercise($"http://127.0.0.1:{port}/api2",observed.Task);}
        finally{timeout.Cancel();listener.Stop();await server;}
    }
}
