// Disposable native qualification only; never compiled into the application.
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PracticeManager.Persistence;

if(args.Length!=1)throw new ArgumentException("Expected prototype SQL path.");
var root=Path.Combine(Path.GetTempPath(),"crc-archive-native-"+Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    var path=Path.Combine(root,"synthetic.db");
    var p=new LocalPractice(path);
    const string pw="Synthetic-Archive-Only!2026";
    p.Bootstrap("Synthetic Archive Qualification","admin",pw,"CL");
    int checks=0;
    void Check(bool value,string label)
    {
        if(!value)throw new InvalidOperationException("FAIL: "+label);
        checks++;Console.WriteLine("PASS: "+label);
    }
    void Reject<T>(Action action,string label) where T:Exception
    {
        try{action();}catch(T){Check(true,label);return;}
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
    // Test-only provisioning on this newly created synthetic database. Not a migration.
    Sql(File.ReadAllText(args[0]));
    Sql("INSERT INTO permissions(code) VALUES('HistoricalProjects.View'),('Administration.HistoricalProjectImport'); INSERT INTO role_permissions VALUES(1,'HistoricalProjects.View',1),(1,'Administration.HistoricalProjectImport',1)");
    Check(Count("PRAGMA user_version")==46,"production schema version not changed");
    Reject<UnauthorizedAccessException>(()=>Preview(),"logged-out preview denied");
    Reject<UnauthorizedAccessException>(()=>Import(),"logged-out import denied");
    Reject<UnauthorizedAccessException>(()=>p.ReadHistoricalProjects(),"logged-out register denied");
    Check(p.Login("admin",pw),"synthetic administrator authenticated");
    Reject<ArgumentException>(()=>Preview(),"CRM history required");
    Check(p.ImportLegacyCrm(crm,ch,true,"Synthetic CRM review").Outcome=="Imported","native CRM import prerequisite");
    var before=Snapshot();
    var preview=Preview();
    Check(preview.TargetStatus=="ReadyForExplicitReview"&&preview.Projects==2&&preview.InactiveSourceClientProjects==1&&preview.DuplicateNumberGroups==1&&!preview.WorkAuthorized&&!preview.SourceAuthenticityVerified,"preview counts and no authority");
    Check(ArchiveCount()==0&&p.ReadHistoricalProjects().Count==0,"preview is read-only");
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
    p.Logout();Check(p.Login("admin",pw),"fresh authenticated synthetic session");
    foreach(var target in new[]{"historical_project_batches","historical_project_rows","historical_project_seals","audit_events"})
    {
        var condition=target=="audit_events"?" WHEN NEW.action='CommitHistoricalProjectArchive'":target=="historical_project_rows"?" WHEN NEW.legacy_id='P2'":"";
        Sql("CREATE TRIGGER test_abort BEFORE INSERT ON "+target+condition+" BEGIN SELECT RAISE(ABORT,'Synthetic injected failure'); END");
        Reject<SqliteException>(()=>Import(),"fault injected at "+target);
        Check(ArchiveCount()==0&&Snapshot()==before,"complete rollback at "+target);
        Sql("DROP TRIGGER test_abort");
    }
    var result=Import();
    Check(result.Outcome=="Imported"&&result.Projects==2&&!result.WorkAuthorized&&ArchiveCount()==5,"atomic archive, rows, seal and audit");
    var rows=p.ReadHistoricalProjects();
    Check(rows.Count==2&&rows.All(x=>!x.WorkAuthorized)&&rows[0].HistoricalAuthorizationStatus=="Approved"&&rows[1].Title==" Historical title ","historical status and whitespace preserved without authorization");
    Check(rows.All(x=>!x.ToString().Contains("Synthetic"))&&typeof(HistoricalArchiveEntry).GetProperty("AuthorizationEvidenceRef")==null,"register evidence omitted and default strings redacted");
    var map=p.ReadLegacyCrmHistory()!.Mappings.Where(x=>x.Kind=="Client").ToDictionary(x=>x.LegacyId,x=>x.NativeId);
    Check(rows.All(x=>map[x.LegacyClientId]==x.NativeClientId),"native parent identities reconciled");
    Check(Preview().TargetStatus=="AlreadyImported"&&Import().Outcome=="AlreadyImported"&&ArchiveCount()==5,"exact replay adds no rows or audit");
    var changed=Package("Synthetic modified");
    Check(p.PreviewHistoricalProjects(crm,changed.Projects,changed.Census,source,ch,Digest(changed.Projects),Digest(changed.Census)).TargetStatus=="DifferentPackageAlreadyImported","changed package detected");
    Reject<ArgumentException>(()=>p.ImportHistoricalProjects(crm,changed.Projects,changed.Census,source,ch,Digest(changed.Projects),Digest(changed.Census),true,"Synthetic"),"changed package cannot merge");
    Check(Snapshot()==before&&ArchiveCount()==5,"all operational records and sequences unchanged");
    // Simulate missing audit evidence only on the disposable fixture.
    Sql("DELETE FROM audit_events WHERE action='CommitHistoricalProjectArchive'");
    Reject<InvalidOperationException>(()=>p.ReadHistoricalProjects(),"missing audit marker fails closed");
    Reject<InvalidOperationException>(()=>Import(),"incomplete archive cannot replay");
    Check(Snapshot()==before,"failed replay leaves operational data unchanged");
    p.Logout();
    Console.WriteLine("Historical archive native candidate: "+checks+" checks passed. Synthetic only.");
}
finally
{
    SqliteConnection.ClearAllPools();
    Directory.Delete(root,true);
}
