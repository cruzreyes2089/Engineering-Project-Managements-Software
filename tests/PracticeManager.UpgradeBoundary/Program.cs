using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PracticeManager.Core;
using PracticeManager.Persistence;

// CI-only, fixed synthetic fixture; never provide an actual firm database.
internal static class Program
{
    const string Password="Synthetic-upgrade-passphrase-0001";
    const string ArchiveCodes="('HistoricalProjects.View','Administration.HistoricalProjectImport')";
    sealed record TableWitness(string Definition,int Rows,string Sha256);
    sealed record Witness(SortedDictionary<string,TableWitness> Tables,SortedDictionary<string,string> Objects);
    sealed record Evidence(string SourceSha256,Witness Before);
    static void Check(bool value,string label)
    {
        if(!value)throw new InvalidOperationException("Qualification failed: "+label);
        Console.WriteLine("PASS: "+label);
    }
    static string Hash(string path)
    {
        using var f=File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(f)).ToLowerInvariant();
    }
    static string Quote(string s)=>"\""+s.Replace("\"","\u0022\u0022")+"\"";
    static SqliteConnection Read(string path)
    {
        var c=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=path,Mode=SqliteOpenMode.ReadOnly,Pooling=false,ForeignKeys=true}.ToString());
        c.Open();return c;
    }
    static long Scalar(SqliteConnection c,string sql)
    {
        using var q=c.CreateCommand();q.CommandText=sql;return Convert.ToInt64(q.ExecuteScalar());
    }
    static void Closed(string path)
    {
        Check(new[]{"-wal","-shm","-journal"}.All(x=>!File.Exists(path+x)&&!Directory.Exists(path+x)),"closed fixture without sidecars");
    }
    static Witness Snapshot(string path,int version,bool excludeArchive)
    {
        using var c=Read(path);using var q=c.CreateCommand();
        Check(Scalar(c,"PRAGMA user_version")==version,"expected schema "+version);
        q.CommandText="PRAGMA integrity_check";Check((string?)q.ExecuteScalar()=="ok","database integrity");
        q.CommandText="PRAGMA foreign_key_check";using(var r=q.ExecuteReader())Check(!r.Read(),"foreign key integrity");
        var tables=new SortedDictionary<string,TableWitness>(StringComparer.Ordinal);
        var objects=new SortedDictionary<string,string>(StringComparer.Ordinal);
        var schema=new List<(string Type,string Name,string Table,string Sql)>();
        q.CommandText="SELECT type,name,tbl_name,coalesce(sql,'') FROM sqlite_schema ORDER BY type,name";
        using(var r=q.ExecuteReader())while(r.Read())schema.Add((r.GetString(0),r.GetString(1),r.GetString(2),r.GetString(3)));
        var archiveTables=new HashSet<string>(StringComparer.Ordinal){"historical_project_batches","historical_project_rows","historical_project_seals","historical_project_register"};
        foreach(var entry in schema)
        {
            if(excludeArchive&&archiveTables.Contains(entry.Table))continue;
            objects.Add(entry.Type+":"+entry.Name,entry.Sql);
            if(entry.Type!="table")continue;
            q.CommandText="SELECT * FROM "+Quote(entry.Name);
            if(excludeArchive&&entry.Name=="permissions")q.CommandText+=" WHERE code NOT IN "+ArchiveCodes;
            if(excludeArchive&&entry.Name=="role_permissions")q.CommandText+=" WHERE permission_code NOT IN "+ArchiveCodes;
            var rows=new List<string>();
            using(var r=q.ExecuteReader())
            {
                while(r.Read())
                {
                    if(rows.Count>=10000)throw new InvalidOperationException("Synthetic row bound exceeded.");
                    var fields=new object[r.FieldCount];r.GetValues(fields);
                    // Explicit CLR types distinguish SQLite INTEGER, REAL, TEXT, BLOB and NULL.
                    rows.Add(Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(fields.Select(v=>new{Type=v.GetType().FullName,Value=v}).ToArray()))));
                }
            }
            rows.Sort(StringComparer.Ordinal);
            tables.Add(entry.Name,new(entry.Sql,rows.Count,Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(string.Join("\n",rows))))));
        }
        return new(tables,objects);
    }
    static void Same(Witness a,Witness b)
    {
        Check(a.Tables.Count==b.Tables.Count&&a.Tables.All(x=>b.Tables.TryGetValue(x.Key,out var v)&&v==x.Value),"all existing table definitions and row-value witnesses preserved");
        Check(a.Objects.Count==b.Objects.Count&&a.Objects.All(x=>b.Objects.TryGetValue(x.Key,out var v)&&v==x.Value),"all existing schema objects preserved");
    }
    static void Main(string[] args)
    {
        if(args.Length!=2||args[0] is not ("generate" or "verify"))throw new ArgumentException("Fixed synthetic qualification mode required.");
        var root=Path.GetFullPath(args[1]);
        if(!Directory.Exists(root)||(File.GetAttributes(root)&FileAttributes.ReparsePoint)!=0)throw new ArgumentException("Trusted private CI directory required.");
        var source=Path.Combine(root,"synthetic-v46.db");
        var manifest=Path.Combine(root,"synthetic-witness.json");
        var target=Path.Combine(root,"synthetic-v47.db");
        if(args[0]=="generate")
        {
            Check(!File.Exists(source)&&!File.Exists(manifest)&&!File.Exists(target),"new isolated fixture");
            var s=new LocalPractice(source);
            s.Bootstrap("Synthetic Prior Version","admin",Password,"UP");
            Check(s.Login("admin",Password),"prior application login");
            var bytes=SyntheticCrmNativeTests.Fixture(false);
            var digest=Convert.ToHexString(SHA256.HashData(bytes));
            Check(s.ImportLegacyCrm(bytes,digest,true,"Synthetic reviewed CRM").Outcome=="Imported","prior application CRM import");
            var staffRole=s.ReadStaffRoles().Single(x=>x.Name=="Engineer").Id;
            s.SetStaffGrant(staffRole,"Clients.View",true,false);
            s.CreateStaffAccount("synthetic-engineer",staffRole,"Synthetic-temporary-password-0001");
            var active=s.SearchClients("").Single(x=>x.Status=="Active");
            var p=s.CreateProposal(new(active.Id,"Synthetic upgrade project","Synthetic site","Synthetic scope",125m));
            s.RecordJurisdictionPolicy("Synthetic jurisdiction",null,"EligibleForScreening","Synthetic policy",true);
            s.RecordProposalConflictReview(p,1,null,new("Synthetic jurisdiction","No","No","No","No","Synthetic review"),true);
            var h=s.RecordProposalIssued(p,1);
            var project=s.RecordProposalDecision(p,h,true,"Synthetic acceptance").ProjectId!.Value;
            s.RecordSignedAgreement(project,h,"synthetic.pdf","%PDF-1.7\nSynthetic\n%%EOF"u8.ToArray(),"Synthetic reviewed agreement",true);
            s.Logout();
            Closed(source);
            var before=Snapshot(source,46,false);
            var evidence=new Evidence(Hash(source),before);
            using var f=new FileStream(manifest,FileMode.CreateNew,FileAccess.Write,FileShare.None);
            JsonSerializer.Serialize(f,evidence);
            Console.WriteLine("Prior schema-46 synthetic fixture created by pinned source.");
        }
        else
        {
            Check(!File.Exists(target),"new disposable upgrade target");
            Closed(source);
            var evidence=JsonSerializer.Deserialize<Evidence>(File.ReadAllBytes(manifest))??throw new ArgumentException("Missing fixture witness.");
            Check(Hash(source)==evidence.SourceSha256,"prior fixture exact digest");
            Same(evidence.Before,Snapshot(source,46,false));
            var rehearsal=UpgradeRehearsal.Run(source,evidence.SourceSha256,true);
            Check(rehearsal.SourceSchema==46&&rehearsal.TargetSchema==47&&rehearsal.SourceUnchanged&&rehearsal.GetType().GetProperty("FullSchemaAndRowsPreserved")?.GetValue(rehearsal) is true,"native isolated upgrade rehearsal with full schema and row witness");
            File.Copy(source,target,false);
            _=new LocalPractice(target);
            Same(evidence.Before,Snapshot(target,47,true));
            using(var c=Read(target))
            {
                Check(Scalar(c,"SELECT count(*) FROM permissions WHERE code IN "+ArchiveCodes)==2,"two new permission definitions");
                Check(Scalar(c,"SELECT count(*) FROM role_permissions WHERE permission_code IN "+ArchiveCodes)==2&&
                      Scalar(c,"SELECT count(*) FROM role_permissions rp JOIN roles r ON r.id=rp.role_id WHERE r.name='Administrator' AND rp.allowed=1 AND rp.permission_code IN "+ArchiveCodes)==2,"only administrator archive grants");
                Check(Scalar(c,"SELECT count(*) FROM historical_project_batches")+
                      Scalar(c,"SELECT count(*) FROM historical_project_rows")+
                      Scalar(c,"SELECT count(*) FROM historical_project_seals")==0,"new archive empty");
            }
            Check(Hash(source)==evidence.SourceSha256,"original prior fixture bytes unchanged");
            Closed(source);
            Console.WriteLine("Actual prior-source upgrade qualification passed; no release authorization.");
        }
    }
}
