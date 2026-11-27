using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using PracticeManager.Core;
using PracticeManager.Persistence;

internal static class HistoricalArchiveRecoveryTests
{
    public static void Run(LocalPractice service,string path,string password,HistoricalProjectPackage package,Action<bool,string> check)
    {
        var root=Path.GetDirectoryName(path)!;
        var copy=Path.Combine(root,"historical-recovery.db");
        var target=Path.Combine(root,"historical-restored.db");
        string Hash(string file)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))).ToLowerInvariant();
        void Mutate(string file,string sql)
        {
            using var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=file,Pooling=false}.ToString());
            db.Open();using var q=db.CreateCommand();q.CommandText=sql;q.ExecuteNonQuery();
        }
        void Reject(Action a,string label)
        {
            try{a();}catch(InvalidOperationException){check(true,label);return;}
            check(false,label);
        }
        var original=service.ReadHistoricalProjects();
        var backup=service.CreateRecoveryCopy(copy);
        check(backup.Sha256==Hash(copy),"Archive recovery exact file hash");
        var rehearsal=UpgradeRehearsal.Run(copy,backup.Sha256,true);
        check(rehearsal.SourceSchema==47&&rehearsal.TargetSchema==47&&rehearsal.HistoricalProjects==package.Rows.Count&&
            rehearsal.HistoricalArchivePreserved&&rehearsal.FullSchemaAndRowsPreserved&&rehearsal.SourceUnchanged&&Hash(copy)==backup.Sha256,
            "Archive populated current-schema rehearsal preserves fingerprint and source");
        var restored=service.RestoreRecoveryCopyToNewFile(copy,backup.Sha256,target);
        check(restored.HistoricalProjects==package.Rows.Count&&restored.Projects==0&&restored.Sha256==backup.Sha256,
            "Archive isolated restore separates historical and operational counts");
        var reopened=new LocalPractice(target);
        check(reopened.Login("admin",password),"Archive restored independent login");
        var restoredRows=reopened.ReadHistoricalProjects();
        check(restoredRows.ToHashSet().SetEquals(original)&&restoredRows.All(x=>!x.WorkAuthorized),
            "Archive restored exact records and mapped client IDs");
        check(Hash(copy)==backup.Sha256&&service.ReadHistoricalProjects().ToHashSet().SetEquals(original),
            "Archive source and recovery copy preserved");
        // Deliberately tamper with disposable recovery clone, not active database.
        var damaged=Path.Combine(root,"historical-damaged.db");File.Copy(copy,damaged);
        Mutate(damaged,"DROP TRIGGER historical_seals_no_delete; DELETE FROM historical_project_seals");
        var refused=Path.Combine(root,"historical-refused.db");
        Reject(()=>service.RestoreRecoveryCopyToNewFile(damaged,Hash(damaged),refused),
            "Archive incomplete seal rejected even with matching file digest");
        check(!File.Exists(refused),"Archive incomplete recovery never published");
        Reject(()=>UpgradeRehearsal.Run(damaged,Hash(damaged),true),"Archive incomplete upgrade source rejected");
        var broken=new LocalPractice(damaged);
        check(broken.Login("admin",password),"Archive disposable damage test login");
        Reject(()=>broken.ReadHistoricalProjects(),"Archive incomplete state fails closed on read");
        Reject(()=>broken.CreateRecoveryCopy(refused),"Archive incomplete source backup rejected");
        check(!File.Exists(refused),"Archive invalid backup never published");
        check(Hash(copy)==backup.Sha256&&service.ReadHistoricalProjects().ToHashSet().SetEquals(original),
            "Archive failure tests preserve valid source and recovery");
        foreach(var (label,mutation) in new[]{
            ("missing-audit","DELETE FROM audit_events WHERE action='ImportHistoricalProjects'"),
            ("duplicate-audit","INSERT INTO audit_events(actor_id,action,entity,entity_id) SELECT actor_id,action,entity,entity_id FROM audit_events WHERE action='ImportHistoricalProjects'"),
            ("wrong-actor","UPDATE audit_events SET actor_id=NULL WHERE action='ImportHistoricalProjects'"),
            ("wrong-entity","UPDATE audit_events SET entity='Firm' WHERE action='ImportHistoricalProjects'")})
        {
            var corrupt=Path.Combine(root,"historical-"+label+".db");File.Copy(copy,corrupt);
            Mutate(corrupt,mutation);
            var destination=Path.Combine(root,"historical-"+label+"-refused.db");
            Reject(()=>service.RestoreRecoveryCopyToNewFile(corrupt,Hash(corrupt),destination),
                "Archive restore rejects "+label+" despite matching file digest");
            check(!File.Exists(destination),"Archive invalid audit restore not published: "+label);
            Reject(()=>UpgradeRehearsal.Run(corrupt,Hash(corrupt),true),"Archive rehearsal rejects "+label);
            var damagedSession=new LocalPractice(corrupt);
            check(damagedSession.Login("admin",password),"Archive disposable audit corruption login: "+label);
            Reject(()=>damagedSession.ReadHistoricalProjects(),"Archive read rejects "+label);
            Reject(()=>damagedSession.CreateRecoveryCopy(destination),"Archive backup rejects "+label);
            check(!File.Exists(destination),"Archive invalid audit backup not published: "+label);
            damagedSession.Logout();
        }
        check(Hash(copy)==backup.Sha256&&service.ReadHistoricalProjects().ToHashSet().SetEquals(original),
            "Archive audit corruption exercises preserve original records and backup");
        HistoricalAssociationTests.Run(reopened,target,check);
    }
}
