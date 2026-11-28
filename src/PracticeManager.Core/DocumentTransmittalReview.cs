using System.Security.Cryptography;
using System.Text.Json;
namespace PracticeManager.Core;

public sealed record ReviewedIssueDocument(long Id,string Sha256,string FileName,string Category,long Bytes);
public sealed record TransmittalIntent(long ProjectId,string Recipient,string Purpose,IReadOnlyList<ReviewedIssueDocument> Documents,string ReviewHash);
public sealed record TransmittalEvent(string State,string EvidenceRef,string Note,long Actor,string RecordedUtc,string? PreviousHash,string Hash);
public static class DocumentTransmittalReview
{
 static string Digest(string format,object value)=>Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new{Format=format,Value=value}))).ToLowerInvariant();
 static bool Hash(string h)=>h.Length==64&&h.All(char.IsAsciiHexDigit);
 static string Required(string v,int max)
 {
  if(string.IsNullOrWhiteSpace(v)||v.Length>max||v!=v.Trim()||v.Any(char.IsControl))throw new ArgumentException("Explicit bounded review information required.");
  return v;
 }
 public static TransmittalIntent Prepare(long project,string recipient,string purpose,IReadOnlyList<ReviewedIssueDocument> docs)
 {
  if(project<=0||docs.Count is <1 or >50||docs.Select(x=>x.Id).Distinct().Count()!=docs.Count)throw new ArgumentException("Exact project and unique reviewed documents required.");
  Required(recipient,200);Required(purpose,200);
  long total=0;var rows=docs.OrderBy(x=>x.Id).ToArray();
  foreach(var d in rows)
  {
   Required(d.FileName,180);
   if(d.Id<=0||!Hash(d.Sha256)||d.FileName.IndexOfAny(new[]{'/','\\',':'})>=0||
     !d.FileName.EndsWith(".pdf",StringComparison.OrdinalIgnoreCase)||d.Category is not ("Drawings" or "Calculations" or "Reports")||
     d.Bytes is <5 or >20971520||(total+=d.Bytes)>50*1024*1024)
    throw new ArgumentException("Invalid reviewed production PDF metadata.");
  }
  return new(project,recipient,purpose,Array.AsReadOnly(rows),Digest("CRC-TransmittalIntent-v1",new{ProjectId=project,Recipient=recipient,Purpose=purpose,Documents=rows}));
 }
 static string EventHash(string intent,TransmittalEvent e)=>Digest("CRC-TransmittalEvent-v1",new{Intent=intent,e.State,e.EvidenceRef,e.Note,e.Actor,e.RecordedUtc,e.PreviousHash});
 static bool Transition(string? prior,string next)=>prior switch
 {
  null=>next=="Prepared",
  "Prepared"=>next is "IssueRecorded" or "Withdrawn",
  "IssueRecorded"=>next is "DeliveryRecorded" or "Withdrawn",
  "DeliveryRecorded"=>next=="Withdrawn",
  _=>false
 };
 public static void Verify(TransmittalIntent intent,IReadOnlyList<TransmittalEvent> history)
 {
  if(Prepare(intent.ProjectId,intent.Recipient,intent.Purpose,intent.Documents).ReviewHash!=intent.ReviewHash||history.Count>4)
   throw new ArgumentException("Invalid transmittal review identity.");
  TransmittalEvent? prior=null;
  foreach(var e in history)
  {
   Required(e.EvidenceRef,200);Required(e.Note,1000);
   if(e.Actor<=0||!DateTimeOffset.TryParseExact(e.RecordedUtc,"O",System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.None,out var timestamp)||timestamp.Offset!=TimeSpan.Zero||
     (prior!=null&&string.CompareOrdinal(e.RecordedUtc,prior.RecordedUtc)<0)||
     !Transition(prior?.State,e.State)||e.PreviousHash!=prior?.Hash||EventHash(intent.ReviewHash,e)!=e.Hash)
    throw new ArgumentException("Invalid transmittal history.");
   prior=e;
  }
 }
 // These record types do not confer actual signing authority. The application
 // must establish the actor, permission and externally reviewed evidence.
 public static TransmittalEvent Append(TransmittalIntent intent,IReadOnlyList<TransmittalEvent> history,string? expected,
  string state,string evidence,string note,long actor,DateTimeOffset recordedUtc,bool confirmed)
 {
  Verify(intent,history);var prior=history.LastOrDefault();
  if(!confirmed||expected!=prior?.Hash||!Transition(prior?.State,state)||actor<=0||recordedUtc.Offset!=TimeSpan.Zero)
   throw new ArgumentException("Explicit reviewed evidence and current transmittal state required.");
  Required(evidence,200);Required(note,1000);
  var e=new TransmittalEvent(state,evidence,note,actor,recordedUtc.ToString("O",System.Globalization.CultureInfo.InvariantCulture),expected,"");
  e=e with{Hash=EventHash(intent.ReviewHash,e)};
  Verify(intent,history.Append(e).ToArray());return e;
 }
}
