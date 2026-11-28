using Microsoft.Data.Sqlite;
using PracticeManager.Core;
namespace PracticeManager.Persistence;
public sealed partial class LocalPractice
{
 public static IReadOnlyList<string> ProjectPartyRoles{get;}=Array.AsReadOnly(new[]{"Client","Owner","Architect","Designer","Contractor","Consultant","Other"});
 private void RequirePartyView(SqliteConnection c,SqliteTransaction t){RequireProjectView(c,t);Require(c,t,"Contacts.View");}
 public IReadOnlyList<ProjectParty> ReadProjectParties(long project,bool includeRetired=false)
 {
  using var c=Open();using var t=c.BeginTransaction();RequirePartyView(c,t);ProjectRow(c,t,project);
  using var q=Command(c,t,@"SELECT p.id,p.client_id,p.contact_id,p.role,p.party_name,p.contact_name,CASE WHEN r.party_id IS NULL THEN 'Active' ELSE 'Retired' END,p.note,p.recorded_utc,r.note,r.recorded_utc FROM project_parties p LEFT JOIN project_party_retirements r ON r.party_id=p.id WHERE p.project_id=$p AND ($all=1 OR r.party_id IS NULL) ORDER BY p.id DESC LIMIT 501",("$p",project),("$all",includeRetired?1:0));
  using var r=q.ExecuteReader();var rows=new List<ProjectParty>();while(r.Read()){if(rows.Count==500)throw new ArgumentException("Project party history exceeds development limit.");rows.Add(new(r.GetInt64(0),r.GetInt64(1),r.IsDBNull(2)?null:r.GetInt64(2),r.GetString(3),r.GetString(4),r.GetString(5),r.GetString(6),r.GetString(7),r.GetString(8),r.IsDBNull(9)?null:r.GetString(9),r.IsDBNull(10)?null:r.GetString(10)));}return rows.AsReadOnly();
 }
 private static void InvalidateContactChecklist(SqliteConnection c,SqliteTransaction t,long actor,long project)
 {
  if(Exec(c,t,"UPDATE project_setup_items SET complete=0,note='Party register changed; review contacts again.',updated_by=$a,updated_utc=$u WHERE project_id=$p AND item='ProjectContacts' AND complete=1",("$a",actor),("$u",UtcNow()),("$p",project))==1){
   var setup=SetupReview(c,t,project);Exec(c,t,"UPDATE projects SET setup_state=$s WHERE id=$p",("$s",setup.State),("$p",project));Audit(c,t,actor,"InvalidateContactChecklist","Project",project);
  }
 }
 public long RegisterProjectParty(long project,long client,long? contact,string role,string note,bool confirmed)
 {
  using var c=Open();using var t=c.BeginTransaction(deferred:false);RequirePartyView(c,t);var actor=Require(c,t,"Projects.Setup");var p=ProjectRow(c,t,project);
  if(!confirmed||!ProjectPartyRoles.Contains(role)||role=="Client"&&client!=p.Client)throw new ArgumentException("Confirm the party and select a valid role. Client role must match accepted engagement.");
  ActiveClient(c,t,client);var evidence=Validation.Text(note,1000,"Party evidence",true);
  var name=Scalar(c,t,"SELECT CASE WHEN trim(company_name)!='' THEN company_name ELSE trim(first_name||' '||last_name) END FROM clients WHERE id=$i",("$i",client)) as string;
  if(string.IsNullOrWhiteSpace(name))throw new ArgumentException("Party identity unavailable.");
  string person="";
  if(contact.HasValue){person=Scalar(c,t,"SELECT trim(first_name||' '||last_name) FROM contacts WHERE id=$i AND client_id=$c",("$i",contact),("$c",client)) as string??"";if(string.IsNullOrWhiteSpace(person))throw new ArgumentException("Contact must belong to the selected party.");}
  if(Convert.ToInt64(Scalar(c,t,@"SELECT count(*) FROM project_parties p WHERE p.project_id=$p AND p.client_id=$c AND p.contact_id IS $i AND p.role=$r AND NOT EXISTS(SELECT 1 FROM project_party_retirements x WHERE x.party_id=p.id)",("$p",project),("$c",client),("$i",contact),("$r",role)))!=0)throw new ArgumentException("This party and role are already active.");
  Exec(c,t,"INSERT INTO project_parties(project_id,client_id,contact_id,role,party_name,contact_name,proposal_hash,note,recorded_by,recorded_utc) VALUES($p,$c,$i,$r,$n,$person,$h,$note,$a,$u)",("$p",project),("$c",client),("$i",contact),("$r",role),("$n",name),("$person",person),("$h",p.ProposalHash),("$note",evidence),("$a",actor),("$u",UtcNow()));
  var id=Last(c,t);Audit(c,t,actor,"RegisterParty","ProjectParty",id);InvalidateContactChecklist(c,t,actor,project);t.Commit();return id;
 }
 public string RetireProjectParty(long project,long party,string note,bool confirmed)
 {
  using var c=Open();using var t=c.BeginTransaction(deferred:false);RequirePartyView(c,t);var actor=Require(c,t,"Projects.Setup");ProjectRow(c,t,project);
  if(!confirmed)throw new ArgumentException("Confirm retirement.");
  if(Convert.ToInt64(Scalar(c,t,"SELECT count(*) FROM project_parties WHERE id=$i AND project_id=$p",("$i",party),("$p",project)))!=1)throw new ArgumentException("Party unavailable for this project.");
  if(Convert.ToInt64(Scalar(c,t,"SELECT count(*) FROM project_party_retirements WHERE party_id=$i",("$i",party)))==1)return "AlreadyRetired";
  var reason=Validation.Text(note,1000,"Retirement evidence",true);
  Exec(c,t,"INSERT INTO project_party_retirements(party_id,note,recorded_by,recorded_utc) VALUES($i,$n,$a,$u)",("$i",party),("$n",reason),("$a",actor),("$u",UtcNow()));
  Audit(c,t,actor,"RetireParty","ProjectParty",party);InvalidateContactChecklist(c,t,actor,project);t.Commit();return "Retired";
 }
}
