using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PracticeManager.Core;
namespace PracticeManager.Persistence;
public sealed partial class LocalPractice
{
 private static (string Title,string Site,string Scope,long Cents) ValidateProposal(ProposalInput input)
 {
  if(input.Fee<0||input.Fee>9999999999.99m||decimal.Round(input.Fee,2)!=input.Fee)throw new ArgumentException("Fee must be a nonnegative USD amount in cents.");
  return (Validation.Text(input.Title,200,"Project title",true),Validation.Text(input.Site,300,"Project site",true),Validation.Text(input.Scope,20000,"Scope",true),decimal.ToInt64(input.Fee*100));
 }
 private static void ActiveClient(SqliteConnection c,SqliteTransaction t,long id)
 {
  if(Convert.ToString(Scalar(c,t,"SELECT status FROM clients WHERE id=$i",("$i",id)))!="Active")throw new ArgumentException("Select an active client.");
 }
 private static string NextNumber(SqliteConnection c,SqliteTransaction t,string sequence,string prefix)
 {
  var n=Convert.ToInt64(Scalar(c,t,"SELECT next_value FROM sequences WHERE name=$n",("$n",sequence)));
  if(n<=0||n==long.MaxValue)throw new InvalidOperationException("Number sequence unavailable.");
  if(Exec(c,t,"UPDATE sequences SET next_value=next_value+1 WHERE name=$n",("$n",sequence))!=1)throw new InvalidOperationException("Number sequence unavailable.");
  return prefix+"-"+n.ToString("D6",CultureInfo.InvariantCulture);
 }
 public IReadOnlyList<ProposalSummary> SearchProposals(string search="")
 {
  using var c=Open();using var t=c.BeginTransaction();Require(c,t,"Engagements.View");Require(c,t,"Clients.View");
  using var q=Command(c,t,@"SELECT p.id,p.proposal_no,p.client_id,CASE WHEN trim(c.company_name)!='' THEN c.company_name ELSE trim(c.first_name||' '||c.last_name) END,p.title,p.site,p.scope,p.fee_cents,p.revision,p.state,p.snapshot_hash FROM proposals p JOIN clients c ON c.id=p.client_id WHERE $s='' OR instr(lower(p.proposal_no||' '||p.title||' '||p.site||' '||c.company_name||' '||c.first_name||' '||c.last_name),lower($s))>0 ORDER BY p.id DESC LIMIT 501",("$s",Validation.Text(search,150,"Search")));
  using var r=q.ExecuteReader();var rows=new List<ProposalSummary>();
  while(r.Read()){
   if(rows.Count==500)throw new ArgumentException("More than 500 proposals match. Narrow the search.");
   rows.Add(new(r.GetInt64(0),r.GetString(1),r.GetInt64(2),r.GetString(3),r.GetString(4),r.GetString(5),r.GetString(6),r.GetInt64(7)/100m,r.GetInt32(8),r.GetString(9),r.IsDBNull(10)?null:r.GetString(10)));
  }return rows.AsReadOnly();
 }
 public long CreateProposal(ProposalInput input)
 {
  using var c=Open();using var t=c.BeginTransaction(deferred:false);var actor=Require(c,t,"Engagements.Edit");Require(c,t,"Engagements.View");Require(c,t,"Clients.View");
  ActiveClient(c,t,input.ClientId);var p=ValidateProposal(input);var number=NextNumber(c,t,"Proposal","P");
  Exec(c,t,"INSERT INTO proposals(proposal_no,client_id,title,site,scope,fee_cents,revision,state,created_by,created_utc) VALUES($n,$c,$title,$site,$scope,$fee,1,'Draft',$a,$u)",("$n",number),("$c",input.ClientId),("$title",p.Title),("$site",p.Site),("$scope",p.Scope),("$fee",p.Cents),("$a",actor),("$u",UtcNow()));
  var id=Last(c,t);Audit(c,t,actor,"Create","Proposal",id);t.Commit();return id;
 }
 public void EditProposal(long id,int expectedRevision,ProposalInput input)
 {
  using var c=Open();using var t=c.BeginTransaction(deferred:false);var actor=Require(c,t,"Engagements.Edit");Require(c,t,"Engagements.View");Require(c,t,"Clients.View");
  var p=ValidateProposal(input);ActiveClient(c,t,input.ClientId);
  if(Exec(c,t,"UPDATE proposals SET title=$title,site=$site,scope=$scope,fee_cents=$fee,revision=revision+1 WHERE id=$i AND client_id=$c AND state='Draft' AND revision=$r",("$title",p.Title),("$site",p.Site),("$scope",p.Scope),("$fee",p.Cents),("$i",id),("$c",input.ClientId),("$r",expectedRevision))!=1)throw new ArgumentException("Draft changed or is no longer editable. Refresh.");
  Audit(c,t,actor,"Update","Proposal",id);t.Commit();
 }
 private static string HashProposal(string snapshot)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(snapshot)));
 private static (long Client,string Snapshot) DraftProposalSnapshot(SqliteConnection c,SqliteTransaction t,long id,int expectedRevision)
 {
  string snapshot;long client;
  using(var q=Command(c,t,@"SELECT p.proposal_no,p.client_id,p.title,p.site,p.scope,p.fee_cents,p.revision,c.company_name,c.first_name,c.last_name FROM proposals p JOIN clients c ON c.id=p.client_id WHERE p.id=$i AND p.state='Draft' AND p.revision=$r",("$i",id),("$r",expectedRevision)))
  using(var r=q.ExecuteReader()){
   if(!r.Read())throw new ArgumentException("Current draft required. Refresh.");
   client=r.GetInt64(1);
   snapshot=JsonSerializer.Serialize(new{Format="CRC-Proposal-Snapshot-v1",Number=r.GetString(0),ClientId=client,Title=r.GetString(2),Site=r.GetString(3),Scope=r.GetString(4),FeeCents=r.GetInt64(5),Currency="USD",Revision=r.GetInt32(6),Company=r.GetString(7),First=r.GetString(8),Last=r.GetString(9)});
  }
  return (client,snapshot);
 }
 public string RecordProposalIssued(long id,int expectedRevision)
 {
  using var c=Open();using var t=c.BeginTransaction(deferred:false);var actor=Require(c,t,"Engagements.Issue");Require(c,t,"Engagements.View");Require(c,t,"Clients.View");
  var (client,snapshot)=DraftProposalSnapshot(c,t,id,expectedRevision);
  ActiveClient(c,t,client);var hash=HashProposal(snapshot);
  if(!ProposalConflictCleared(c,t,id,expectedRevision,hash))throw new ArgumentException("Current proposal conflict review required before issue.");
  if(Exec(c,t,"UPDATE proposals SET state='Issued',snapshot=$s,snapshot_hash=$h,issued_by=$a,issued_utc=$u WHERE id=$i AND state='Draft' AND revision=$r",("$s",snapshot),("$h",hash),("$a",actor),("$u",UtcNow()),("$i",id),("$r",expectedRevision))!=1)throw new InvalidOperationException("Draft changed.");
  Audit(c,t,actor,"RecordIssued","Proposal",id);t.Commit();return hash;
 }
 public string ReadIssuedProposalSnapshot(long id)
 {
  using var c=Open();using var t=c.BeginTransaction();Require(c,t,"Engagements.View");Require(c,t,"Clients.View");
  using var q=Command(c,t,"SELECT snapshot,snapshot_hash FROM proposals WHERE id=$i AND state!='Draft'",("$i",id));using var r=q.ExecuteReader();
  if(!r.Read()||r.IsDBNull(0)||r.IsDBNull(1)||HashProposal(r.GetString(0))!=r.GetString(1))throw new ArgumentException("Verified issued snapshot unavailable.");
  return r.GetString(0);
 }
 public ProposalDecision RecordProposalDecision(long id,string expectedHash,bool accepted,string evidenceNote)
 {
  using var c=Open();using var t=c.BeginTransaction(deferred:false);var actor=Require(c,t,"Engagements.Accept");Require(c,t,"Engagements.View");Require(c,t,"Clients.View");if(accepted)Require(c,t,"Projects.View");
  var note=Validation.Text(evidenceNote,1000,"Decision evidence",true);
  string state,snapshot,hash;long client;
  using(var q=Command(c,t,"SELECT state,snapshot,snapshot_hash,client_id FROM proposals WHERE id=$i",("$i",id)))
  using(var r=q.ExecuteReader()){
   if(!r.Read()||r.IsDBNull(1)||r.IsDBNull(2))return new("IssuedProposalRequired");
   state=r.GetString(0);snapshot=r.GetString(1);hash=r.GetString(2);client=r.GetInt64(3);
  }
  if(hash!=expectedHash||HashProposal(snapshot)!=hash)return new("SnapshotMismatch");
  if(state=="Accepted"&&accepted){
   using var q=Command(c,t,"SELECT p.id,p.project_no FROM projects p JOIN engagements e ON e.id=p.engagement_id WHERE e.proposal_id=$i",("$i",id));using var r=q.ExecuteReader();
   return r.Read()?new("AlreadyAccepted",r.GetInt64(0),r.GetString(1)):new("HistoryInconsistent");
  }
  if(state!="Issued")return new("DecisionAlreadyRecorded");
  if(accepted)ActiveClient(c,t,client);
  if(Exec(c,t,"UPDATE proposals SET state=$s,decision_by=$a,decision_utc=$u,decision_note=$n WHERE id=$i AND state='Issued'",("$s",accepted?"Accepted":"Rejected"),("$a",actor),("$u",UtcNow()),("$n",note),("$i",id))!=1)throw new InvalidOperationException("Proposal changed.");
  Audit(c,t,actor,accepted?"RecordAccepted":"RecordRejected","Proposal",id);
  if(!accepted){t.Commit();return new("Rejected");}
  Exec(c,t,"INSERT INTO engagements(proposal_id,created_by,created_utc) VALUES($i,$a,$u)",("$i",id),("$a",actor),("$u",UtcNow()));var engagement=Last(c,t);Audit(c,t,actor,"Create","Engagement",engagement);
  var number=NextNumber(c,t,"Project","PRJ");
  Exec(c,t,"INSERT INTO projects(project_no,engagement_id,state,setup_state,created_by,created_utc) VALUES($n,$e,'Pending','NotStarted',$a,$u)",("$n",number),("$e",engagement),("$a",actor),("$u",UtcNow()));var project=Last(c,t);Audit(c,t,actor,"CreatePending","Project",project);
  t.Commit();return new("Accepted",project,number);
 }
 // Historical method name retained for compatibility; register now includes both Pending and Active projects.
 public (long Pending,long Active) ReadProjectDashboardCounts()
 {
  using var c=Open();using var t=c.BeginTransaction(deferred:true);RequireProjectView(c,t);
  using var q=Command(c,t,"SELECT count(CASE WHEN v.state='Pending' THEN 1 END),count(CASE WHEN v.state='Active' THEN 1 END) FROM project_operational_state v");using var reader=q.ExecuteReader();
  if(!reader.Read())throw new InvalidOperationException("Project counts unavailable.");
  return(reader.GetInt64(0),reader.GetInt64(1));
 }
 public IReadOnlyList<PendingProject> SearchPendingProjects(string search="",string state="All")
 {
  using var c=Open();using var t=c.BeginTransaction();Require(c,t,"Projects.View");Require(c,t,"Clients.View");
  if(state is not ("All" or "Pending" or "Active" or "OperationalClosed"))throw new ArgumentException("Select a valid project status.");
  using var q=Command(c,t,@"SELECT p.id,p.project_no,s.id,s.title,CASE WHEN trim(c.company_name)!='' THEN c.company_name ELSE trim(c.first_name||' '||c.last_name) END,s.site,(SELECT v.state FROM project_operational_state v WHERE v.project_id=p.id),p.setup_state,CASE WHEN EXISTS(SELECT 1 FROM project_conflict_cleared cr WHERE cr.project_id=p.id) THEN 'Clear' WHEN EXISTS(SELECT 1 FROM project_conflict_current cr WHERE cr.project_id=p.id) THEN 'Hold' ELSE 'NotReviewed' END FROM projects p JOIN engagements e ON e.id=p.engagement_id JOIN proposals s ON s.id=e.proposal_id JOIN clients c ON c.id=s.client_id WHERE ($state='All' OR (SELECT v.state FROM project_operational_state v WHERE v.project_id=p.id)=$state) AND ($s='' OR instr(lower(p.project_no||' '||s.title||' '||s.site||' '||c.company_name||' '||c.first_name||' '||c.last_name),lower($s))>0) ORDER BY p.id DESC LIMIT 501",("$s",Validation.Text(search,150,"Search")),("$state",state));
  using var r=q.ExecuteReader();var rows=new List<PendingProject>();while(r.Read()){
   if(rows.Count==500)throw new ArgumentException("More than 500 projects match. Narrow the search.");
   rows.Add(new(r.GetInt64(0),r.GetString(1),r.GetInt64(2),r.GetString(3),r.GetString(4),r.GetString(5),r.GetString(6),r.GetString(7),r.GetString(8)));
  }return rows.AsReadOnly();
 }
}
