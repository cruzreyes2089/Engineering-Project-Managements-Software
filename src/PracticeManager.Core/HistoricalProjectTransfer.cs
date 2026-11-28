using System.Security.Cryptography;
using System.Text.Json;

namespace PracticeManager.Core;

public sealed record HistoricalProjectRow(string LegacyId,string LegacyClientId,string LegacyProjectNumber,
    string Title,string Site,string LegacyStatus,string LegacyAuthorizationStatus,string AuthorizationEvidenceRef)
{
    public override string ToString()=>"[Confidential historical project]";
}

public sealed class HistoricalProjectPackage
{
    internal HistoricalProjectPackage(string source,string crm,string transfer,string census,
        IReadOnlyList<HistoricalProjectRow> rows,int inactive,int duplicates)
    {
        SourceSha256=source;CrmTransferSha256=crm;ProjectTransferSha256=transfer;CensusSha256=census;
        Rows=Array.AsReadOnly(rows.ToArray());InactiveClientProjectCount=inactive;DuplicateNumberGroups=duplicates;
    }
    public string SourceSha256{get;}
    public string CrmTransferSha256{get;}
    public string ProjectTransferSha256{get;}
    public string CensusSha256{get;}
    public IReadOnlyList<HistoricalProjectRow> Rows{get;}
    public int InactiveClientProjectCount{get;}
    public int DuplicateNumberGroups{get;}
    public bool SourceAuthenticityVerified=>false;
    public bool HistoricalAuthorizationVerified=>false;
    public bool NativeProjectCreationAuthorized=>false;
    public bool WorkAuthorized=>false;
    public bool FinancialMigrationAuthorized=>false;
    public override string ToString()=>"[Confidential historical project package]";
}

public static class HistoricalProjectTransfer
{
    public const int MaxBytes=10*1024*1024;
    private const int MaxRows=10000;
    private static void Require(bool condition,string message)
    {
        if(!condition)throw new ArgumentException(message);
    }
    private static string Digest(byte[] value)=>Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();
    private static bool ValidDigest(string value)=>value is not null&&value.Length==64&&
        value.All(c=>c is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static byte[] Snapshot(byte[] data,string expected)
    {
        Require(data is not null&&data.Length is >=1 and <=MaxBytes,"Historical input exceeds supported size.");
        Require(ValidDigest(expected),"Invalid expected SHA-256.");
        var copy=data!.ToArray();
        Require(Digest(copy)==expected,"Historical input digest mismatch.");
        return copy;
    }
    private static void Exact(JsonElement item,params string[] names)
    {
        Require(item.ValueKind==JsonValueKind.Object,"Expected historical object.");
        var actual=item.EnumerateObject().Select(x=>x.Name).ToArray();
        Require(actual.Length==names.Length&&actual.ToHashSet(StringComparer.Ordinal).SetEquals(names),
            "Unexpected, duplicate or missing historical fields.");
    }
    private static string Field(JsonElement row,string key,int max,bool required=false)
    {
        var item=row.GetProperty(key);
        Require(item.ValueKind==JsonValueKind.String,"Expected historical text.");
        var value=item.GetString()!;
        int scalars=0;
        for(int i=0;i<value.Length;i++)
        {
            var ch=value[i];
            Require(!char.IsControl(ch),"Unsupported historical text.");
            if(char.IsHighSurrogate(ch))
            {
                Require(i+1<value.Length&&char.IsLowSurrogate(value[i+1]),"Invalid Unicode.");
                i++;
            }
            else Require(!char.IsLowSurrogate(ch),"Invalid Unicode.");
            scalars++;
        }
        Require(scalars<=max&&(!required||!string.IsNullOrWhiteSpace(value)),"Historical text length or presence invalid.");
        return value;
    }
    private static string Id(JsonElement row,string key)
    {
        var value=Field(row,key,100,true);
        Require(value.All(c=>char.IsAsciiLetterOrDigit(c)||c is '.' or '_' or '-'),"Invalid historical identifier.");
        return value;
    }
    private static (string Id,string Parent,string Number) Identity(JsonElement row)
        =>(Id(row,"legacyId"),Id(row,"legacyClientId"),Field(row,"legacyProjectNumber",100,true));

    public static HistoricalProjectPackage Parse(byte[] crmBytes,byte[] projectBytes,byte[] censusBytes,
        string expectedSourceSha256,string expectedCrmSha256,string expectedProjectSha256,string expectedCensusSha256)
    {
        Require(ValidDigest(expectedSourceSha256),"Invalid independently retained source hash.");
        var crm=Snapshot(crmBytes,expectedCrmSha256);
        var projects=Snapshot(projectBytes,expectedProjectSha256);
        var census=Snapshot(censusBytes,expectedCensusSha256);
        try
        {
            var clients=LegacyCrmTransfer.Parse(crm).Clients.ToDictionary(x=>x.LegacyId,x=>x.Value.Status,StringComparer.OrdinalIgnoreCase);
            using var p=JsonDocument.Parse(projects,new JsonDocumentOptions{MaxDepth=8});
            using var c=JsonDocument.Parse(census,new JsonDocumentOptions{MaxDepth=8});
            var pr=p.RootElement;var cr=c.RootElement;
            Exact(pr,"format","sourceSha256","crmTransferSha256","projects");
            Exact(cr,"format","sourceSha256","crmTransferSha256","projectTransferSha256","projects");
            Require(Field(pr,"format",40,true)=="CRC-Legacy-Projects-v1"&&
                Field(cr,"format",40,true)=="CRC-Legacy-Project-Census-v1","Unsupported historical package format.");
            foreach(var root in new[]{pr,cr})
                Require(Field(root,"sourceSha256",64,true)==expectedSourceSha256&&
                    Field(root,"crmTransferSha256",64,true)==expectedCrmSha256,"Historical source or CRM binding mismatch.");
            Require(Field(cr,"projectTransferSha256",64,true)==expectedProjectSha256,"Census transfer binding mismatch.");
            var projectRows=pr.GetProperty("projects");var censusRows=cr.GetProperty("projects");
            Require(projectRows.ValueKind==JsonValueKind.Array&&censusRows.ValueKind==JsonValueKind.Array&&
                projectRows.GetArrayLength() is >=1 and <=MaxRows&&censusRows.GetArrayLength()==projectRows.GetArrayLength(),
                "Historical archive requires matching nonempty bounded batches.");
            var original=new Dictionary<string,(string Parent,string Number)>(StringComparer.Ordinal);
            var censusIds=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(var item in censusRows.EnumerateArray())
            {
                Exact(item,"legacyId","legacyClientId","legacyProjectNumber");
                var id=Identity(item);
                Require(censusIds.Add(id.Id)&&clients.ContainsKey(id.Parent),"Duplicate or orphan census identity.");
                original.Add(id.Id,(id.Parent,id.Number));
            }
            var rows=new List<HistoricalProjectRow>();
            var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int inactive=0;
            foreach(var item in projectRows.EnumerateArray())
            {
                Exact(item,"legacyId","legacyClientId","legacyProjectNumber","title","site",
                    "legacyStatus","legacyAuthorizationStatus","authorizationEvidenceRef");
                var id=Identity(item);
                Require(seen.Add(id.Id)&&clients.ContainsKey(id.Parent),"Duplicate or orphan historical identity.");
                Require(original.TryGetValue(id.Id,out var expected)&&expected==(id.Parent,id.Number),
                    "Historical identities differ from independent census.");
                if(clients[id.Parent]=="Inactive")inactive++;
                rows.Add(new(id.Id,id.Parent,id.Number,Field(item,"title",200,true),Field(item,"site",300),
                    Field(item,"legacyStatus",100,true),Field(item,"legacyAuthorizationStatus",100,true),
                    Field(item,"authorizationEvidenceRef",200)));
            }
            var duplicates=rows.GroupBy(x=>x.LegacyProjectNumber.Trim(),StringComparer.OrdinalIgnoreCase).Count(g=>g.Count()>1);
            return new HistoricalProjectPackage(expectedSourceSha256,expectedCrmSha256,expectedProjectSha256,
                expectedCensusSha256,rows,inactive,duplicates);
        }
        catch(JsonException){throw new ArgumentException("Invalid historical JSON.");}
        catch(InvalidOperationException){throw new ArgumentException("Invalid historical JSON value.");}
    }
}
