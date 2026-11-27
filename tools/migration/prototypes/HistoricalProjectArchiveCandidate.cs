// INTEGRATION CANDIDATE ONLY: outside the production project and not compiled.
// Requires approved archive schema, seeded permissions, native tests and recovery qualification.
using Microsoft.Data.Sqlite;
using PracticeManager.Core;

namespace PracticeManager.Persistence;

public sealed record HistoricalArchivePreview(int Projects,int InactiveSourceClientProjects,int DuplicateNumberGroups,
    string SourceSha256,string CrmTransferSha256,string ProjectTransferSha256,string CensusSha256,string TargetStatus)
{
    public bool WorkAuthorized=>false;
    public bool SourceAuthenticityVerified=>false;
}
public sealed record HistoricalArchiveImportResult(string Outcome,string ProjectTransferSha256,int Projects)
{
    public bool WorkAuthorized=>false;
}
public sealed record HistoricalArchiveEntry(string LegacyId,string LegacyClientId,long NativeClientId,
    string HistoricalNumber,string Title,string Site,string HistoricalStatus,string HistoricalAuthorizationStatus)
{
    public bool WorkAuthorized=>false;
    public override string ToString()=>"[Confidential historical archive entry]";
}
public sealed partial class LocalPractice
{
    private const string ArchiveBatchQuery="SELECT source_hash,crm_transfer_hash,project_transfer_hash,census_hash,expected_count FROM historical_project_batches WHERE id=1";
    private const string ArchiveCountsQuery="SELECT (SELECT count(*) FROM historical_project_rows),(SELECT count(*) FROM historical_project_seals),(SELECT count(*) FROM historical_project_batches)";
    private const string ArchiveRowsQuery="SELECT legacy_id,legacy_client_id,native_client_id,legacy_number,title,site,legacy_status,legacy_authorization_status,authorization_evidence_ref,work_authorized FROM historical_project_register WHERE batch_id=1 ORDER BY legacy_id COLLATE NOCASE LIMIT 10001";
    private const string ArchiveInsertBatch="INSERT INTO historical_project_batches(id,crm_batch_id,source_hash,crm_transfer_hash,project_transfer_hash,census_hash,expected_count,review_note,imported_by,imported_utc) VALUES(1,1,$s,$c,$p,$h,$n,$r,$a,$u)";
    private const string ArchiveInsertRow="INSERT INTO historical_project_rows(batch_id,legacy_id,legacy_client_id,legacy_number,title,site,legacy_status,legacy_authorization_status,authorization_evidence_ref) VALUES(1,$i,$c,$n,$t,$s,$l,$a,$e)";
    private const string ArchiveInsertSeal="INSERT INTO historical_project_seals(batch_id,sealed_utc) VALUES(1,$u)";
    private sealed record ArchiveStoredRow(HistoricalProjectRow Source,long NativeClientId);
    private sealed record ArchiveStored(string SourceHash,string CrmHash,string ProjectHash,string CensusHash,
        IReadOnlyList<ArchiveStoredRow> Rows);

    private long RequireHistoricalArchiveImport(SqliteConnection c,SqliteTransaction t)
    {
        var actor=RequireAdministration(c,t);
        foreach(var grant in new[]{"Administration.HistoricalProjectImport","HistoricalProjects.View","Clients.View"})
            Require(c,t,grant);
        return actor;
    }
    private static Dictionary<string,long> ArchiveClientMap(SqliteConnection c,SqliteTransaction t,string crmHash)
    {
        var history=LegacyHistory(c,t)??throw new ArgumentException("Reviewed CRM import required first.");
        if(!string.Equals(history.Sha256,crmHash,StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Historical package does not match imported CRM bytes.");
        return history.Mappings.Where(x=>x.Kind=="Client")
            .ToDictionary(x=>x.LegacyId,x=>x.NativeId,StringComparer.OrdinalIgnoreCase);
    }
    private static ArchiveStored? ReadArchiveStored(SqliteConnection c,SqliteTransaction t)
    {
        string? source=null,crm=null,project=null,census=null;int expected=0;
        using(var q=Command(c,t,ArchiveBatchQuery))
        using(var r=q.ExecuteReader())
            if(r.Read()){source=r.GetString(0);crm=r.GetString(1);project=r.GetString(2);census=r.GetString(3);expected=r.GetInt32(4);}
        using(var q=Command(c,t,ArchiveCountsQuery))
        using(var r=q.ExecuteReader())
        {
            if(!r.Read())throw new InvalidOperationException("Historical archive counts unavailable.");
            if(source is null)
            {
                if(r.GetInt64(0)!=0||r.GetInt64(1)!=0||r.GetInt64(2)!=0)
                    throw new InvalidOperationException("Incomplete historical archive.");
                return null;
            }
            if(expected is <1 or >10000||r.GetInt64(0)!=expected||r.GetInt64(1)!=1||r.GetInt64(2)!=1)
                throw new InvalidOperationException("Incomplete historical archive.");
        }
        var map=ArchiveClientMap(c,t,crm!);
        var rows=new List<ArchiveStoredRow>();
        using(var q=Command(c,t,ArchiveRowsQuery))
        using(var r=q.ExecuteReader())
            while(r.Read())
            {
                var row=new HistoricalProjectRow(r.GetString(0),r.GetString(1),r.GetString(3),r.GetString(4),
                    r.GetString(5),r.GetString(6),r.GetString(7),r.GetString(8));
                if(r.GetInt64(9)!=0||!map.TryGetValue(row.LegacyClientId,out var native)||native!=r.GetInt64(2))
                    throw new InvalidOperationException("Historical archive mapping mismatch.");
                rows.Add(new(row,native));
            }
        if(rows.Count!=expected||rows.Select(x=>x.Source.LegacyId).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=expected)
            throw new InvalidOperationException("Historical archive row evidence incomplete.");
        return new(source,crm!,project!,census!,rows.AsReadOnly());
    }
    private static string ArchiveTarget(SqliteConnection c,SqliteTransaction t,HistoricalProjectPackage p)
    {
        var map=ArchiveClientMap(c,t,p.CrmTransferSha256);
        if(p.Rows.Any(x=>!map.ContainsKey(x.LegacyClientId)))
            throw new ArgumentException("Historical parent not mapped.");
        var existing=ReadArchiveStored(c,t);
        if(existing is null)return "ReadyForExplicitReview";
        if(existing.SourceHash!=p.SourceSha256||existing.CrmHash!=p.CrmTransferSha256||
            existing.ProjectHash!=p.ProjectTransferSha256||existing.CensusHash!=p.CensusSha256)
            return "DifferentPackageAlreadyImported";
        var expected=p.Rows.ToDictionary(x=>x.LegacyId,StringComparer.Ordinal);
        if(existing.Rows.Count!=expected.Count||existing.Rows.Any(x=>
            !expected.TryGetValue(x.Source.LegacyId,out var row)||row!=x.Source||
            map[x.Source.LegacyClientId]!=x.NativeClientId))
            throw new InvalidOperationException("Historical archive differs from exact package.");
        return "AlreadyImported";
    }
    public HistoricalArchivePreview PreviewHistoricalProjects(byte[] crm,byte[] projects,byte[] census,
        string sourceHash,string crmHash,string projectHash,string censusHash)
    {
        using var c=Open();using var t=c.BeginTransaction();RequireHistoricalArchiveImport(c,t);
        var p=HistoricalProjectTransfer.Parse(crm,projects,census,sourceHash,crmHash,projectHash,censusHash);
        return new(p.Rows.Count,p.InactiveClientProjectCount,p.DuplicateNumberGroups,p.SourceSha256,
            p.CrmTransferSha256,p.ProjectTransferSha256,p.CensusSha256,ArchiveTarget(c,t,p));
    }
    public HistoricalArchiveImportResult ImportHistoricalProjects(byte[] crm,byte[] projects,byte[] census,
        string sourceHash,string crmHash,string projectHash,string censusHash,bool explicitlyConfirmed,string reviewNote)
    {
        using var c=Open();using var t=c.BeginTransaction(deferred:false);var actor=RequireHistoricalArchiveImport(c,t);
        if(!explicitlyConfirmed)throw new ArgumentException("Explicit historical archive confirmation required.");
        var note=Validation.Text(reviewNote,1000,"Historical mapping review",true);
        var p=HistoricalProjectTransfer.Parse(crm,projects,census,sourceHash,crmHash,projectHash,censusHash);
        var target=ArchiveTarget(c,t,p);
        if(target=="AlreadyImported")return new(target,p.ProjectTransferSha256,p.Rows.Count);
        if(target!="ReadyForExplicitReview")throw new ArgumentException("Historical archive does not support merge or replacement.");
        if(Exec(c,t,ArchiveInsertBatch,("$s",p.SourceSha256),("$c",p.CrmTransferSha256),("$p",p.ProjectTransferSha256),
            ("$h",p.CensusSha256),("$n",p.Rows.Count),("$r",note),("$a",actor),("$u",UtcNow()))!=1)
            throw new InvalidOperationException("Historical archive batch write failed.");
        foreach(var row in p.Rows)
            if(Exec(c,t,ArchiveInsertRow,("$i",row.LegacyId),("$c",row.LegacyClientId),("$n",row.LegacyProjectNumber),
                ("$t",row.Title),("$s",row.Site),("$l",row.LegacyStatus),("$a",row.LegacyAuthorizationStatus),
                ("$e",row.AuthorizationEvidenceRef))!=1)
                throw new InvalidOperationException("Historical archive row write failed.");
        if(Exec(c,t,ArchiveInsertSeal,("$u",UtcNow()))!=1||ArchiveTarget(c,t,p)!="AlreadyImported")
            throw new InvalidOperationException("Historical archive seal/readback failed.");
        Audit(c,t,actor,"CommitHistoricalProjectArchive","Firm",1);
        t.Commit();
        return new("Imported",p.ProjectTransferSha256,p.Rows.Count);
    }
    public IReadOnlyList<HistoricalArchiveEntry> ReadHistoricalProjects()
    {
        using var c=Open();using var t=c.BeginTransaction();
        Require(c,t,"HistoricalProjects.View");Require(c,t,"Clients.View");
        var stored=ReadArchiveStored(c,t);
        if(stored is null)return Array.Empty<HistoricalArchiveEntry>();
        return Array.AsReadOnly(stored.Rows.Select(x=>new HistoricalArchiveEntry(x.Source.LegacyId,x.Source.LegacyClientId,
            x.NativeClientId,x.Source.LegacyProjectNumber,x.Source.Title,x.Source.Site,
            x.Source.LegacyStatus,x.Source.LegacyAuthorizationStatus)).ToArray());
    }
}
