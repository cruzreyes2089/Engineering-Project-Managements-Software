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
