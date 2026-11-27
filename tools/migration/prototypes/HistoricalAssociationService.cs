// Integration candidate only. Outside the application project; not compiled or registered.
// Requires a separately numbered, verified association migration.
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PracticeManager.Core;

namespace PracticeManager.Persistence;

public sealed record HistoricalAssociationCommitReview(
    long ReviewerId, string LegacyId, long NativeProjectId, string NativeProjectNumber,
    string ArchiveTransferHash, string ProposalHash, string IntegrityDigest,
    bool TitleMatches, bool SiteMatches, bool SiteAvailable)
{
    public bool WorkAuthorized => false;
    public override string ToString() => "[Confidential association review]";
}

public sealed record HistoricalAssociationReversalReview(
    long ReviewerId, long AssociationId, string LegacyId, long NativeProjectId,
    string IntegrityDigest)
{
    public bool WorkAuthorized => false;
    public override string ToString() => "[Confidential association reversal review]";
}

public sealed record HistoricalAssociationProjectChoice(long ProjectId,string ProjectNumber,string Title)
{
    public string Label => ProjectNumber+" — "+Title;
    public override string ToString()=>"[Confidential project choice]";
}
public sealed record HistoricalAssociationHistoryItem(long Id,string LegacyId,long NativeProjectId,
    string NativeProjectNumber,string RecordedUtc,string ReviewNote,string? ReversedUtc,string? ReversalNote)
{
    public bool IsActive=>ReversedUtc==null;
    public bool WorkAuthorized=>false;
    public override string ToString()=>"[Confidential historical association]";
}

public sealed partial class LocalPractice
{
    private const int MaxAssociationHistoryRows = 100000;

    // Same transaction as all evidence reads. Separate versioned native format;
    // no claim of byte-for-byte parity with the Python reference fingerprint.
    internal static (int ArchiveCount, long AssociationCount, long ReversalCount, string Digest)
        HistoricalAssociationIntegrity(SqliteConnection c, SqliteTransaction t)
    {
        var archive = HistoricalArchiveFingerprint(c,t);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes("CRC-native-association-integrity-v1\n"));
        hash.AppendData(JsonSerializer.SerializeToUtf8Bytes(new object[]{archive.Count,archive.Digest}));
        hash.AppendData(new byte[]{10});

        long Scan(string sql)
        {
            hash.AppendData(Encoding.UTF8.GetBytes(sql+"\n"));
            using var q=Command(c,t,sql);
            using var r=q.ExecuteReader();
            long count=0;
            while(r.Read())
            {
                if(++count>MaxAssociationHistoryRows)
                    throw new InvalidOperationException("Association history exceeds reviewed integrity scan limit.");
                var fields=new object[r.FieldCount];
                r.GetValues(fields);
                hash.AppendData(JsonSerializer.SerializeToUtf8Bytes(fields));
                hash.AppendData(new byte[]{10});
            }
            return count;
        }

        var links=Scan("SELECT id,batch_id,legacy_id,native_project_id,archive_hash,proposal_hash,review_note,actor_id,recorded_utc FROM historical_associations ORDER BY id");
        var reversals=Scan("SELECT association_id,review_note,actor_id,recorded_utc FROM historical_association_reversals ORDER BY association_id");
        if(reversals>links||(archive.Count==0&&links!=0))
            throw new InvalidOperationException("Historical association history is inconsistent.");
        return (archive.Count,links,reversals,Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant());
    }

    public bool CanReviewHistoricalAssociations()
    {
        using var c=Open();using var t=c.BeginTransaction();
        try{RequireHistoricalImport(c,t);RequireProjectView(c,t);return true;}
        catch(UnauthorizedAccessException){return false;}
    }

    // A bounded, explicit same-client search; never return a candidate based on fuzzy identity.
    public IReadOnlyList<HistoricalAssociationProjectChoice> FindHistoricalAssociationProjects(string legacyId,string projectNumber)
    {
        using var c=Open();using var t=c.BeginTransaction();
        RequireHistoricalImport(c,t);RequireProjectView(c,t);
        var number=Validation.Text(projectNumber,100,"Exact native project number",true);
        var row=HistoricalRows(c,t).SingleOrDefault(x=>string.Equals(x.Source.LegacyId,legacyId,StringComparison.Ordinal))
            ??throw new ArgumentException("Exact historical project identity unavailable.");
        long id;
        using(var q=Command(c,t,@"SELECT p.id FROM projects p JOIN engagements e ON e.id=p.engagement_id
            JOIN proposals s ON s.id=e.proposal_id WHERE p.project_no=$n COLLATE BINARY AND s.client_id=$c
            AND s.state='Accepted' LIMIT 2",("$n",number),("$c",row.NativeClientId)))
        using(var reader=q.ExecuteReader())
        {
            if(!reader.Read())return Array.Empty<HistoricalAssociationProjectChoice>();
            id=reader.GetInt64(0);
            if(reader.Read())throw new InvalidOperationException("Ambiguous native project number.");
        }
        var p=ProjectRow(c,t,id);
        if(p.Client!=row.NativeClientId)throw new InvalidOperationException("Native project identity changed.");
        using var doc=JsonDocument.Parse(p.Snapshot);
        return Array.AsReadOnly(new[]{new HistoricalAssociationProjectChoice(id,p.Number,
            doc.RootElement.GetProperty("Title").GetString()??"")});
    }

    public IReadOnlyList<HistoricalAssociationHistoryItem> ReadHistoricalAssociationHistory(string legacyId)
    {
        using var c=Open();using var t=c.BeginTransaction();
        RequireHistoricalImport(c,t);RequireProjectView(c,t);
        if(!HistoricalRows(c,t).Any(x=>string.Equals(x.Source.LegacyId,legacyId,StringComparison.Ordinal)))
            throw new ArgumentException("Exact historical project identity unavailable.");
        var rows=new List<HistoricalAssociationHistoryItem>();
        using var q=Command(c,t,@"SELECT a.id,a.legacy_id,a.native_project_id,p.project_no,a.recorded_utc,
            a.review_note,r.recorded_utc,r.review_note FROM historical_associations a
            JOIN projects p ON p.id=a.native_project_id LEFT JOIN historical_association_reversals r ON r.association_id=a.id
            WHERE a.batch_id=1 AND a.legacy_id=$l COLLATE BINARY ORDER BY a.id DESC LIMIT 1001",("$l",legacyId));
        using var reader=q.ExecuteReader();
        while(reader.Read())
        {
            if(rows.Count==1000)throw new InvalidOperationException("Association history exceeds display limit.");
            rows.Add(new(reader.GetInt64(0),reader.GetString(1),reader.GetInt64(2),reader.GetString(3),
                reader.GetString(4),reader.GetString(5),reader.IsDBNull(6)?null:reader.GetString(6),
                reader.IsDBNull(7)?null:reader.GetString(7)));
        }
        return rows.AsReadOnly();
    }

    private static HistoricalAssociationCommitReview BuildAssociationReview(
        SqliteConnection c,SqliteTransaction t,long actor,string legacyId,long nativeProjectId)
    {
        if(string.IsNullOrEmpty(legacyId)||legacyId.Length>100||
           legacyId.Any(ch=>!char.IsAsciiLetterOrDigit(ch)&&ch is not ('.' or '_' or '-'))||nativeProjectId<=0)
            throw new ArgumentException("Explicit historical and native identities required.");
        var row=HistoricalRows(c,t).SingleOrDefault(x=>string.Equals(x.Source.LegacyId,legacyId,StringComparison.Ordinal))
            ??throw new ArgumentException("Exact historical project identity unavailable.");
        var native=ProjectRow(c,t,nativeProjectId);
        if(native.Client!=row.NativeClientId)
            throw new ArgumentException("Historical and native client identities differ.");
        var transfer=Scalar(c,t,"SELECT project_transfer_hash FROM historical_project_batches WHERE id=1") as string
            ??throw new InvalidOperationException("Historical archive evidence unavailable.");
        if(Convert.ToInt64(Scalar(c,t,@"SELECT count(*) FROM historical_active_associations
            WHERE (batch_id=1 AND legacy_id=$l COLLATE NOCASE) OR native_project_id=$p",
            ("$l",legacyId),("$p",nativeProjectId)))!=0)
            throw new ArgumentException("An active association already exists.");
        using var snapshot=JsonDocument.Parse(native.Snapshot);
        var title=snapshot.RootElement.GetProperty("Title").GetString();
        var site=snapshot.RootElement.GetProperty("Site").GetString();
        var siteAvailable=!string.IsNullOrWhiteSpace(row.Source.Site)&&!string.IsNullOrWhiteSpace(site);
        return new(actor,legacyId,nativeProjectId,native.Number,transfer,native.ProposalHash,
            HistoricalAssociationIntegrity(c,t).Digest,
            string.Equals(row.Source.Title,title,StringComparison.Ordinal),
            siteAvailable&&string.Equals(row.Source.Site,site,StringComparison.Ordinal),siteAvailable);
    }

    public HistoricalAssociationCommitReview PrepareHistoricalAssociation(string legacyId,long nativeProjectId)
    {
        using var c=Open();using var t=c.BeginTransaction();
        var actor=RequireHistoricalImport(c,t);RequireProjectView(c,t);
        return BuildAssociationReview(c,t,actor,legacyId,nativeProjectId);
    }

    public long RecordHistoricalAssociation(HistoricalAssociationCommitReview expected,bool confirmed,string reviewNote)
    {
        ArgumentNullException.ThrowIfNull(expected);
        using var c=Open();using var t=c.BeginTransaction(deferred:false);
        var actor=RequireHistoricalImport(c,t);RequireProjectView(c,t);
        if(!confirmed)throw new ArgumentException("Explicit association review required.");
        var note=Validation.Text(reviewNote,1000,"Association review evidence",true);
        var current=BuildAssociationReview(c,t,actor,expected.LegacyId,expected.NativeProjectId);
        if(current!=expected)throw new ArgumentException("Association review changed. Refresh.");
        if(current.IntegrityDigest.Length!=64)
            throw new InvalidOperationException("Association integrity evidence unavailable.");
        Exec(c,t,@"INSERT INTO historical_associations(batch_id,legacy_id,native_project_id,archive_hash,proposal_hash,review_note,actor_id,recorded_utc)
            VALUES(1,$l,$p,$h,$s,$n,$a,$u)",
            ("$l",current.LegacyId),("$p",current.NativeProjectId),("$h",current.ArchiveTransferHash),
            ("$s",current.ProposalHash),("$n",note),("$a",actor),("$u",UtcNow()));
        var id=Last(c,t);
        Audit(c,t,actor,"RecordHistoricalAssociation","HistoricalAssociation",id);
        t.Commit();return id;
    }

    private static HistoricalAssociationReversalReview BuildReversalReview(
        SqliteConnection c,SqliteTransaction t,long actor,long associationId)
    {
        if(associationId<=0)throw new ArgumentException("An active association identity is required.");
        string legacy;long project;
        using(var q=Command(c,t,"SELECT legacy_id,native_project_id FROM historical_active_associations WHERE id=$id",("$id",associationId)))
        using(var r=q.ExecuteReader())
        {
            if(!r.Read())throw new ArgumentException("An active association is required.");
            legacy=r.GetString(0);project=r.GetInt64(1);
        }
        return new(actor,associationId,legacy,project,HistoricalAssociationIntegrity(c,t).Digest);
    }

    public HistoricalAssociationReversalReview PrepareHistoricalAssociationReversal(long associationId)
    {
        using var c=Open();using var t=c.BeginTransaction();
        var actor=RequireHistoricalImport(c,t);RequireProjectView(c,t);
        return BuildReversalReview(c,t,actor,associationId);
    }

    public void ReverseHistoricalAssociation(HistoricalAssociationReversalReview expected,bool confirmed,string reviewNote)
    {
        ArgumentNullException.ThrowIfNull(expected);
        using var c=Open();using var t=c.BeginTransaction(deferred:false);
        var actor=RequireHistoricalImport(c,t);RequireProjectView(c,t);
        if(!confirmed)throw new ArgumentException("Explicit reversal review required.");
        var note=Validation.Text(reviewNote,1000,"Association reversal evidence",true);
        if(BuildReversalReview(c,t,actor,expected.AssociationId)!=expected)
            throw new ArgumentException("Reversal review changed. Refresh.");
        Exec(c,t,@"INSERT INTO historical_association_reversals(association_id,review_note,actor_id,recorded_utc)
            VALUES($i,$n,$a,$u)",("$i",expected.AssociationId),("$n",note),("$a",actor),("$u",UtcNow()));
        Audit(c,t,actor,"ReverseHistoricalAssociation","HistoricalAssociation",expected.AssociationId);
        t.Commit();
    }
}
