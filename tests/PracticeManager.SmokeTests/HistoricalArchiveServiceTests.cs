using Microsoft.Data.Sqlite;
using System.Security.Cryptography;
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
        Sql("CREATE TRIGGER synthetic_abort_archive_audit BEFORE INSERT ON audit_events WHEN NEW.action='ImportHistoricalProjects' BEGIN SELECT RAISE(ABORT,'Synthetic audit failure'); END");
        Reject<SqliteException>(()=>Import(),"Archive injected audit failure after sealing");
        check(Count("SELECT (SELECT count(*) FROM historical_project_batches)+(SELECT count(*) FROM historical_project_rows)+(SELECT count(*) FROM historical_project_seals)")==0&&
            Count("SELECT count(*) FROM audit_events")==audit,"Archive failed audit rolls back batch rows and seal");
        Sql("DROP TRIGGER synthetic_abort_archive_audit");
        // Independent sessions and connections compete for the same reviewed bytes.
        var second=new LocalPractice(path);
        check(second.Login("admin",password),"Archive independent concurrent session");
        using(var start=new ManualResetEventSlim(false))
        {
            var a=Task.Run(()=>{start.Wait();return Import();});
            var b=Task.Run(()=>{start.Wait();return second.ImportHistoricalProjects(crm,projects,census,
                p.SourceSha256,p.CrmTransferSha256,p.ProjectTransferSha256,p.CensusSha256,true,"Synthetic concurrent review");});
            start.Set();
            check(Task.WaitAll(new Task[]{a,b},TimeSpan.FromSeconds(30)),"Archive concurrent attempts complete");
            check(new[]{a.Result,b.Result}.OrderBy(x=>x,StringComparer.Ordinal).SequenceEqual(new[]{"AlreadyImported","Imported"}),
                "Archive concurrent exact imports commit once");
        }
        second.Logout();
        check(Count("SELECT count(*) FROM audit_events WHERE action='ImportHistoricalProjects'")==1,"Archive single commit audit");
        var rows=s.ReadHistoricalProjects();
        check(rows.Count==p.Rows.Count&&rows.All(x=>!x.WorkAuthorized)&&rows.Select(x=>x.Source).ToHashSet().SetEquals(p.Rows),"Archive exact stored historical values");
        check(Count("SELECT count(*) FROM projects")==0&&Count("SELECT count(*) FROM engagements")==0&&
            Count("SELECT next_value FROM sequences WHERE name='Project'")==sequence,"Archive does not create operational projects or consume numbers");
        audit=Count("SELECT count(*) FROM audit_events");
        check(Preview().TargetStatus=="AlreadyImported"&&Import()=="AlreadyImported"&&Count("SELECT count(*) FROM audit_events")==audit,"Archive replay is read-only");
        Reject<SqliteException>(()=>Sql("UPDATE historical_project_rows SET title='Changed'"),"Archive historical rows immutable");

        var changedCensus=census.Concat(new byte[]{32}).ToArray();
        var changedHash=Convert.ToHexString(SHA256.HashData(changedCensus)).ToLowerInvariant();
        check(s.PreviewHistoricalProjects(crm,projects,changedCensus,p.SourceSha256,p.CrmTransferSha256,p.ProjectTransferSha256,changedHash).TargetStatus=="DifferentPackageAlreadyImported",
            "Archive valid changed census cannot replace original");
        Reject<ArgumentException>(()=>s.ImportHistoricalProjects(crm,projects,changedCensus,p.SourceSha256,p.CrmTransferSha256,p.ProjectTransferSha256,changedHash,true,"Synthetic changed package"),
            "Archive changed exact package import denied");
        check(Count("SELECT count(*) FROM audit_events")==audit,"Archive changed package creates no audit or write");
        HistoricalArchiveRecoveryTests.Run(s,path,password,p,check);
        check(s.CanImportHistoricalProjects(),"Archive import administrator capability");
        check(rows.Any(x=>!string.IsNullOrEmpty(x.Source.AuthorizationEvidenceRef)),"Archive reviewed fixture contains restricted evidence");
        Sql("UPDATE role_permissions SET allowed=0 WHERE permission_code='Administration.HistoricalProjectImport' AND role_id=1");
        check(!s.CanImportHistoricalProjects(),"Archive revoked capability denied");
        var restricted=s.ReadHistoricalProjects();
        check(restricted.Count==rows.Count&&restricted.All(x=>x.Source.AuthorizationEvidenceRef=="")&&restricted.All(x=>!x.WorkAuthorized),"Archive view-only projection redacts evidence");
        Reject<UnauthorizedAccessException>(()=>Preview(),"Archive view-only preview denied");
        Sql("UPDATE role_permissions SET allowed=1 WHERE permission_code='Administration.HistoricalProjectImport' AND role_id=1");
        check(s.ReadHistoricalProjects().Select(x=>x.Source).ToHashSet().SetEquals(p.Rows),"Archive restored grant recovers restricted projection");
        Sql("UPDATE role_permissions SET allowed=0 WHERE permission_code='Clients.View' AND role_id=1");
        Reject<UnauthorizedAccessException>(()=>s.ReadHistoricalProjects(),"Archive requires client view permission");
        check(!s.CanImportHistoricalProjects(),"Archive client-view revocation denies import capability");
        Sql("UPDATE role_permissions SET allowed=1 WHERE permission_code='Clients.View' AND role_id=1");

        // Exercise ordinary staff through the actual account/password/grant APIs.
        var role=s.ReadStaffRoles().Single(x=>x.Name=="Engineer").Id;
        const string temporary="Synthetic-staff-temporary-0001", permanent="Synthetic-staff-permanent-0002";
        var staffId=s.CreateStaffAccount("archive-staff",role,temporary);
        var staff=new LocalPractice(path);
        check(!staff.Login("archive-staff",temporary),"Archive staff required password change blocks login");
        Reject<UnauthorizedAccessException>(()=>staff.ReadHistoricalProjects(),"Archive staff unauthenticated view denied");
        check(staff.ChangeRequiredPassword("archive-staff",temporary,permanent),"Archive staff required password change completed");
        check(staff.Login("archive-staff",permanent),"Archive ordinary staff login");
        Reject<UnauthorizedAccessException>(()=>staff.ReadHistoricalProjects(),"Archive staff default view denied");
        s.SetStaffGrant(role,"HistoricalProjects.View",true,false);
        Reject<UnauthorizedAccessException>(()=>staff.ReadHistoricalProjects(),"Archive staff requires client view too");
        s.SetStaffGrant(role,"Clients.View",true,false);
        check(staff.ReadHistoricalProjects().Count==p.Rows.Count&&staff.ReadHistoricalProjects().All(x=>x.Source.AuthorizationEvidenceRef==""&&!x.WorkAuthorized),
            "Archive delegated staff view omits restricted evidence");
        // Even granting the import code to non-admin cannot confer admin authority.
        s.SetStaffGrant(role,"Administration.HistoricalProjectImport",true,false);
        check(!staff.CanImportHistoricalProjects(),"Archive nonadministrator cannot import despite explicit grant");
        Reject<UnauthorizedAccessException>(()=>staff.PreviewHistoricalProjects(crm,projects,census,p.SourceSha256,p.CrmTransferSha256,p.ProjectTransferSha256,p.CensusSha256),
            "Archive nonadministrator preview denied");
        s.SetStaffGrant(role,"HistoricalProjects.View",false,true);
        Reject<UnauthorizedAccessException>(()=>staff.ReadHistoricalProjects(),"Archive staff revocation immediately enforced");
        s.SetStaffGrant(role,"HistoricalProjects.View",true,false);
        var account=s.ReadStaffAccounts().Single(x=>x.Id==staffId);
        s.ResetStaffPassword(staffId,account.Revision,temporary);
        Reject<UnauthorizedAccessException>(()=>staff.ReadHistoricalProjects(),"Archive credential revision invalidates existing session");
        staff.Logout();
        s.Logout();
        Reject<UnauthorizedAccessException>(()=>s.ReadHistoricalProjects(),"Archive logout enforced");
    }
}
