using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using PracticeManager.Persistence;

internal static class HistoricalArchiveUpgradeTests
{
    // Constructed boundary fixture, not a database produced by the prior binary.
    // The separate exact schema-46 build remains a qualification prerequisite.
    public static void Run(string root,byte[] crm,Action<bool,string> check)
    {
        const string pw="Synthetic-upgrade-passphrase-0001";
        var source=Path.Combine(root,"historical-upgrade-v46.db");
        var s=new LocalPractice(source);
        s.Bootstrap("Synthetic upgrade","admin",pw,"UP");
        check(s.Login("admin",pw),"Archive boundary fixture admin login");
        var crmHash=Convert.ToHexString(SHA256.HashData(crm));
        s.ImportLegacyCrm(crm,crmHash,true,"Synthetic boundary CRM");
        var prior=s.ReadLegacyCrmHistory()!;
        var engineer=s.ReadStaffRoles().Single(x=>x.Name=="Engineer").Id;
        s.SetStaffGrant(engineer,"Clients.View",true,false);
        s.Logout();

        void Sql(string file,string sql)
        {
            using var c=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=file,Pooling=false,ForeignKeys=true}.ToString());
            c.Open();using var q=c.CreateCommand();q.CommandText=sql;q.ExecuteNonQuery();
        }
        long Count(string file,string sql)
        {
            using var c=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=file,Mode=SqliteOpenMode.ReadOnly,Pooling=false}.ToString());
            c.Open();using var q=c.CreateCommand();q.CommandText=sql;return Convert.ToInt64(q.ExecuteScalar());
        }
        string Hash(string file)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))).ToLowerInvariant();
        const string archiveObjects="SELECT count(*) FROM sqlite_master WHERE name LIKE 'historical_project_%' OR name LIKE 'historical_batch_%' OR name LIKE 'historical_row_%' OR name LIKE 'historical_seal_%' OR name LIKE 'historical_batches_%' OR name LIKE 'historical_rows_%' OR name LIKE 'historical_seals_%'";
        const string grants="SELECT count(*) FROM role_permissions WHERE permission_code IN ('HistoricalProjects.View','Administration.HistoricalProjectImport')";
        check(Count(source,"SELECT count(*) FROM historical_project_batches")==0,"Archive boundary fixture empty archive");
        // Remove only the additive v47 objects from this disposable synthetic database.
        Sql(source,@"BEGIN IMMEDIATE;
            DROP VIEW historical_project_register;
            DROP TABLE historical_project_seals;
            DROP TABLE historical_project_rows;
            DROP TABLE historical_project_batches;
            DELETE FROM role_permissions WHERE permission_code IN ('HistoricalProjects.View','Administration.HistoricalProjectImport');
            DELETE FROM permissions WHERE code IN ('HistoricalProjects.View','Administration.HistoricalProjectImport');
            PRAGMA user_version=46;
            COMMIT;");
        check(Count(source,archiveObjects)==0&&Count(source,grants)==0&&Count(source,"PRAGMA user_version")==46,
            "Archive constructed boundary lacks v47 objects and grants");
        var hash=Hash(source);
        var result=UpgradeRehearsal.Run(source,hash,true);
        check(result.SourceSchema==46&&result.TargetSchema==47&&result.CrmAndExistingGrantsPreserved&&
            result.HistoricalProjects==0&&result.HistoricalArchivePreserved&&Hash(source)==hash,
            "Archive boundary rehearsal preserves full CRM and existing grants");
        var target=Path.Combine(root,"historical-upgrade-v47.db");File.Copy(source,target);
        var upgraded=new LocalPractice(target);
        check(upgraded.Login("admin",pw),"Archive upgraded admin login");
        var actual=upgraded.ReadLegacyCrmHistory()!;
        check(actual.Sha256==prior.Sha256&&actual.ReviewNote==prior.ReviewNote&&actual.ImportedBy==prior.ImportedBy&&
            actual.ImportedUtc==prior.ImportedUtc&&actual.Mappings.SequenceEqual(prior.Mappings),
            "Archive upgrade preserves original CRM review and mapped identities");
        check(upgraded.CanImportHistoricalProjects()&&upgraded.ReadHistoricalProjects().Count==0&&
            Count(target,grants)==2&&upgraded.ReadStaffGrants(engineer).Single(x=>x.Permission=="Clients.View").Allowed,
            "Archive upgrade adds admin-only grants and preserves prior employee grant");
        upgraded.Logout();

        var fault=Path.Combine(root,"historical-upgrade-fault.db");File.Copy(source,fault);
        Sql(fault,@"CREATE TRIGGER synthetic_v47_grant_failure BEFORE INSERT ON permissions
            WHEN NEW.code='Administration.HistoricalProjectImport' BEGIN SELECT RAISE(ABORT,'Synthetic grant failure'); END");
        var faultHash=Hash(fault);
        try{UpgradeRehearsal.Run(fault,faultHash,true);check(false,"Archive rehearsal rejects injected grant failure");}
        catch(SqliteException){check(true,"Archive rehearsal rejects injected grant failure");}
        check(Hash(fault)==faultHash,"Archive failed rehearsal preserves source bytes");
        // Direct constructor on a separate disposable clone tests actual SQLite rollback.
        var direct=Path.Combine(root,"historical-upgrade-rollback.db");File.Copy(fault,direct);
        try{_=new LocalPractice(direct);check(false,"Archive direct migration fails at grant insertion");}
        catch(SqliteException){check(true,"Archive direct migration fails at grant insertion");}
        check(Count(direct,"PRAGMA user_version")==46&&Count(direct,archiveObjects)==0&&Count(direct,grants)==0&&
            Count(direct,"SELECT count(*) FROM legacy_client_map")==prior.Clients&&
            Count(direct,"SELECT count(*) FROM legacy_contact_map")==prior.Contacts,
            "Archive failed migration rolls back schema objects grants and version");
        check(Hash(source)==hash,"Archive original boundary fixture remains unchanged");
    }
}
