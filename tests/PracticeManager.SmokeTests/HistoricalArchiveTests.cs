using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PracticeManager.Persistence;

internal static class HistoricalArchiveTests
{
 public static void Run(string root,string pw,Action<bool,string> check)
 {
    Directory.CreateDirectory(root);
    var path=Path.Combine(root,"synthetic.db");
    var p=new LocalPractice(path);
    p.Bootstrap("Synthetic Archive Qualification","admin",pw,"CL");
    void Reject<T>(Action action,string label) where T:Exception
    {
        try{action();}catch(T){check(true,label);return;}
        throw new InvalidOperationException("FAIL: "+label);
    }
    SqliteConnection Open()
    {
        var c=new SqliteConnection(new SqliteConnectionStringBuilder
        {DataSource=path,Mode=SqliteOpenMode.ReadWrite,ForeignKeys=true,Pooling=false}.ToString());
        c.Open();return c;
    }
    void Sql(string sql)
    {
        using var c=Open();using var q=c.CreateCommand();q.CommandText=sql;q.ExecuteNonQuery();
    }
    long Count(string sql)
    {
        using var c=Open();using var q=c.CreateCommand();q.CommandText=sql;
        return Convert.ToInt64(q.ExecuteScalar());
    }
    string Snapshot()
    {
        // Snapshot every operational table, excluding the archive and audit events.
        using var c=Open();
        using var q=c.CreateCommand();
        q.CommandText="SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'historical_project_%' AND name NOT IN ('audit_events','users','permissions','role_permissions') AND name NOT LIKE 'sqlite_%' ORDER BY name";
        var tables=new List<string>();using(var r=q.ExecuteReader())while(r.Read())tables.Add(r.GetString(0));
        var result=new List<string>();
        foreach(var table in tables)
        {
            using var row=c.CreateCommand();row.CommandText="SELECT * FROM \""+table.Replace("\"","\"\"")+"\"";
            using var r=row.ExecuteReader();var values=new List<string>();
            while(r.Read())
            {
                var fields=new object[r.FieldCount];r.GetValues(fields);
                values.Add(JsonSerializer.Serialize(fields.Select(x=>x is DBNull?null:x).ToArray()));
            }
            values.Sort(StringComparer.Ordinal);result.Add(table+":"+JsonSerializer.Serialize(values));
        }
        return JsonSerializer.Serialize(result);
    }
    static string Digest(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    static byte[] Bytes(object value)=>JsonSerializer.SerializeToUtf8Bytes(value);
    object Client(string id,string status)=>new{legacyId=id,legacyNumber="OLD-"+id,type="Business / Company",company="Synthetic "+id,first="",last="",email="",phone="",address1="",address2="",city="",state="",zip="",notes="",status};
    var crm=Bytes(new{format="CRC-Legacy-CRM-v1",clients=new[]{Client("A","Active"),Client("B","Inactive")},contacts=Array.Empty<object>()});
    var source=new string('a',64);var ch=Digest(crm);
    (byte[] Projects,byte[] Census) Package(string title)
    {
        object Project(string id,string parent,string number,string name,string status,string authorization,string evidence)
            =>new{legacyId=id,legacyClientId=parent,legacyProjectNumber=number,title=name,site="Synthetic site",legacyStatus=status,legacyAuthorizationStatus=authorization,authorizationEvidenceRef=evidence};
        var projects=Bytes(new{format="CRC-Legacy-Projects-v1",sourceSha256=source,crmTransferSha256=ch,projects=new[]{
            Project("P1","A","OLD-1",title,"Active","Approved","Synthetic-private-ref"),
            Project("P2","B","old-1"," Historical title ","Closed","Unknown","")}});
        var census=Bytes(new{format="CRC-Legacy-Project-Census-v1",sourceSha256=source,crmTransferSha256=ch,projectTransferSha256=Digest(projects),projects=new[]{
            new{legacyId="P1",legacyClientId="A",legacyProjectNumber="OLD-1"},
            new{legacyId="P2",legacyClientId="B",legacyProjectNumber="old-1"}}});
        return(projects,census);
    }
    var data=Package("Synthetic original");
    HistoricalArchivePreview Preview()=>p.PreviewHistoricalProjects(crm,data.Projects,data.Census,source,ch,Digest(data.Projects),Digest(data.Census));
    HistoricalArchiveImportResult Import(bool confirm=true,string note="Synthetic reviewed mapping")=>p.ImportHistoricalProjects(crm,data.Projects,data.Census,source,ch,Digest(data.Projects),Digest(data.Census),confirm,note);
    long ArchiveCount()=>Count("SELECT (SELECT count(*) FROM historical_project_batches)+(SELECT count(*) FROM historical_project_rows)+(SELECT count(*) FROM historical_project_seals)+(SELECT count(*) FROM audit_events WHERE action='CommitHistoricalProjectArchive')");
    check(Count("PRAGMA user_version")==47,"archive native schema 47");
    check(Count("SELECT count(*) FROM role_permissions WHERE permission_code IN ('HistoricalProjects.View','Administration.HistoricalProjectImport') AND role_id=1 AND allowed=1")==2,"archive bootstrap administrator grants");
    check(Count("SELECT count(*) FROM role_permissions WHERE permission_code IN ('HistoricalProjects.View','Administration.HistoricalProjectImport') AND role_id<>1 AND allowed=1")==0,"archive nonadministrator grants default denied");
    Reject<UnauthorizedAccessException>(()=>Preview(),"logged-out preview denied");
    Reject<UnauthorizedAccessException>(()=>Import(),"logged-out import denied");
    Reject<UnauthorizedAccessException>(()=>p.ReadHistoricalProjects(),"logged-out register denied");
    check(p.Login("admin",pw),"synthetic administrator authenticated");
    Reject<ArgumentException>(()=>Preview(),"CRM history required");
    check(p.ImportLegacyCrm(crm,ch.ToUpperInvariant(),true,"Synthetic CRM review").Outcome=="Imported","native CRM import prerequisite");
    var before=Snapshot();
    var preview=Preview();
    check(preview.TargetStatus=="ReadyForExplicitReview"&&preview.Projects==2&&preview.InactiveSourceClientProjects==1&&preview.DuplicateNumberGroups==1&&!preview.WorkAuthorized&&!preview.SourceAuthenticityVerified,"preview counts and no authority");
    check(ArchiveCount()==0&&p.ReadHistoricalProjects().Count==0,"preview is read-only");
    Reject<ArgumentException>(()=>Import(false),"confirmation required");
    Reject<ArgumentException>(()=>Import(true," "),"review note required");
    Reject<ArgumentException>(()=>p.PreviewHistoricalProjects(crm,data.Projects,data.Census,source,ch,new string('0',64),Digest(data.Census)),"project digest mismatch rejected");
    foreach(var permission in new[]{"Administration.Manage","Administration.HistoricalProjectImport","HistoricalProjects.View","Clients.View"})
    {
        Sql("UPDATE role_permissions SET allowed=0 WHERE role_id=1 AND permission_code='"+permission+"'");
        Reject<UnauthorizedAccessException>(()=>Preview(),"revoked "+permission+" blocks preview");
        Reject<UnauthorizedAccessException>(()=>Import(),"revoked "+permission+" blocks import");
        if(permission is "HistoricalProjects.View" or "Clients.View")
            Reject<UnauthorizedAccessException>(()=>p.ReadHistoricalProjects(),"revoked "+permission+" blocks register");
        Sql("UPDATE role_permissions SET allowed=1 WHERE role_id=1 AND permission_code='"+permission+"'");
    }
    Sql("UPDATE users SET force_password_change=1 WHERE role_id=1");
    Reject<UnauthorizedAccessException>(()=>Preview(),"forced password change blocks preview");
    Reject<UnauthorizedAccessException>(()=>Import(),"forced password change blocks import");
    Reject<UnauthorizedAccessException>(()=>p.ReadHistoricalProjects(),"forced password change blocks register");
    Sql("UPDATE users SET force_password_change=0 WHERE role_id=1");
    p.Logout();check(p.Login("admin",pw),"fresh authenticated synthetic session");
    foreach(var target in new[]{"historical_project_batches","historical_project_rows","historical_project_seals","audit_events"})
    {
        var condition=target=="audit_events"?" WHEN NEW.action='CommitHistoricalProjectArchive'":target=="historical_project_rows"?" WHEN NEW.legacy_id='P2'":"";
        Sql("CREATE TRIGGER test_abort BEFORE INSERT ON "+target+condition+" BEGIN SELECT RAISE(ABORT,'Synthetic injected failure'); END");
        Reject<SqliteException>(()=>Import(),"fault injected at "+target);
        check(ArchiveCount()==0&&Snapshot()==before,"complete rollback at "+target);
        Sql("DROP TRIGGER test_abort");
    }
    var result=Import();
    check(result.Outcome=="Imported"&&result.Projects==2&&!result.WorkAuthorized&&ArchiveCount()==5,"atomic archive, rows, seal and audit");
    var rows=p.ReadHistoricalProjects();
    check(rows.Count==2&&rows.All(x=>!x.WorkAuthorized)&&rows[0].HistoricalAuthorizationStatus=="Approved"&&rows[1].Title==" Historical title ","historical status and whitespace preserved without authorization");
    check(rows.All(x=>!x.ToString().Contains("Synthetic"))&&typeof(HistoricalArchiveEntry).GetProperty("AuthorizationEvidenceRef")==null,"register evidence omitted and default strings redacted");
    var map=p.ReadLegacyCrmHistory()!.Mappings.Where(x=>x.Kind=="Client").ToDictionary(x=>x.LegacyId,x=>x.NativeId);
    check(rows.All(x=>map[x.LegacyClientId]==x.NativeClientId),"native parent identities reconciled");
    check(Preview().TargetStatus=="AlreadyImported"&&Import().Outcome=="AlreadyImported"&&ArchiveCount()==5,"exact replay adds no rows or audit");
    var changed=Package("Synthetic modified");
    check(p.PreviewHistoricalProjects(crm,changed.Projects,changed.Census,source,ch,Digest(changed.Projects),Digest(changed.Census)).TargetStatus=="DifferentPackageAlreadyImported","changed package detected");
    Reject<ArgumentException>(()=>p.ImportHistoricalProjects(crm,changed.Projects,changed.Census,source,ch,Digest(changed.Projects),Digest(changed.Census),true,"Synthetic"),"changed package cannot merge");
    check(Snapshot()==before&&ArchiveCount()==5,"all operational records and sequences unchanged");
    {
        var recovery=Path.Combine(root,"recovery.db");
        var restored=Path.Combine(root,"restored.db");
        var backup=p.CreateRecoveryCopy(recovery);
        var copy=p.RestoreRecoveryCopyToNewFile(recovery,backup.Sha256,restored);
        check(copy.Sha256==backup.Sha256,"archive recovery hash reconciled");
        var restoredPractice=new LocalPractice(restored);
        check(restoredPractice.Login("admin",pw),"restored synthetic archive authenticated");
        check(restoredPractice.ReadHistoricalProjects().SequenceEqual(rows),"recovery preserves all register values");
        check(restoredPractice.PreviewHistoricalProjects(crm,data.Projects,data.Census,source,ch,Digest(data.Projects),Digest(data.Census)).TargetStatus=="AlreadyImported","recovery retains exact package replay evidence");
        restoredPractice.Logout();
        Reject<ArgumentException>(()=>p.RestoreRecoveryCopyToNewFile(recovery,backup.Sha256,restored),"restore refuses overwrite");
    }
    Sql("UPDATE role_permissions SET allowed=0 WHERE role_id=1 AND permission_code='HistoricalProjects.View'");
    var reopened=new LocalPractice(path);
    check(reopened.Login("admin",pw),"archive reopened administrator authenticated");
    Reject<UnauthorizedAccessException>(()=>reopened.ReadHistoricalProjects(),"archive reopen preserves revoked view");
    reopened.Logout();
    Sql("UPDATE role_permissions SET allowed=1 WHERE role_id=1 AND permission_code='HistoricalProjects.View'");
    // Simulate missing audit evidence only on the disposable fixture.
    Sql("DELETE FROM audit_events WHERE action='CommitHistoricalProjectArchive'");
    Reject<InvalidOperationException>(()=>p.ReadHistoricalProjects(),"missing audit marker fails closed");
    Reject<InvalidOperationException>(()=>Import(),"incomplete archive cannot replay");
    {
        Reject<InvalidOperationException>(()=>p.CreateRecoveryCopy(Path.Combine(root,"invalid.db")),"incomplete archive cannot be backed up");
        check(!File.Exists(Path.Combine(root,"invalid.db")),"invalid recovery output not published");
    }
    // Recovery may legitimately add audit events; operational tables must not change.
    check(Snapshot()==before,"failed replay leaves operational data unchanged");
    p.Logout();
 }
}
