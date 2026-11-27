using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using PracticeManager.Core;

internal static class HistoricalProjectTransferTests
{
    private static string Digest(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static byte[] Bytes(JsonNode node)=>JsonSerializer.SerializeToUtf8Bytes(node);
    public static void Run(Action<bool,string> check)
    {
        var crm=SyntheticCrmNativeTests.Fixture(false);
        var source=new string('a',64);
        var crmHash=Digest(crm);
        var projects=new JsonObject
        {
            ["format"]="CRC-Legacy-Projects-v1",["sourceSha256"]=source,["crmTransferSha256"]=crmHash,
            ["projects"]=new JsonArray(
                new JsonObject{["legacyId"]="P01",["legacyClientId"]="001",["legacyProjectNumber"]="OLD-001",
                    ["title"]="Synthetic project",["site"]="",["legacyStatus"]="Active",
                    ["legacyAuthorizationStatus"]="Approved",["authorizationEvidenceRef"]="Ref-01"},
                new JsonObject{["legacyId"]="P02",["legacyClientId"]="002",["legacyProjectNumber"]="old-001",
                    ["title"]=" Historical title ",["site"]="Synthetic site",["legacyStatus"]="Closed",
                    ["legacyAuthorizationStatus"]="Unknown",["authorizationEvidenceRef"]=""})
        };
        JsonObject Census(JsonObject package,byte[] packageBytes)
        {
            var ids=new JsonArray();
            foreach(var item in package["projects"]!.AsArray())
                ids.Add(new JsonObject{["legacyId"]=item!["legacyId"]!.DeepClone(),
                    ["legacyClientId"]=item["legacyClientId"]!.DeepClone(),
                    ["legacyProjectNumber"]=item["legacyProjectNumber"]!.DeepClone()});
            return new JsonObject{["format"]="CRC-Legacy-Project-Census-v1",["sourceSha256"]=source,
                ["crmTransferSha256"]=crmHash,["projectTransferSha256"]=Digest(packageBytes),["projects"]=ids};
        }
        var raw=Bytes(projects);var census=Bytes(Census(projects,raw));
        HistoricalProjectPackage Parse(byte[] p,byte[] c,string? expectedProject=null,string? expectedCensus=null)
            =>HistoricalProjectTransfer.Parse(crm,p,c,source,crmHash,expectedProject??Digest(p),expectedCensus??Digest(c));
        void Invalid(Action a,string label)
        {
            try{a();}catch(ArgumentException){check(true,label);return;}
            check(false,label);
        }
        var accepted=Parse(raw,census);
        check(accepted.Rows.Count==2&&accepted.InactiveClientProjectCount==1&&accepted.DuplicateNumberGroups==1,
            "Historical archive counts, inactive parents and duplicate numbers");
        check(accepted.Rows[1].Title==" Historical title "&&accepted.Rows[0].LegacyAuthorizationStatus=="Approved"&&
            !accepted.WorkAuthorized&&!accepted.NativeProjectCreationAuthorized&&!accepted.HistoricalAuthorizationVerified,
            "Historical labels preserved without operational authority");
        check(accepted.SourceSha256==source&&accepted.CrmTransferSha256==crmHash&&
            accepted.ProjectTransferSha256==Digest(raw)&&accepted.CensusSha256==Digest(census),
            "Historical archive exact byte digests retained");
        check(!accepted.ToString().Contains("Synthetic project")&&!accepted.Rows[0].ToString().Contains("Ref-01"),
            "Historical archive default string representation redacted");
        Invalid(()=>Parse(raw,census,new string('0',64)),"Historical project hash mismatch denied");
        Invalid(()=>Parse(raw,census,null,new string('0',64)),"Historical census hash mismatch denied");
        var changed=projects.DeepClone().AsObject();
        changed["projects"]![0]!["legacyClientId"]="002";
        var changedBytes=Bytes(changed);
        var oldCensus=Census(projects,changedBytes);
        Invalid(()=>Parse(changedBytes,Bytes(oldCensus)),"Historical changed parent rejected even after rebinding");
        foreach(var change in new[]{"orphan","duplicate","financial","control","empty"})
        {
            var bad=projects.DeepClone().AsObject();
            var row=bad["projects"]![0]!.AsObject();
            switch(change)
            {
                case "orphan":row["legacyClientId"]="missing";break;
                case "duplicate":row["legacyId"]="p02";break;
                case "financial":row["fee"]=100;break;
                case "control":row["title"]="bad\nvalue";break;
                case "empty":bad["projects"]=new JsonArray();break;
            }
            var bytes=Bytes(bad);
            Invalid(()=>Parse(bytes,Bytes(Census(bad,bytes))),"Historical invalid package rejected: "+change);
        }
        var duplicate=System.Text.Encoding.UTF8.GetBytes(System.Text.Encoding.UTF8.GetString(raw).Replace(
            "\"legacyId\":\"P01\"","\"legacyId\":\"P01\",\"legacyId\":\"P01\""));
        Invalid(()=>Parse(duplicate,Bytes(Census(projects,duplicate))),"Historical duplicate JSON key denied");
        var reverse=Census(projects,raw);
        var items=reverse["projects"]!.AsArray();
        var first=items[0]!.DeepClone();items.RemoveAt(0);items.Add(first);
        check(Parse(raw,Bytes(reverse)).Rows.Count==2,"Historical census order independent");
    }
}
