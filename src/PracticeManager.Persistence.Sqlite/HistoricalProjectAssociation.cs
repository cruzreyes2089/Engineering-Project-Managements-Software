using System.Text.Json;
using PracticeManager.Core;

namespace PracticeManager.Persistence;

public sealed record HistoricalAssociationReview(string LegacyProjectId,long NativeProjectId,string NativeProjectNumber,
    string ArchiveTransferSha256,string NativeProposalSha256,bool TitleMatches,bool SiteMatches,bool SiteAvailable)
{
    public bool AssociationRecorded=>false;
    public bool WorkAuthorized=>false;
    public override string ToString()=>"[Confidential historical association review]";
}
public sealed partial class LocalPractice
{
    // A comparison only. Never create, persist, activate or amend an operational project.
    public HistoricalAssociationReview PreviewHistoricalAssociation(string legacyId,long nativeProjectId)
    {
        using var c=Open();using var t=c.BeginTransaction();
        RequireHistoricalImport(c,t);RequireProjectView(c,t);
        if(string.IsNullOrEmpty(legacyId)||legacyId.Length>100||
            legacyId.Any(ch=>!char.IsAsciiLetterOrDigit(ch)&&ch is not ('.' or '_' or '-'))||nativeProjectId<=0)
            throw new ArgumentException("Explicit historical and native identities required.");
        var row=HistoricalRows(c,t).SingleOrDefault(x=>string.Equals(x.Source.LegacyId,legacyId,StringComparison.Ordinal))
            ??throw new ArgumentException("Exact historical project identity unavailable.");
        var native=ProjectRow(c,t,nativeProjectId);
        if(native.Client!=row.NativeClientId)throw new ArgumentException("Historical and native client identities differ.");
        var archive=Scalar(c,t,"SELECT project_transfer_hash FROM historical_project_batches WHERE id=1") as string
            ??throw new InvalidOperationException("Historical archive evidence unavailable.");
        using var snapshot=JsonDocument.Parse(native.Snapshot);
        var title=snapshot.RootElement.GetProperty("Title").GetString();
        var site=snapshot.RootElement.GetProperty("Site").GetString();
        var siteAvailable=!string.IsNullOrWhiteSpace(row.Source.Site)&&!string.IsNullOrWhiteSpace(site);
        return new(legacyId,nativeProjectId,native.Number,archive,native.ProposalHash,
            string.Equals(row.Source.Title,title,StringComparison.Ordinal),
            siteAvailable&&string.Equals(row.Source.Site,site,StringComparison.Ordinal),siteAvailable);
    }
}
