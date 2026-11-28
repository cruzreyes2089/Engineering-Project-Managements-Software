using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using PracticeManager.Core;
namespace PracticeManager.Persistence;
public sealed record PartyDeclarationReview(long Id,long PartyId,long DocumentId,string DocumentHash,string Decision,string CurrentStatus,string Note,long ReviewerId,string ReviewedUtc);
public sealed partial class LocalPractice
{
 private static long PartyRevision(SqliteConnection c,SqliteTransaction t,long project)=>Convert.ToInt64(Scalar(c,t,"SELECT revision FROM project_party_evidence_revision WHERE project_id=$p",("$p",project)));
 public long ReadPartyEvidenceRevision(long project)
 {
  using var c=Open();using var t=c.BeginTransaction();RequireProjectView(c,t);Require(c,t,"Compliance.View");ProjectRow(c,t,project);return PartyRevision(c,t,project);
 }
 private long RequireDeclaration(SqliteConnection c,SqliteTransaction t,long project,long party,bool record)
 {
  RequirePartyView(c,t);RequireDocuments(c,t,false);Require(c,t,"Documents.Restricted");var actor=Require(c,t,"Compliance.View");if(record)Require(c,t,"Compliance.Record");ProjectRow(c,t,project);
  if(Convert.ToInt64(Scalar(c,t,"SELECT count(*) FROM project_parties WHERE id=$i AND project_id=$p",("$i",party),("$p",project)))!=1)throw new ArgumentException("Party unavailable for this project.");
  return actor;
 }
 public IReadOnlyList<PartyDeclarationReview> ReadPartyDeclarations(long project,long party)
 {
  using var c=Open();using var t=c.BeginTransaction();RequireDeclaration(c,t,project,party,false);
  using var q=Command(c,t,@"SELECT r.id,r.party_id,r.document_id,r.document_hash,r.decision,CASE WHEN x.id IS NULL THEN 'Historical' ELSE x.current_status END,r.note,r.reviewed_by,r.reviewed_utc FROM party_declaration_reviews r LEFT JOIN party_declaration_current x ON x.id=r.id WHERE r.party_id=$p ORDER BY r.id DESC LIMIT 501",("$p",party));using var r=q.ExecuteReader();var rows=new List<PartyDeclarationReview>();
  while(r.Read()){if(rows.Count==500)throw new ArgumentException("Declaration history exceeds development limit.");rows.Add(new(r.GetInt64(0),r.GetInt64(1),r.GetInt64(2),r.GetString(3),r.GetString(4),r.GetString(5),r.GetString(6),r.GetInt64(7),r.GetString(8)));}return rows.AsReadOnly();
 }
 public long RecordPartyDeclaration(long project,long party,long? expectedReview,long document,string expectedHash,string decision,string note,bool confirmed)
 {
  using var c=Open();using var t=c.BeginTransaction(deferred:false);var actor=RequireDeclaration(c,t,project,party,true);
  if(!confirmed||decision is not ("ReviewedNoConflict" or "Concern" or "Unverified"))throw new ArgumentException("Explicit review of declaration, signatures, parties and project scope required.");
  if(Convert.ToInt64(Scalar(c,t,"SELECT count(*) FROM project_party_retirements WHERE party_id=$p",("$p",party)))!=0)throw new ArgumentException("Retired party cannot receive a new declaration review.");
  var prior=Scalar(c,t,"SELECT id FROM party_declaration_current WHERE party_id=$p",("$p",party)) as long?;
  if(prior!=expectedReview)throw new ArgumentException("Declaration review changed; refresh.");
  var d=FindDocument(c,t,project,document);
  if(d==null||!d.IsCurrent||d.Hash!=expectedHash||d.Classification!="Restricted"||d.Category!="Contracts"||!d.FileName.EndsWith(".pdf",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Current restricted Contracts PDF and exact document identity required.");
  var value=Scalar(c,t,"SELECT file_bytes FROM project_documents WHERE id=$i",("$i",document));
  if(value is not byte[] bytes||!LooksLikePdf(bytes)||AgreementHash(bytes)!=expectedHash)throw new ArgumentException("Declaration PDF integrity or size invalid.");
  var evidence=Validation.Text(note,4000,"Declaration review evidence",true);
  Exec(c,t,"INSERT INTO party_declaration_reviews(party_id,document_id,document_hash,decision,note,reviewed_by,reviewed_utc) VALUES($p,$d,$h,$s,$n,$a,$u)",("$p",party),("$d",document),("$h",expectedHash),("$s",decision),("$n",evidence),("$a",actor),("$u",UtcNow()));
  var id=Last(c,t);Audit(c,t,actor,"ReviewPartyDeclaration","ProjectParty",party);t.Commit();return id;
 }
}
