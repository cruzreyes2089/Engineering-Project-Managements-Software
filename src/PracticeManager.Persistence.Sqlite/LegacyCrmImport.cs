using Microsoft.Data.Sqlite;
using PracticeManager.Core;
using System.Globalization;
namespace PracticeManager.Persistence;
public sealed record LegacyCrmPreview(string Sha256,int Clients,int Contacts,int DuplicateNameGroups,int DuplicateNumberGroups,string TargetStatus);
public sealed record LegacyCrmImportResult(string Outcome,string Sha256,int Clients,int Contacts);
public sealed record LegacyCrmMappingEvidence(string Kind,string LegacyId,string LegacyNumber,string? LegacyParentId,long NativeId,string NativeClientNumber);
public sealed record LegacyCrmHistory(string Sha256,int Clients,int Contacts,string ReviewNote,string ImportedUtc,long ImportedBy,IReadOnlyList<LegacyCrmMappingEvidence> Mappings);
public sealed partial class LocalPractice
{
 // Fresh-firm production candidate excludes unqualified legacy transfer.
 // Existing mapping history remains readable with the original grants.
 private void RequireLegacyMigrationAvailable()
 {
  if(productionFixtureLayout!=null)throw new UnauthorizedAccessException("Legacy migration is deferred for the initial release.");
 }
 private long RequireLegacyCrm(SqliteConnection c,SqliteTransaction t)
 {
  var actor=RequireAdministration(c,t);foreach(var grant in new[]{"Administration.LegacyCrmImport","Clients.View","Clients.Edit","Contacts.View","Contacts.Edit"})Require(c,t,grant);return actor;
 }
 private static string LegacyCrmTarget(SqliteConnection c,SqliteTransaction t,string hash)
 {
  var existing=Scalar(c,t,"SELECT source_hash FROM legacy_crm_batches WHERE id=1");
  if(existing is string prior)return prior==hash?"AlreadyImported":"DifferentPackageAlreadyImported";
  if(Convert.ToInt64(Scalar(c,t,"SELECT (SELECT count(*) FROM clients)+(SELECT count(*) FROM contacts)+(SELECT count(*) FROM proposals)+(SELECT count(*) FROM legacy_client_map)+(SELECT count(*) FROM legacy_contact_map)"))!=0)return "TargetNotEmpty";
  return "ReadyForExplicitReview";
 }
 public LegacyCrmPreview PreviewLegacyCrm(byte[] data)
 {
  RequireLegacyMigrationAvailable();
  using var c=Open();using var t=c.BeginTransaction();RequireLegacyCrm(c,t);var p=LegacyCrmTransfer.Parse(data);
  return new(p.Sha256,p.Clients.Count,p.Contacts.Count,p.DuplicateNameGroups,p.DuplicateNumberGroups,LegacyCrmTarget(c,t,p.Sha256));
 }
 private static LegacyCrmHistory? LegacyHistory(SqliteConnection c,SqliteTransaction t)
 {
  string hash,note,utc;int clients,contacts;long actor;
  using(var q=Command(c,t,"SELECT source_hash,client_count,contact_count,review_note,imported_utc,imported_by FROM legacy_crm_batches WHERE id=1"))
  using(var r=q.ExecuteReader()){if(!r.Read())return null;hash=r.GetString(0);clients=r.GetInt32(1);contacts=r.GetInt32(2);note=r.GetString(3);utc=r.GetString(4);actor=r.GetInt64(5);}
  if(clients is <1 or >5000||contacts is <0 or >10000)throw new InvalidOperationException("Legacy batch counts outside supported limits.");
  var rows=new List<LegacyCrmMappingEvidence>();
  using(var q=Command(c,t,"SELECT m.legacy_id,m.legacy_number,m.client_id,c.client_no FROM legacy_client_map m JOIN clients c ON c.id=m.client_id WHERE m.batch_id=1 ORDER BY m.legacy_id COLLATE NOCASE LIMIT 5001"))
  using(var r=q.ExecuteReader())while(r.Read())rows.Add(new("Client",r.GetString(0),r.GetString(1),null,r.GetInt64(2),r.GetString(3)));
  if(rows.Count!=clients)throw new InvalidOperationException("Legacy client mapping count mismatch.");
  using(var q=Command(c,t,@"SELECT m.legacy_id,m.legacy_client_id,m.contact_id,c.client_no FROM legacy_contact_map m JOIN legacy_client_map l ON l.legacy_id=m.legacy_client_id AND l.batch_id=m.batch_id JOIN contacts o ON o.id=m.contact_id AND o.client_id=l.client_id JOIN clients c ON c.id=l.client_id WHERE m.batch_id=1 ORDER BY m.legacy_id COLLATE NOCASE LIMIT 10001"))
  using(var r=q.ExecuteReader())while(r.Read())rows.Add(new("Contact",r.GetString(0),"",r.GetString(1),r.GetInt64(2),r.GetString(3)));
  if(rows.Count!=clients+contacts)throw new InvalidOperationException("Legacy contact mapping count or parent mismatch.");
  return new(hash,clients,contacts,note,utc,actor,rows.AsReadOnly());
 }
 public LegacyCrmHistory? ReadLegacyCrmHistory()
 {
  using var c=Open();using var t=c.BeginTransaction();RequireLegacyCrm(c,t);return LegacyHistory(c,t);
 }
 public string ExportLegacyCrmMapping(string expectedHash)
 {
  using var c=Open();using var t=c.BeginTransaction(deferred:false);var actor=RequireLegacyCrm(c,t);var h=LegacyHistory(c,t)??throw new ArgumentException("No imported package.");
  if(h.Sha256!=expectedHash)throw new ArgumentException("Migration batch identity changed.");
  var rows=new List<IReadOnlyList<string?>>{new[]{"LEGACY CRM MAPPING — CONFIDENTIAL — NOT A FINANCIAL MIGRATION"},new[]{"Source SHA-256","Imported UTC","Client count","Contact count"},new[]{h.Sha256,h.ImportedUtc,SafeCsv.Number(h.Clients),SafeCsv.Number(h.Contacts)},new[]{"Kind","Legacy ID","Legacy number","Legacy parent ID","Native ID","Native client number"}};
  rows.AddRange(h.Mappings.Select(m=>(IReadOnlyList<string?>)new[]{m.Kind,m.LegacyId,m.LegacyNumber,m.LegacyParentId,SafeCsv.Number(m.NativeId),m.NativeClientNumber}));
  var csv=SafeCsv.Build(rows);Audit(c,t,actor,"PrepareLegacyMappingExport","Firm",1);t.Commit();return csv;
 }
 public LegacyCrmImportResult ImportLegacyCrm(byte[] data,string expectedHash,bool explicitlyConfirmed,string reviewNote)
 {
  RequireLegacyMigrationAvailable();
  using var c=Open();using var t=c.BeginTransaction(deferred:false);var actor=RequireLegacyCrm(c,t);var p=LegacyCrmTransfer.Parse(data);
  if(p.Sha256!=expectedHash||!explicitlyConfirmed)throw new ArgumentException("Exact preview hash and explicit import confirmation required.");
  var note=Validation.Text(reviewNote,1000,"Reviewed source mapping and counts",true);var status=LegacyCrmTarget(c,t,p.Sha256);
  if(status=="AlreadyImported"){
   var history=LegacyHistory(c,t);if(history==null||history.Sha256!=p.Sha256||history.Clients!=p.Clients.Count||history.Contacts!=p.Contacts.Count)throw new InvalidOperationException("Legacy mapping evidence incomplete.");
   return new(status,p.Sha256,p.Clients.Count,p.Contacts.Count);
  }
  if(status!="ReadyForExplicitReview")throw new ArgumentException("Import unavailable: "+status+". No merge or overwrite is supported.");
  var prefix=Convert.ToString(Scalar(c,t,"SELECT client_prefix FROM firms WHERE id=1"))??throw new InvalidOperationException("Client prefix unavailable.");
  var next=Convert.ToInt64(Scalar(c,t,"SELECT next_value FROM sequences WHERE name='Client'"));
  if(next<=0||next>long.MaxValue-p.Clients.Count)throw new InvalidOperationException("Client sequence unavailable.");
  Exec(c,t,"INSERT INTO legacy_crm_batches(id,source_hash,client_count,contact_count,review_note,imported_by,imported_utc) VALUES(1,$h,$c,$o,$n,$a,$u)",("$h",p.Sha256),("$c",p.Clients.Count),("$o",p.Contacts.Count),("$n",note),("$a",actor),("$u",UtcNow()));
  var map=new Dictionary<string,long>(StringComparer.OrdinalIgnoreCase);
  foreach(var source in p.Clients){
   var x=source.Value;var number=prefix+"-"+next.ToString("D6",CultureInfo.InvariantCulture);next++;
   Exec(c,t,"INSERT INTO clients(client_no,client_type,company_name,first_name,last_name,email,phone,address1,address2,city,state,zip,notes,status) VALUES($no,$t,$co,$f,$l,$e,$p,$a1,$a2,$city,$st,$z,$n,$s)",("$no",number),("$t",x.Type),("$co",x.Company),("$f",x.First),("$l",x.Last),("$e",x.Email),("$p",x.Phone),("$a1",x.Address1),("$a2",x.Address2),("$city",x.City),("$st",x.State),("$z",x.Zip),("$n",x.Notes),("$s",x.Status));
   var id=Last(c,t);map.Add(source.LegacyId,id);Exec(c,t,"INSERT INTO legacy_client_map(legacy_id,legacy_number,client_id,batch_id) VALUES($l,$n,$i,1)",("$l",source.LegacyId),("$n",source.LegacyNumber),("$i",id));Audit(c,t,actor,"ImportLegacy","Client",id);
  }
  foreach(var source in p.Contacts){
   var x=source.Value;var parent=map[source.LegacyClientId];
   Exec(c,t,"INSERT INTO contacts(client_id,first_name,last_name,title,email,phone,is_primary) VALUES($c,$f,$l,$t,$e,$p,$b)",("$c",parent),("$f",x.First),("$l",x.Last),("$t",x.Title),("$e",x.Email),("$p",x.Phone),("$b",x.Primary?1:0));
   var id=Last(c,t);Exec(c,t,"INSERT INTO legacy_contact_map(legacy_id,legacy_client_id,contact_id,batch_id) VALUES($l,$p,$i,1)",("$l",source.LegacyId),("$p",source.LegacyClientId),("$i",id));Audit(c,t,actor,"ImportLegacy","Contact",id);
  }
  if(Exec(c,t,"UPDATE sequences SET next_value=$n WHERE name='Client'",("$n",next))!=1)throw new InvalidOperationException("Client sequence update failed.");
  if(Convert.ToInt64(Scalar(c,t,"SELECT count(*) FROM clients"))!=p.Clients.Count||Convert.ToInt64(Scalar(c,t,"SELECT count(*) FROM contacts"))!=p.Contacts.Count)throw new InvalidOperationException("Imported record counts do not reconcile.");
  var evidence=LegacyHistory(c,t);if(evidence==null||evidence.Sha256!=p.Sha256||evidence.Mappings.Count!=p.Clients.Count+p.Contacts.Count)throw new InvalidOperationException("Legacy mapping reconciliation failed.");
  Audit(c,t,actor,"CommitLegacyCrmImport","Firm",1);t.Commit();return new("Imported",p.Sha256,p.Clients.Count,p.Contacts.Count);
 }
}
