using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using PracticeManager.Core;
namespace PracticeManager.Persistence;
public sealed partial class LocalPractice
{
 private const int MaxAgreementBytes=10*1024*1024;
 private static readonly string[] SetupItems=["ScopeReviewed","ConflictReviewed","ProjectContacts","DeliveryPlan"];
 private static string AgreementHash(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes));
 private static bool LooksLikePdf(byte[] bytes)=>bytes.Length is >=8 and <=MaxAgreementBytes&&bytes.AsSpan(0,5).SequenceEqual("%PDF-"u8);
 private void RequireProjectView(SqliteConnection c,SqliteTransaction t){Require(c,t,"Projects.View");Require(c,t,"Clients.View");}
 private sealed record HandoffRow(string Number,string State,long Client,string ProposalHash,string Snapshot);
 private static HandoffRow ProjectRow(SqliteConnection c,SqliteTransaction t,long project)
 {
  using var q=Command(c,t,"SELECT p.project_no,p.state,s.client_id,s.snapshot_hash,s.snapshot FROM projects p JOIN engagements e ON e.id=p.engagement_id JOIN proposals s ON s.id=e.proposal_id WHERE p.id=$p AND s.state='Accepted'",("$p",project));
  using var r=q.ExecuteReader();
  if(!r.Read()||r.IsDBNull(3)||r.IsDBNull(4)||HashProposal(r.GetString(4))!=r.GetString(3))throw new ArgumentException("Accepted proposal snapshot unavailable or invalid.");
  return new(r.GetString(0),r.GetString(1),r.GetInt64(2),r.GetString(3),r.GetString(4));
 }
 private static AgreementEvidence? CurrentAgreement(SqliteConnection c,SqliteTransaction t,long project)
 {
  using var q=Command(c,t,"SELECT id,issued_hash,file_name,pdf_hash,recorded_utc FROM project_agreements WHERE project_id=$p ORDER BY id DESC LIMIT 1",("$p",project));using var r=q.ExecuteReader();
  return r.Read()?new(r.GetInt64(0),project,r.GetString(1),r.GetString(2),r.GetString(3),r.GetString(4)):null;
 }
 public ProjectHandoff ReadProjectHandoff(long project)
 {
  using var c=Open();using var t=c.BeginTransaction();RequireProjectView(c,t);Require(c,t,"Agreements.View");
  var p=ProjectRow(c,t,project);var a=CurrentAgreement(c,t,project);
  var authorized=Convert.ToInt64(Scalar(c,t,"SELECT count(*) FROM project_start_authorizations WHERE project_id=$p",("$p",project)))==1;
  return new(project,p.Number,OperationalState(c,t,project),p.ProposalHash,a,authorized);
 }
 public AgreementEvidence RecordSignedAgreement(long project,string expectedIssuedHash,string fileName,byte[] pdf,string reviewNote,bool manuallyReviewed)
 {
  using var c=Open();using var t=c.BeginTransaction(deferred:false);var actor=Require(c,t,"Agreements.Record");Require(c,t,"Agreements.View");RequireProjectView(c,t);
  var p=ProjectRow(c,t,project);if(p.State!="Pending"||p.ProposalHash!=expectedIssuedHash)throw new ArgumentException("Current pending project and accepted proposal required.");
  ActiveClient(c,t,p.Client);
  if(!manuallyReviewed)throw new ArgumentException("Explicit review of signatures, parties and terms is required.");
  var name=Validation.Text(fileName,180,"PDF file name",true);var note=Validation.Text(reviewNote,1000,"Review evidence",true);
  if(name.Any(ch=>char.IsControl(ch)||ch is '/' or '\\' or ':' or '<' or '>' or '"' or '|' or '?' or '*')||!name.EndsWith(".pdf",StringComparison.OrdinalIgnoreCase)||pdf==null||pdf.Length>MaxAgreementBytes)throw new ArgumentException("A PDF file name and bounded file are required.");
  var copy=pdf.ToArray();if(!LooksLikePdf(copy))throw new ArgumentException("PDF header required. The operator must verify the actual signed document.");
  var hash=AgreementHash(copy);var current=CurrentAgreement(c,t,project);
  if(current?.PdfHash==hash)return current;
  if(Convert.ToInt64(Scalar(c,t,"SELECT count(*) FROM project_agreements WHERE project_id=$p AND pdf_hash=$h",("$p",project),("$h",hash)))!=0)throw new ArgumentException("A superseded agreement cannot be silently restored.");
  var now=UtcNow();
  Exec(c,t,"INSERT INTO project_agreements(project_id,issued_hash,file_name,pdf_bytes,pdf_hash,review_note,recorded_by,recorded_utc) VALUES($p,$i,$f,$b,$h,$n,$a,$u)",("$p",project),("$i",p.ProposalHash),("$f",name),("$b",copy),("$h",hash),("$n",note),("$a",actor),("$u",now));
  var id=Last(c,t);Audit(c,t,actor,"RecordReviewedAgreement","ProjectAgreement",id);t.Commit();return new(id,project,p.ProposalHash,name,hash,now);
 }
 private static bool AgreementIntact(SqliteConnection c,SqliteTransaction t,AgreementEvidence a)
 {
  using var q=Command(c,t,"SELECT pdf_bytes FROM project_agreements WHERE id=$i AND project_id=$p",("$i",a.Id),("$p",a.ProjectId));
  var value=q.ExecuteScalar();return value is byte[] bytes&&LooksLikePdf(bytes)&&AgreementHash(bytes)==a.PdfHash;
 }
 public string AuthorizeProjectStart(long project,string expectedIssuedHash,long expectedAgreementId,string expectedPdfHash,bool explicitlyAuthorized,string note)
 {
  using var c=Open();using var t=c.BeginTransaction(deferred:false);var actor=Require(c,t,"Projects.Authorize");Require(c,t,"Agreements.View");RequireProjectView(c,t);
  var p=ProjectRow(c,t,project);
  if(!explicitlyAuthorized)return "NotAuthorized";
  var reason=Validation.Text(note,1000,"Authorization evidence",true);
  var a=CurrentAgreement(c,t,project);
  if(a==null||a.Id!=expectedAgreementId||a.IssuedHash!=p.ProposalHash||p.ProposalHash!=expectedIssuedHash||a.PdfHash!=expectedPdfHash||!AgreementIntact(c,t,a))return "AgreementEvidenceMismatch";
  if(p.State=="Active"&&OperationalState(c,t,project)=="OperationalClosed")return "OperationalClosed";
  if(p.State=="Active")return Convert.ToInt64(Scalar(c,t,"SELECT count(*) FROM project_start_authorizations WHERE project_id=$p AND agreement_id=$a",("$p",project),("$a",a.Id)))==1?"AlreadyActive":"HistoryInconsistent";
  ActiveClient(c,t,p.Client);
  if(!ConflictCleared(c,t,project))return "ConflictReviewRequired";
  Exec(c,t,"INSERT INTO project_start_authorizations(project_id,agreement_id,authorization_note,authorized_by,authorized_utc) VALUES($p,$i,$n,$a,$u)",("$p",project),("$i",a.Id),("$n",reason),("$a",actor),("$u",UtcNow()));
  if(Exec(c,t,"UPDATE projects SET state='Active' WHERE id=$p AND state='Pending'",("$p",project))!=1)throw new InvalidOperationException("Project changed.");
  Audit(c,t,actor,"AuthorizeStart","Project",project);t.Commit();return "Active";
 }
 private static ProjectSetupReview SetupReview(SqliteConnection c,SqliteTransaction t,long project)
 {
  var items=new Dictionary<string,ProjectSetupItem>(StringComparer.Ordinal);
  using var q=Command(c,t,"SELECT item,complete,note FROM project_setup_items WHERE project_id=$p",("$p",project));using var r=q.ExecuteReader();
  while(r.Read()){var item=r.GetString(0);items.Add(item,new(item,r.GetInt64(1)==1,r.GetString(2)));}
  var ordered=SetupItems.Select(i=>items.TryGetValue(i,out var row)?row:new ProjectSetupItem(i,false,"")).ToArray();
  var state=ordered.All(i=>i.Complete)?"Complete":items.Count==0?"NotStarted":"InProgress";
  return new(project,state,Array.AsReadOnly(ordered));
 }
 public ProjectSetupReview ReadProjectSetup(long project)
 {
  using var c=Open();using var t=c.BeginTransaction();RequireProjectView(c,t);ProjectRow(c,t,project);return SetupReview(c,t,project);
 }
 public ProjectSetupReview RecordProjectSetupItem(long project,string item,bool complete,string note,bool expectedComplete,string expectedNote)
 {
  using var c=Open();using var t=c.BeginTransaction(deferred:false);var actor=Require(c,t,"Projects.Setup");RequireProjectView(c,t);ProjectRow(c,t,project);
  if(!SetupItems.Contains(item))throw new ArgumentException("Unknown checklist item.");
  var current=SetupReview(c,t,project).Items.Single(x=>x.Item==item);
  if(current.Complete!=expectedComplete||current.Note!=expectedNote)throw new ArgumentException("Checklist changed. Refresh.");
  var reason=Validation.Text(note,1000,"Checklist evidence",true);
  Exec(c,t,"INSERT INTO project_setup_items(project_id,item,complete,note,updated_by,updated_utc) VALUES($p,$i,$c,$n,$a,$u) ON CONFLICT(project_id,item) DO UPDATE SET complete=excluded.complete,note=excluded.note,updated_by=excluded.updated_by,updated_utc=excluded.updated_utc",("$p",project),("$i",item),("$c",complete?1:0),("$n",reason),("$a",actor),("$u",UtcNow()));
  var next=SetupReview(c,t,project);Exec(c,t,"UPDATE projects SET setup_state=$s WHERE id=$p",("$s",next.State),("$p",project));Audit(c,t,actor,"RecordSetupItem","Project",project);t.Commit();return next;
 }
}
