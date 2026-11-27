using Microsoft.Data.Sqlite;
using PracticeManager.Core;
using PracticeManager.Persistence;

internal static class HistoricalArchiveServiceTests
{
    public static void Run(string path,byte[] crm,byte[] projects,byte[] census,HistoricalProjectPackage p,Action<bool,string> check)
    {
        const string password="Synthetic-test-passphrase-0001";
        var s=new LocalPractice(path);
        s.Bootstrap("Synthetic archive","admin",password,"CL");
        void Sql(string sql)
        {
            using var c=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=path,Pooling=false}.ToString());
            c.Open();using var q=c.CreateCommand();q.CommandText=sql;q.ExecuteNonQuery();
        }
        long Count(string sql)
        {
            using var c=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=path,Pooling=false}.ToString());
            c.Open();using var q=c.CreateCommand();q.CommandText=sql;return Convert.ToInt64(q.ExecuteScalar());
        }
        void Reject<T>(Action a,string label) where T:Exception
        {
            try{a();}catch(T){check(true,label);return;}check(false,label);
        }
        HistoricalArchivePreview Preview()=>s.PreviewHistoricalProjects(crm,projects,census,p.SourceSha256,p.CrmTransferSha256,p.ProjectTransferSha256,p.CensusSha256);
        string Import(bool confirmed=true)=>s.ImportHistoricalProjects(crm,projects,census,p.SourceSha256,p.CrmTransferSha256,p.ProjectTransferSha256,p.CensusSha256,confirmed,"Synthetic review");
        Reject<UnauthorizedAccessException>(()=>s.ReadHistoricalProjects(),"Archive anonymous access denied");
        check(s.Login("admin",password),"Archive admin login");
        check(Count("PRAGMA user_version")==47,"Archive candidate schema version");
        Reject<ArgumentException>(()=>Preview(),"Archive requires matching imported CRM batch");
        s.ImportLegacyCrm(crm,p.CrmTransferSha256.ToUpperInvariant(),true,"Synthetic CRM");
        check(Preview().TargetStatus=="ReadyForExplicitReview"&&s.ReadHistoricalProjects().Count==0,"Archive preview read-only");
        Reject<ArgumentException>(()=>Import(false),"Archive explicit confirmation required");
        Sql("UPDATE role_permissions SET allowed=0 WHERE permission_code='Administration.HistoricalProjectImport' AND role_id=1");
        Reject<UnauthorizedAccessException>(()=>Import(),"Archive revoked import grant enforced");
        Sql("UPDATE role_permissions SET allowed=1 WHERE permission_code='Administration.HistoricalProjectImport' AND role_id=1");
        Sql("UPDATE role_permissions SET allowed=0 WHERE permission_code='HistoricalProjects.View' AND role_id=1");
        Reject<UnauthorizedAccessException>(()=>s.ReadHistoricalProjects(),"Archive revoked read grant enforced");
        Reject<UnauthorizedAccessException>(()=>Import(),"Archive import requires view grant");
        Sql("UPDATE role_permissions SET allowed=1 WHERE permission_code='HistoricalProjects.View' AND role_id=1");
        var audit=Count("SELECT count(*) FROM audit_events");
        var sequence=Count("SELECT next_value FROM sequences WHERE name='Project'");
        Sql("CREATE TRIGGER synthetic_abort_archive BEFORE INSERT ON historical_project_seals BEGIN SELECT RAISE(ABORT,'Synthetic failure'); END");
        Reject<SqliteException>(()=>Import(),"Archive injected seal failure");
        check(Count("SELECT (SELECT count(*) FROM historical_project_batches)+(SELECT count(*) FROM historical_project_rows)+(SELECT count(*) FROM historical_project_seals)")==0&&
            Count("SELECT count(*) FROM audit_events")==audit,"Archive failed transaction rolled back");
        Sql("DROP TRIGGER synthetic_abort_archive");
        check(Import()=="Imported","Archive sealed import committed");
        var rows=s.ReadHistoricalProjects();
        check(rows.Count==p.Rows.Count&&rows.All(x=>!x.WorkAuthorized)&&rows.Select(x=>x.Source).ToHashSet().SetEquals(p.Rows),"Archive exact stored historical values");
        check(Count("SELECT count(*) FROM projects")==0&&Count("SELECT count(*) FROM engagements")==0&&
            Count("SELECT next_value FROM sequences WHERE name='Project'")==sequence,"Archive does not create operational projects or consume numbers");
        audit=Count("SELECT count(*) FROM audit_events");
        check(Preview().TargetStatus=="AlreadyImported"&&Import()=="AlreadyImported"&&Count("SELECT count(*) FROM audit_events")==audit,"Archive replay is read-only");
        Reject<SqliteException>(()=>Sql("UPDATE historical_project_rows SET title='Changed'"),"Archive historical rows immutable");
        s.Logout();
        Reject<UnauthorizedAccessException>(()=>s.ReadHistoricalProjects(),"Archive logout enforced");
    }
}
