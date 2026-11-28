using Microsoft.Data.Sqlite;
using PracticeManager.Persistence;

internal static class HistoricalAssociationTests
{
    public static void Run(LocalPractice s,string path,Action<bool,string> check)
    {
        // Only modify the already restored disposable fixture, never the original archive.
        var archive=s.ReadHistoricalProjects().Single(x=>x.Source.LegacyId=="P01");
        var other=s.ReadHistoricalProjects().Single(x=>x.Source.LegacyId=="P02");
        var proposal=s.CreateProposal(new(archive.NativeClientId,archive.Source.Title,"Synthetic site","Synthetic scope",100m));
        var hash=ConflictReviewTests.Issue(s,proposal,1);
        var native=s.RecordProposalDecision(proposal,hash,true,"Synthetic acceptance").ProjectId!.Value;
        long Count(string sql)
        {
            using var c=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=path,Pooling=false}.ToString());
            c.Open();using var q=c.CreateCommand();q.CommandText=sql;return Convert.ToInt64(q.ExecuteScalar());
        }
        void Sql(string sql)
        {
            using var c=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=path,Pooling=false}.ToString());
            c.Open();using var q=c.CreateCommand();q.CommandText=sql;q.ExecuteNonQuery();
        }
        void Reject<T>(Action action,string label) where T:Exception
        {
            try{action();}catch(T){check(true,label);return;}check(false,label);
        }
        var audit=Count("SELECT count(*) FROM audit_events");
        var before=s.ReadProjectHandoff(native);
        var comparison=s.PreviewHistoricalAssociation("P01",native);
        check(comparison.TitleMatches&&!comparison.SiteAvailable&&!comparison.SiteMatches&&
            comparison.NativeProposalSha256==hash&&!comparison.AssociationRecorded&&!comparison.WorkAuthorized,
            "Archive comparison preserves missing-site uncertainty and binds native snapshot");
        Reject<ArgumentException>(()=>s.PreviewHistoricalAssociation(other.Source.LegacyId,native),"Archive comparison rejects cross-client project");
        Reject<ArgumentException>(()=>s.PreviewHistoricalAssociation("p01",native),"Archive comparison rejects substituted identity spelling");
        Reject<ArgumentException>(()=>s.PreviewHistoricalAssociation("P01",long.MaxValue),"Archive comparison rejects nonexistent project");
        check(s.ReadProjectHandoff(native)==before&&Count("SELECT count(*) FROM audit_events")==audit,
            "Archive comparison changes neither project nor audit");
        check(s.AuthorizeProjectStart(native,hash,0,"",true,"Synthetic")=="AgreementEvidenceMismatch"&&
            s.ReadProjectHandoff(native).State=="Pending","Historical approval cannot substitute for signed agreement");
        Sql("UPDATE role_permissions SET allowed=0 WHERE role_id=1 AND permission_code='Projects.View'");
        Reject<UnauthorizedAccessException>(()=>s.PreviewHistoricalAssociation("P01",native),"Archive comparison requires native project permission");
        Sql("UPDATE role_permissions SET allowed=1 WHERE role_id=1 AND permission_code='Projects.View'");
        Sql("UPDATE role_permissions SET allowed=0 WHERE role_id=1 AND permission_code='Administration.HistoricalProjectImport'");
        Reject<UnauthorizedAccessException>(()=>s.PreviewHistoricalAssociation("P01",native),"Archive comparison requires import administrator permission");
        Sql("UPDATE role_permissions SET allowed=1 WHERE role_id=1 AND permission_code='Administration.HistoricalProjectImport'");
        check(s.ReadHistoricalProjects().Count==2&&Count("SELECT count(*) FROM projects")==1,
            "Archive comparison does not create projects or modify archive rows");
    }
}
