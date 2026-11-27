using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PracticeManager.Core;
namespace PracticeManager.Persistence;

public sealed record ReleaseReviewSelection(long DocumentId,string Hash);
public sealed record ProjectReleaseReviewPackage(long ProjectId,int DocumentCount,string Sha256,byte[] Bytes)
{
 public override string ToString()=>"[Confidential internal release review package]";
}
public sealed partial class LocalPractice
{
 // Not an issuance authorization or delivery event. No restricted documents,
 // contracts, native CAD files or generated agreement/invoice copies.
 public ProjectReleaseReviewPackage PrepareProjectReleaseReview(long project,IReadOnlyList<ReleaseReviewSelection> selection,
  string purpose,string recipient,string reviewNote,bool confirmed)
 {
  if(!confirmed||selection.Count is <1 or >50)throw new ArgumentException("Explicit review and 1–50 selected production PDF versions required.");
  var chosen=selection.OrderBy(x=>x.DocumentId).ToArray();
  if(chosen.Select(x=>x.DocumentId).Distinct().Count()!=chosen.Length)throw new ArgumentException("Duplicate document selection.");
  purpose=Validation.Text(purpose,200,"Proposed issue purpose",true);
  recipient=Validation.Text(recipient,200,"Proposed recipient",true);
  reviewNote=Validation.Text(reviewNote,1000,"Review note",true);
  using var c=Open();using var t=c.BeginTransaction(deferred:false);
  var actor=RequireDocuments(c,t,true);RequireAdministration(c,t);RequireReleaseReviewEligibility(c,t,project);var p=ProjectRow(c,t,project);
  var metadata=new List<object>();long total=0;using var output=new MemoryStream();
  using(var zip=new ZipArchive(output,ZipArchiveMode.Create,true))
  {
   foreach(var item in chosen)
   {
    var d=FindDocument(c,t,project,item.DocumentId);
    if(d==null||d.Classification!="Production")throw new UnauthorizedAccessException("Selected document unavailable for production review.");
    if(!d.IsCurrent||d.Hash!=item.Hash||d.Category is not ("Drawings" or "Calculations" or "Reports")||
      !string.Equals(Path.GetExtension(d.FileName),".pdf",StringComparison.OrdinalIgnoreCase))
     throw new ArgumentException("Only exact current production drawing, calculation or report PDF versions are eligible.");
    if(d.Bytes is <5 or >MaxProjectDocumentBytes||(total+=d.Bytes)>50*1024*1024)throw new ArgumentException("Package exceeds 50 MiB document limit.");
    var bytes=Scalar(c,t,"SELECT file_bytes FROM project_documents WHERE id=$i AND project_id=$p",("$i",d.Id),("$p",project)) as byte[];
    if(bytes==null||bytes.LongLength!=d.Bytes||!bytes.AsSpan(0,5).SequenceEqual("%PDF-"u8)||Convert.ToHexString(SHA256.HashData(bytes))!=d.Hash)
     throw new InvalidOperationException("Selected PDF bytes failed integrity or header check.");
    // Fixed generated entry names; never embed a user-controlled path.
    var entryName="document-"+d.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)+".pdf";
    var entry=zip.CreateEntry(entryName,CompressionLevel.NoCompression);
    using(var stream=entry.Open())stream.Write(bytes);
    metadata.Add(new{DocumentId=d.Id,OriginalName=d.FileName,Entry=entryName,Category=d.Category,Sha256=d.Hash,Bytes=d.Bytes});
    Audit(c,t,actor,"RetrieveForReleaseReview","ProjectDocument",d.Id);
   }
   var manifest=zip.CreateEntry("REVIEW-ONLY.json",CompressionLevel.NoCompression);
   using var manifestStream=manifest.Open();
   JsonSerializer.Serialize(manifestStream,new{Format="CRC-ProjectReleaseReview-v1",Status="INTERNAL REVIEW ONLY — NOT ISSUED OR DELIVERED",
    ProjectId=project,ProjectNumber=p.Number,ProposedPurpose=purpose,ProposedRecipient=recipient,ReviewNote=reviewNote,
    PreparedBy=actor,PreparedUtc=UtcNow(),Documents=metadata});
  }
  var result=output.ToArray();
  if(result.Length>64*1024*1024)throw new InvalidOperationException("Package exceeds output limit.");
  Audit(c,t,actor,"PrepareReleaseReview","Project",project);t.Commit();
  return new(project,chosen.Length,Convert.ToHexString(SHA256.HashData(result)),result);
 }
 private void RequireReleaseReviewEligibility(SqliteConnection c,SqliteTransaction t,long project)
 {
  var p=ProjectRow(c,t,project);
  RequireOpenOperations(c,t,project);
  if(p.State!="Active"||!ConflictCleared(c,t,project))throw new ArgumentException("Active project with current conflict clearance required.");
  var a=CurrentAgreement(c,t,project);
  if(a==null||a.IssuedHash!=p.ProposalHash||!AgreementIntact(c,t,a)||
     Convert.ToInt64(Scalar(c,t,"SELECT count(*) FROM project_start_authorizations WHERE project_id=$p AND agreement_id=$a",("$p",project),("$a",a.Id)))!=1)
   throw new ArgumentException("Intact project start authorization required.");
  if(Convert.ToInt64(Scalar(c,t,"SELECT count(*) FROM project_quality_items i WHERE i.project_id=$p AND NOT EXISTS(SELECT 1 FROM project_quality_closures x WHERE x.item_id=i.id)",("$p",project)))!=0)
   throw new ArgumentException("Resolve open project QA items before preparing a release review package.");
 }

}
