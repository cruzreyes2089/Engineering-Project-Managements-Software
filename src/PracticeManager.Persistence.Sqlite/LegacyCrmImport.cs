using Microsoft.Data.Sqlite;
using PracticeManager.Core;
using System.Globalization;
namespace PracticeManager.Persistence;
public sealed record LegacyCrmPreview(string Sha256,int Clients,int Contacts,int DuplicateNameGroups,int DuplicateNumberGroups,string TargetStatus);
public sealed record LegacyCrmImportResult(string Outcome,string Sha256,int Clients,int Contacts);
public sealed partial class LocalPractice
{
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
  using var c=Open();using var t=c.BeginTransaction();RequireLegacyCrm(c,t);var p=LegacyCrmTransfer.Parse(data);
  return new(p.Sha256,p.Clients.Count,p.Contacts.Count,p.DuplicateNameGroups,p.DuplicateNumberGroups,LegacyCrmTarget(c,t,p.Sha256));
 }
 public LegacyCrmImportResult ImportLegacyCrm(byte[] data,string expectedHash,bool explicitlyConfirmed,string reviewNote)
 {
  using var c=Open();using var t=c.BeginTransaction(deferred:false);var actor=RequireLegacyCrm(c,t);var p=LegacyCrmTransfer.Parse(data);
  if(p.Sha256!=expectedHash||!explicitlyConfirmed)throw new ArgumentException("Exact preview hash and explicit import confirmation required.");
  var note=Validation.Text(reviewNote,1000,"Reviewed source mapping and counts",true);var status=LegacyCrmTarget(c,t,p.Sha256);
  if(status=="AlreadyImported"){
   if(Convert.ToInt64(Scalar(c,t,"SELECT count(*) FROM legacy_client_map"))!=p.Clients.Count||Convert.ToInt64(Scalar(c,t,"SELECT count(*) FROM legacy_contact_map"))!=p.Contacts.Count)throw new InvalidOperationException("Legacy mapping evidence incomplete.");
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
  Audit(c,t,actor,"CommitLegacyCrmImport","Firm",1);t.Commit();return new("Imported",p.Sha256,p.Clients.Count,p.Contacts.Count);
 }
}
