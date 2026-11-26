using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PracticeManager.Core;
namespace PracticeManager.Persistence;
public sealed record GeneratedReviewDocument(long Id,string FileName,string SourceHash,string Sha256,byte[] Bytes)
{
 public override string ToString()=>"[Confidential generated document]";
}
public sealed record GeneratedReviewMetadata(long Id,string FileName,string SourceHash,string Sha256,long Bytes,string CreatedUtc);
public sealed partial class LocalPractice
{
 private static string ReviewAmount(decimal amount)=>amount.ToString("0.00",CultureInfo.InvariantCulture)+" USD";
 private static string ReviewClient(JsonElement source)
 {
  var company=source.GetProperty("Company").GetString()??"";
  var person=((source.GetProperty("First").GetString()??"")+" "+(source.GetProperty("Last").GetString()??"")).Trim();
  return string.IsNullOrWhiteSpace(company)?person:string.IsNullOrWhiteSpace(person)?company:company+" — "+person;
 }
 private static GeneratedReviewDocument StoredReview(SqliteDataReader r)
 {
  var bytes=(byte[])r.GetValue(4);var hash=r.GetString(3);
  if(bytes.Length is <1 or >2097152||Convert.ToHexString(SHA256.HashData(bytes))!=hash)throw new InvalidOperationException("Generated document integrity failed.");
  return new(r.GetInt64(0),r.GetString(1),r.GetString(2),hash,bytes);
 }
 private static GeneratedReviewDocument RegisterReviewDocument(SqliteConnection c,SqliteTransaction t,long actor,long? proposal,long? invoice,string sourceHash,string fileName,string heading,IReadOnlyList<ReviewParagraph> paragraphs)
 {
  // Existing evidence is returned exactly as originally registered; never regenerate and replace.
  GeneratedReviewDocument? saved=null;
  using(var q=Command(c,t,"SELECT id,file_name,source_hash,file_hash,file_bytes FROM generated_review_documents WHERE proposal_id IS $p AND invoice_draft_id IS $i AND source_hash=$h AND generator='ReviewDocx-v1'",("$p",proposal),("$i",invoice),("$h",sourceHash)))
  using(var r=q.ExecuteReader()){if(r.Read())saved=StoredReview(r);}
  if(saved!=null){Audit(c,t,actor,"RetrieveReviewDocument","GeneratedReviewDocument",saved.Id);return saved;}
  var bytes=ReviewDocx.Create(heading,paragraphs);var hash=Convert.ToHexString(SHA256.HashData(bytes));
  Exec(c,t,"INSERT INTO generated_review_documents(proposal_id,invoice_draft_id,source_hash,generator,file_name,file_bytes,file_hash,created_by,created_utc) VALUES($p,$i,$s,'ReviewDocx-v1',$f,$b,$h,$a,$u)",("$p",proposal),("$i",invoice),("$s",sourceHash),("$f",fileName),("$b",bytes),("$h",hash),("$a",actor),("$u",UtcNow()));
  var id=Last(c,t);Audit(c,t,actor,"RegisterReviewDocument","GeneratedReviewDocument",id);return new(id,fileName,sourceHash,hash,bytes);
 }
 private static void RequireProposalDocumentSource(SqliteConnection c,SqliteTransaction t,long id)
 {
  if(Convert.ToInt64(Scalar(c,t,"SELECT count(*) FROM proposals WHERE id=$i",("$i",id)))!=1)throw new ArgumentException("Proposal unavailable.");
 }
 private static IReadOnlyList<GeneratedReviewMetadata> ReviewMetadata(SqliteConnection c,SqliteTransaction t,long? proposal,long? invoice)
 {
  using var q=Command(c,t,"SELECT id,file_name,source_hash,file_hash,length(file_bytes),created_utc FROM generated_review_documents WHERE proposal_id IS $p AND invoice_draft_id IS $i ORDER BY id DESC LIMIT 501",("$p",proposal),("$i",invoice));using var r=q.ExecuteReader();var rows=new List<GeneratedReviewMetadata>();
  while(r.Read()){if(rows.Count==500)throw new ArgumentException("Generated review history exceeds development limit.");rows.Add(new(r.GetInt64(0),r.GetString(1),r.GetString(2),r.GetString(3),r.GetInt64(4),r.GetString(5)));}return rows.AsReadOnly();
 }
 private static GeneratedReviewDocument RetrieveReview(SqliteConnection c,SqliteTransaction t,long actor,long? proposal,long? invoice,long document,string expectedHash)
 {
  GeneratedReviewDocument result;
  using(var q=Command(c,t,"SELECT id,file_name,source_hash,file_hash,file_bytes FROM generated_review_documents WHERE id=$d AND proposal_id IS $p AND invoice_draft_id IS $i",("$d",document),("$p",proposal),("$i",invoice)))
  using(var r=q.ExecuteReader()){if(!r.Read())throw new ArgumentException("Review document unavailable for selected source.");result=StoredReview(r);}
  if(result.Sha256!=expectedHash)throw new ArgumentException("Review document identity changed.");
  Audit(c,t,actor,"RetrieveReviewDocument","GeneratedReviewDocument",result.Id);return result;
 }
 public IReadOnlyList<GeneratedReviewMetadata> ReadProposalReviewDocuments(long proposal)
 {
  using var c=Open();using var t=c.BeginTransaction();Require(c,t,"Engagements.Export");Require(c,t,"Engagements.View");Require(c,t,"Clients.View");RequireProposalDocumentSource(c,t,proposal);
  return ReviewMetadata(c,t,proposal,null);
 }
 public GeneratedReviewDocument RetrieveProposalReviewDocument(long proposal,long document,string hash)
 {
  using var c=Open();using var t=c.BeginTransaction(deferred:false);var actor=Require(c,t,"Engagements.Export");Require(c,t,"Engagements.View");Require(c,t,"Clients.View");RequireProposalDocumentSource(c,t,proposal);
  var result=RetrieveReview(c,t,actor,proposal,null,document,hash);t.Commit();return result;
 }
 public IReadOnlyList<GeneratedReviewMetadata> ReadInvoiceReviewDocuments(long project,long invoice)
 {
  using var c=Open();using var t=c.BeginTransaction();RequireInvoices(c,t,"Billing.Invoices.Export");ProjectRow(c,t,project);
  var d=InvoiceDetail(c,t,project,invoice);if(d.Summary.State!="Approved")throw new ArgumentException("Approved internal draft required.");
  return ReviewMetadata(c,t,null,invoice);
 }
 public GeneratedReviewDocument RetrieveInvoiceReviewDocument(long project,long invoice,long document,string hash)
 {
  using var c=Open();using var t=c.BeginTransaction(deferred:false);var actor=RequireInvoices(c,t,"Billing.Invoices.Export");ProjectRow(c,t,project);
  var d=InvoiceDetail(c,t,project,invoice);if(d.Summary.State!="Approved")throw new ArgumentException("Approved internal draft required.");
  var result=RetrieveReview(c,t,actor,null,invoice,document,hash);t.Commit();return result;
 }
 public GeneratedReviewDocument PrepareProposalReviewDocx(long id,int expectedRevision,string? expectedIssuedHash)
 {
  using var c=Open();using var t=c.BeginTransaction(deferred:false);var actor=Require(c,t,"Engagements.Export");Require(c,t,"Engagements.View");Require(c,t,"Clients.View");
  string state;string? snapshot,storedHash;
  using(var q=Command(c,t,"SELECT state,snapshot,snapshot_hash FROM proposals WHERE id=$i AND revision=$r",("$i",id),("$r",expectedRevision)))
  using(var r=q.ExecuteReader()){if(!r.Read())throw new ArgumentException("Current proposal revision required.");state=r.GetString(0);snapshot=r.IsDBNull(1)?null:r.GetString(1);storedHash=r.IsDBNull(2)?null:r.GetString(2);}
  if(state=="Draft"){
   if(expectedIssuedHash!=null)throw new ArgumentException("Unexpected issued identity.");
   var draft=DraftProposalSnapshot(c,t,id,expectedRevision);ActiveClient(c,t,draft.Client);snapshot=draft.Snapshot;storedHash=HashProposal(snapshot);
   if(!ProposalConflictCleared(c,t,id,expectedRevision,storedHash))throw new ArgumentException("Current conflict screening required before preparing a proposal review copy.");
  }else if(snapshot==null||storedHash==null||storedHash!=expectedIssuedHash||HashProposal(snapshot)!=storedHash)throw new ArgumentException("Frozen proposal identity changed.");
  if(snapshot==null||storedHash==null)throw new InvalidOperationException("Missing proposal evidence.");
  using var json=JsonDocument.Parse(snapshot);var root=json.RootElement;
  string Text(string field)=>root.GetProperty(field).GetString()??"";
  var number=Text("Number");var firm=Convert.ToString(Scalar(c,t,"SELECT name FROM firms WHERE id=1"))??"";
  var rows=new List<ReviewParagraph>{new(firm,true),new("Proposal "+number+" • Revision "+expectedRevision.ToString(CultureInfo.InvariantCulture)),new("Client",true),new(ReviewClient(root)),new("Project and location",true),new(Text("Title")),new(Text("Site")),new("Scope of services",true),new(Text("Scope")),new("Proposed fee",true),new(ReviewAmount(root.GetProperty("FeeCents").GetInt64()/100m)),new("This summary is not a contract, signed agreement, authorization to start work or evidence of client delivery. Approved agreement terms are not included."),new("Source SHA-256: "+storedHash)};
  var result=RegisterReviewDocument(c,t,actor,id,null,storedHash,number+"-review-r"+expectedRevision.ToString(CultureInfo.InvariantCulture)+".docx","PROPOSAL SUMMARY",rows);t.Commit();return result;
 }
 public GeneratedReviewDocument PrepareInvoiceReviewDocx(long project,long id,string expectedHash)
 {
  using var c=Open();using var t=c.BeginTransaction(deferred:false);var actor=RequireInvoices(c,t,"Billing.Invoices.Export");var p=ProjectRow(c,t,project);var d=InvoiceDetail(c,t,project,id);
  if(d.Summary.PayloadHash!=expectedHash||d.Summary.State!="Approved")throw new ArgumentException("Exact approved internal draft required.");
  using var json=JsonDocument.Parse(p.Snapshot);
  var firm=Convert.ToString(Scalar(c,t,"SELECT name FROM firms WHERE id=1"))??"";
  var rows=new List<ReviewParagraph>{new(firm,true),new("NOT AN ISSUED INVOICE — NO PAYMENT REQUEST",true),new("Internal draft: "+d.Summary.Number),new("Project: "+p.Number),new("Client: "+ReviewClient(json.RootElement)),new("Draft date: "+SafeCsv.Date(d.Summary.InvoiceDate)),new("Proposed due date: "+SafeCsv.Date(d.Summary.DueDate)),new("Approved draft lines",true)};
  foreach(var l in d.Lines){rows.Add(new(l.Kind+" — "+l.Description));rows.Add(new((l.Minutes.HasValue?l.Minutes.Value.ToString(CultureInfo.InvariantCulture)+" min × "+ReviewAmount(l.BillingRate!.Value)+"/hour; ":"")+"Amount: "+ReviewAmount(l.Amount)));}
  rows.Add(new("Internal draft total: "+ReviewAmount(d.Summary.Total),true));rows.Add(new("Manager remains authoritative for issued invoices, tax, payments and balances. This review copy creates no accounting transaction."));rows.Add(new("Source SHA-256: "+expectedHash));
  var result=RegisterReviewDocument(c,t,actor,null,id,expectedHash,d.Summary.Number+"-review.docx","INVOICE PREPARATION",rows);t.Commit();return result;
 }
}
