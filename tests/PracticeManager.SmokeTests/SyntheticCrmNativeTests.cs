using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PracticeManager.Core;
using PracticeManager.Persistence;

internal static class SyntheticCrmNativeTests
{
    private const string Password = "Synthetic-test-passphrase-0001";

    // Independently specified fixture values; never accept arbitrary CRM records.
    private static Client ExpectedClient(string id) => id switch
    {
        "001" => new(0,"","Business / Company","Synthetic Alpha","","","","","","","","","00501","","Active"),
        "002" => new(0,"","Architect / Designer","Synthetic Beta","","","","","","","","","00999","","Inactive"),
        _ => throw new ArgumentException("Unexpected synthetic client.")
    };
    private static Contact ExpectedContact(string id) => id switch
    {
        "11" or "001" => new(0,0,"Synthetic First","","","","",true),
        "12" or "002" => new(0,0,"Synthetic Second","","","","",false),
        _ => throw new ArgumentException("Unexpected synthetic contact.")
    };
    private static string Parent(string id) => id is "11" or "001" ? "001" : id is "12" or "002" ? "002" : throw new ArgumentException("Unexpected contact.");
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    public static void VerifyFixture(byte[] bytes, bool empty, bool shared=false)
    {
        if(empty&&shared)throw new ArgumentException("Distinct synthetic fixture modes required.");
        var p = LegacyCrmTransfer.Parse(bytes);
        if (p.Clients.Count != 2 || p.Contacts.Count != (empty ? 0 : 2) ||
            !p.Clients.Select(x=>x.LegacyId).ToHashSet(StringComparer.Ordinal).SetEquals(new[]{"001","002"}) ||
            !p.Contacts.Select(x=>x.LegacyId).ToHashSet(StringComparer.Ordinal).SetEquals(empty?Array.Empty<string>():shared?new[]{"001","002"}:new[]{"11","12"}))
            throw new ArgumentException("Synthetic identities do not match.");
        foreach (var row in p.Clients)
            if (row.LegacyNumber != "" || row.Value != ExpectedClient(row.LegacyId))
                throw new ArgumentException("Synthetic client values do not match.");
        foreach (var row in p.Contacts)
            if (row.LegacyClientId != Parent(row.LegacyId) || row.Value != ExpectedContact(row.LegacyId))
                throw new ArgumentException("Synthetic contact values do not match.");
    }

    public static byte[] Fixture(bool empty, bool shared=false)
    {
        if(empty&&shared)throw new ArgumentException("Distinct synthetic fixture modes required.");
        object ClientRow(string id)
        {
            var x=ExpectedClient(id);
            return new{legacyId=id,legacyNumber="",type=x.Type,company=x.Company,first=x.First,last=x.Last,email=x.Email,
                phone=x.Phone,address1=x.Address1,address2=x.Address2,city=x.City,state=x.State,zip=x.Zip,notes=x.Notes,status=x.Status};
        }
        object ContactRow(string id)
        {
            var x=ExpectedContact(id);
            return new{legacyId=id,legacyClientId=Parent(id),first=x.First,last=x.Last,title=x.Title,email=x.Email,phone=x.Phone,primary=x.Primary};
        }
        return JsonSerializer.SerializeToUtf8Bytes(new{format="CRC-Legacy-CRM-v1",
            clients=new[]{ClientRow("001"),ClientRow("002")},contacts=empty?Array.Empty<object>():new[]{ContactRow(shared?"001":"11"),ContactRow(shared?"002":"12")}});
    }

    public static void Run(string path, byte[] bytes, bool empty, Action<bool,string> check, bool shared=false)
    {
        VerifyFixture(bytes,empty,shared);
        if(shared)
        {
            try{VerifyFixture(bytes,false);check(false,"Shared-key fixture rejects ordinary identity mode");}
            catch(ArgumentException){check(true,"Shared-key fixture rejects ordinary identity mode");}
            try{VerifyFixture(Fixture(false),false,true);check(false,"Ordinary fixture rejected in shared-key mode");}
            catch(ArgumentException){check(true,"Ordinary fixture rejected in shared-key mode");}
        }
        var parsed=LegacyCrmTransfer.Parse(bytes);
        var hash=Hash(bytes);
        var s=new LocalPractice(path);
        s.Bootstrap("Synthetic DAO Qualification","admin",Password,"CL");
        void Sql(string sql)
        {
            using var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=path,Pooling=false}.ToString());
            db.Open();using var q=db.CreateCommand();q.CommandText=sql;q.ExecuteNonQuery();
        }
        long Count(string sql)
        {
            using var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=path,Pooling=false}.ToString());
            db.Open();using var q=db.CreateCommand();q.CommandText=sql;return Convert.ToInt64(q.ExecuteScalar());
        }
        void Denied(Action action,string label)
        {
            try{action();}catch(UnauthorizedAccessException){check(true,label);return;}
            check(false,label);
        }
        void Invalid(Action action,string label)
        {
            try{action();}catch(ArgumentException){check(true,label);return;}
            check(false,label);
        }
        Denied(()=>s.PreviewLegacyCrm(bytes),"DAO fixture unauthorized preview denied");
        check(s.Login("admin",Password),"DAO fixture admin login");
        var preview=s.PreviewLegacyCrm(bytes);
        check(preview.Sha256==hash&&preview.Clients==2&&preview.Contacts==parsed.Contacts.Count&&
              preview.TargetStatus=="ReadyForExplicitReview","DAO fixture exact preview identity");
        long auditBefore=Count("SELECT count(*) FROM audit_events");
        void EmptyTarget()
        {
            check(Count("SELECT (SELECT count(*) FROM clients)+(SELECT count(*) FROM contacts)+(SELECT count(*) FROM legacy_crm_batches)+(SELECT count(*) FROM legacy_client_map)+(SELECT count(*) FROM legacy_contact_map)")==0,
                "DAO fixture no partial records or mappings");
            check(Count("SELECT next_value FROM sequences WHERE name='Client'")==1,"DAO fixture sequence unchanged");
            check(Count("SELECT count(*) FROM audit_events")==auditBefore,"DAO fixture audit rolled back");
        }
        Invalid(()=>s.ImportLegacyCrm(bytes,hash,false,"Synthetic"),"DAO fixture explicit confirmation required");
        Invalid(()=>s.ImportLegacyCrm(bytes,new string('0',64),true,"Synthetic"),"DAO fixture exact hash required");
        Sql("UPDATE role_permissions SET allowed=0 WHERE permission_code='Administration.LegacyCrmImport' AND role_id=1");
        Denied(()=>s.ImportLegacyCrm(bytes,hash,true,"Synthetic"),"DAO fixture revoked migration grant enforced");
        Sql("UPDATE role_permissions SET allowed=1 WHERE permission_code='Administration.LegacyCrmImport' AND role_id=1");
        EmptyTarget();
        // Fail after all source rows and mappings have been inserted, before transaction commit.
        Sql("CREATE TRIGGER synthetic_abort_sequence BEFORE UPDATE ON sequences WHEN OLD.name='Client' BEGIN SELECT RAISE(ABORT,'Synthetic rollback'); END");
        try{s.ImportLegacyCrm(bytes,hash,true,"Synthetic");check(false,"DAO fixture fault injection");}
        catch(SqliteException){check(true,"DAO fixture fault injection");}
        EmptyTarget();
        Sql("DROP TRIGGER synthetic_abort_sequence");
        var imported=s.ImportLegacyCrm(bytes,hash,true,"Synthetic");
        check(imported.Outcome=="Imported"&&imported.Sha256==hash,"DAO fixture import committed");
        var h=s.ReadLegacyCrmHistory()!;
        check(h.Sha256==hash&&h.Clients==2&&h.Contacts==parsed.Contacts.Count,"DAO fixture batch evidence reconciles");
        var clients=s.SearchClients("");
        check(clients.Count==2,"DAO fixture client count");
        foreach(var source in parsed.Clients)
        {
            var map=h.Mappings.Single(x=>x.Kind=="Client"&&x.LegacyId==source.LegacyId);
            var actual=clients.Single(x=>x.Id==map.NativeId);
            var expected=ExpectedClient(source.LegacyId);
            var ordinal=parsed.Clients.ToList().FindIndex(x=>x.LegacyId==source.LegacyId)+1;
            check(actual.Number==$"CL-{ordinal:D6}"&&actual.Number==map.NativeClientNumber&&map.LegacyNumber=="",
                "DAO fixture original identity and native numbering");
            check((actual with {Id=0,Number=""})==expected,"DAO fixture all client values");
            foreach(var contact in parsed.Contacts.Where(x=>x.LegacyClientId==source.LegacyId))
            {
                var cm=h.Mappings.Single(x=>x.Kind=="Contact"&&x.LegacyId==contact.LegacyId);
                var value=s.Contacts(actual.Id).Single(x=>x.Id==cm.NativeId);
                check(cm.LegacyParentId==source.LegacyId&&cm.NativeClientNumber==actual.Number&&value.ClientId==actual.Id,
                    "DAO fixture original and native parent identity");
                check((value with {Id=0,ClientId=0})==ExpectedContact(contact.LegacyId),"DAO fixture all contact values");
            }
        }
        check(Count("SELECT count(*) FROM clients WHERE (zip='00501' OR zip='00999') AND phone=''")==2,
            "DAO fixture raw stored postal and phone fields");
        check(Count("SELECT count(*) FROM contacts")==parsed.Contacts.Count,"DAO fixture stored contact count");
        check(Count("SELECT count(*) FROM legacy_client_map")==2&&Count("SELECT count(*) FROM legacy_contact_map")==parsed.Contacts.Count,
            "DAO fixture stored mapping counts");
        check(Count("SELECT next_value FROM sequences WHERE name='Client'")==3,"DAO fixture sequence reconciles");
        if(shared)
        {
            check(h.Mappings.Where(x=>x.Kind=="Client").Select(x=>x.LegacyId).ToHashSet().SetEquals(new[]{"001","002"})&&
                h.Mappings.Where(x=>x.Kind=="Contact").Select(x=>x.LegacyId).ToHashSet().SetEquals(new[]{"001","002"})&&
                h.Mappings.Where(x=>x.Kind=="Contact").All(x=>x.LegacyId==x.LegacyParentId),
                "Shared-key native mappings preserve separate entity kinds and exact text identities");
        }
        var postAudit=Count("SELECT count(*) FROM audit_events");
        check(s.ImportLegacyCrm(bytes,hash,true,"Synthetic repeat").Outcome=="AlreadyImported","DAO fixture repeat prevented");
        var changed=bytes.Concat(new byte[]{32}).ToArray();
        Invalid(()=>s.ImportLegacyCrm(changed,Hash(changed),true,"Synthetic changed bytes"),"DAO fixture different bytes cannot merge");
        check(Count("SELECT count(*) FROM clients")==2&&Count("SELECT count(*) FROM contacts")==parsed.Contacts.Count&&
            Count("SELECT next_value FROM sequences WHERE name='Client'")==3&&Count("SELECT count(*) FROM audit_events")==postAudit,
            "DAO fixture repeat and changed package preserve target");
    }
}
