using Microsoft.Data.Sqlite;
using PracticeManager.Core;

namespace PracticeManager.Persistence;
public sealed record HistoricalArchivePreview(string ProjectSha256,string CensusSha256,int Count,int DuplicateNumberGroups,string TargetStatus);
public sealed record HistoricalArchiveItem(long NativeClientId,HistoricalProjectRow Source)
{
    public bool WorkAuthorized=>false;
    public override string ToString()=>"[Confidential historical archive item]";
}
public sealed partial class LocalPractice
{
    private void RequireHistoricalView(SqliteConnection c,SqliteTransaction t)
    {
        Require(c,t,"HistoricalProjects.View");Require(c,t,"Clients.View");
    }
    private long RequireHistoricalImport(SqliteConnection c,SqliteTransaction t)
    {
        var actor=RequireAdministration(c,t);RequireHistoricalView(c,t);
        Require(c,t,"Administration.HistoricalProjectImport");return actor;
    }
    private static long HistoricalCount(SqliteConnection c,SqliteTransaction t,string table)
        =>Convert.ToInt64(Scalar(c,t,"SELECT count(*) FROM "+table));
    private static IReadOnlyList<HistoricalArchiveItem> HistoricalRows(SqliteConnection c,SqliteTransaction t)
    {
        var batches=HistoricalCount(c,t,"historical_project_batches");
        var seals=HistoricalCount(c,t,"historical_project_seals");
        var count=HistoricalCount(c,t,"historical_project_rows");
        if(batches==0&&seals==0&&count==0)return Array.Empty<HistoricalArchiveItem>();
        if(batches!=1||seals!=1||count is <1 or >10000||
            Convert.ToInt64(Scalar(c,t,"SELECT expected_count FROM historical_project_batches WHERE id=1"))!=count)
            throw new InvalidOperationException("Incomplete historical archive.");
        var result=new List<HistoricalArchiveItem>();
        using(var q=Command(c,t,@"SELECT native_client_id,legacy_id,legacy_client_id,legacy_number,title,site,
            legacy_status,legacy_authorization_status,authorization_evidence_ref FROM historical_project_register
            ORDER BY legacy_id COLLATE NOCASE LIMIT 10001"))
        using(var r=q.ExecuteReader())
            while(r.Read())result.Add(new(r.GetInt64(0),new(r.GetString(1),r.GetString(2),r.GetString(3),
                r.GetString(4),r.GetString(5),r.GetString(6),r.GetString(7),r.GetString(8))));
        if(result.Count!=count)throw new InvalidOperationException("Historical archive reconciliation failed.");
        return result.AsReadOnly();
    }
    private static void VerifyHistoricalParents(SqliteConnection c,SqliteTransaction t,HistoricalProjectPackage p)
    {
        var history=LegacyHistory(c,t);
        if(history==null||!string.Equals(history.Sha256,p.CrmTransferSha256,StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Matching imported CRM batch required.");
        var ids=history.Mappings.Where(x=>x.Kind=="Client").Select(x=>x.LegacyId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if(p.Rows.Any(x=>!ids.Contains(x.LegacyClientId)))throw new ArgumentException("Historical parent mapping unavailable.");
    }
    private static string HistoricalTarget(SqliteConnection c,SqliteTransaction t,HistoricalProjectPackage p)
    {
        VerifyHistoricalParents(c,t,p);
        var rows=HistoricalRows(c,t);
        if(rows.Count==0)return "ReadyForExplicitReview";
        bool exact;
        using(var q=Command(c,t,"SELECT source_hash,crm_transfer_hash,project_transfer_hash,census_hash FROM historical_project_batches WHERE id=1"))
        using(var r=q.ExecuteReader())
            exact=r.Read()&&r.GetString(0)==p.SourceSha256&&r.GetString(1)==p.CrmTransferSha256&&
                r.GetString(2)==p.ProjectTransferSha256&&r.GetString(3)==p.CensusSha256;
        if(!exact)return "DifferentPackageAlreadyImported";
        var expected=p.Rows.ToDictionary(x=>x.LegacyId,StringComparer.Ordinal);
        if(rows.Count!=expected.Count||rows.Any(x=>!expected.TryGetValue(x.Source.LegacyId,out var value)||value!=x.Source))
            throw new InvalidOperationException("Historical replay evidence mismatch.");
        return "AlreadyImported";
    }
    public HistoricalArchivePreview PreviewHistoricalProjects(byte[] crm,byte[] projects,byte[] census,
        string sourceHash,string crmHash,string projectHash,string censusHash)
    {
        using var c=Open();using var t=c.BeginTransaction();RequireHistoricalImport(c,t);
        var p=HistoricalProjectTransfer.Parse(crm,projects,census,sourceHash,crmHash,projectHash,censusHash);
        return new(p.ProjectTransferSha256,p.CensusSha256,p.Rows.Count,p.DuplicateNumberGroups,HistoricalTarget(c,t,p));
    }
    public string ImportHistoricalProjects(byte[] crm,byte[] projects,byte[] census,
        string sourceHash,string crmHash,string projectHash,string censusHash,bool confirmed,string reviewNote)
    {
        using var c=Open();using var t=c.BeginTransaction(deferred:false);var actor=RequireHistoricalImport(c,t);
        if(!confirmed)throw new ArgumentException("Explicit historical archive review required.");
        var note=Validation.Text(reviewNote,1000,"Historical migration review",true);
        var p=HistoricalProjectTransfer.Parse(crm,projects,census,sourceHash,crmHash,projectHash,censusHash);
        var status=HistoricalTarget(c,t,p);
        if(status=="AlreadyImported")return status;
        if(status!="ReadyForExplicitReview")throw new ArgumentException("Historical archive already contains another package.");
        Exec(c,t,@"INSERT INTO historical_project_batches(id,crm_batch_id,crm_transfer_hash,source_hash,project_transfer_hash,census_hash,expected_count,review_note,imported_by,imported_utc)
            VALUES(1,1,$crm,$s,$p,$c,$n,$r,$a,$u)",("$crm",p.CrmTransferSha256),("$s",p.SourceSha256),
            ("$p",p.ProjectTransferSha256),("$c",p.CensusSha256),("$n",p.Rows.Count),("$r",note),("$a",actor),("$u",UtcNow()));
        foreach(var row in p.Rows)
            Exec(c,t,@"INSERT INTO historical_project_rows(batch_id,legacy_id,legacy_client_id,legacy_number,title,site,legacy_status,legacy_authorization_status,authorization_evidence_ref)
                VALUES(1,$i,$c,$n,$t,$s,$l,$a,$e)",("$i",row.LegacyId),("$c",row.LegacyClientId),("$n",row.LegacyProjectNumber),
                ("$t",row.Title),("$s",row.Site),("$l",row.LegacyStatus),("$a",row.LegacyAuthorizationStatus),("$e",row.AuthorizationEvidenceRef));
        Exec(c,t,"INSERT INTO historical_project_seals(batch_id,sealed_utc) VALUES(1,$u)",("$u",UtcNow()));
        if(HistoricalTarget(c,t,p)!="AlreadyImported")throw new InvalidOperationException("Historical import evidence mismatch.");
        Audit(c,t,actor,"ImportHistoricalProjects","HistoricalProjectBatch",1);t.Commit();return "Imported";
    }
    public bool CanImportHistoricalProjects()
    {
        using var c=Open();using var t=c.BeginTransaction();
        try{RequireHistoricalImport(c,t);return true;}catch(UnauthorizedAccessException){return false;}
    }
    public IReadOnlyList<HistoricalArchiveItem> ReadHistoricalProjects()
    {
        using var c=Open();using var t=c.BeginTransaction();RequireHistoricalView(c,t);
        var rows=HistoricalRows(c,t);
        try{RequireHistoricalImport(c,t);return rows;}
        catch(UnauthorizedAccessException)
        {
            return Array.AsReadOnly(rows.Select(x=>x with
                {Source=x.Source with {AuthorizationEvidenceRef=""}}).ToArray());
        }
    }
}
