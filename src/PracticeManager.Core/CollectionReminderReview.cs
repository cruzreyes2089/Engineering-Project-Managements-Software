using System.Globalization;
namespace PracticeManager.Core;
public sealed record ReminderReview(string Outcome,string? Subject=null,string? Body=null,DateTimeOffset? ObservedUtc=null)
{
 public bool MaySend=>false;
 public override string ToString()=>"[Synthetic reminder review: "+Outcome+"]";
}
public static class CollectionReminderReview
{
 // Requires a newly acquired observation of the same dedicated synthetic scope.
 // No recipient, delivery capability, invoice state change or payment instructions.
 public static ReminderReview Prepare(CollectionPreview previous,CollectionPreview fresh,Guid invoiceKey,DateOnly day,DateTimeOffset now)
 {
  if(previous.Outcome!="ObservedForReview"||fresh.Outcome!="ObservedForReview"||previous.DueFixtureMarker is not Guid marker||marker==Guid.Empty||fresh.DueFixtureMarker!=marker||previous.DueDates==null||fresh.DueDates==null||previous.ObservedUtc==null||fresh.ObservedUtc==null)return new("CompleteSameFixtureEvidenceRequired");
  if(fresh.ObservedUtc.Value<=previous.ObservedUtc.Value||fresh.ObservedUtc.Value>now||now-fresh.ObservedUtc.Value>TimeSpan.FromMinutes(2))return new("FreshObservationRequired");
  CollectionDueAgeReport before,after;
  try{before=CollectionDueAging.Calculate(previous,previous.DueDates,day);after=CollectionDueAging.Calculate(fresh,fresh.DueDates,day);}
  catch(ArgumentException){return new("InvalidCollectionEvidence");}
  if(previous.Invoices!.Count!=3||fresh.Invoices!.Count!=3)return new("DedicatedFixtureRequired");
  var old=previous.Invoices.SingleOrDefault(x=>x.Key==invoiceKey);
  var current=fresh.Invoices.SingleOrDefault(x=>x.Key==invoiceKey);
  var oldDue=before.Rows.SingleOrDefault(x=>x.InvoiceKey==invoiceKey);
  var due=after.Rows.SingleOrDefault(x=>x.InvoiceKey==invoiceKey);
  if(old==null||current==null||old!=current||oldDue==null||due==null||oldDue!=due)return new("InvoiceChangedRefreshRequired");
  if(current.CustomerKey is not Guid customer||customer==Guid.Empty||!new[]{"CRC-DUE-IMMEDIATE","CRC-DUE-NET30","CRC-DUE-FIXED"}.Contains(current.Reference,StringComparer.Ordinal)||current.Amount!=100m||current.Outstanding!=100m||current.Issued!=new DateOnly(2026,11,1))return new("DedicatedFixtureRequired");
  if(due.DueDate==null||due.DaysOverdue is not int days||days<=0)return new("InvoiceNotOverdue");
  string Date(DateOnly value)=>value.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
  var subject="SYNTHETIC TEST — payment reminder review — "+current.Reference;
  var body="INTERNAL TEST ONLY — DO NOT SEND\n\n"+
   "This is a test of the payment reminder workflow, not a request for payment.\n\n"+
   "Invoice reference: "+current.Reference+"\n"+
   "Issue date: "+Date(current.Issued)+"\n"+
   "Due date: "+Date(due.DueDate.Value)+"\n"+
   "Observed outstanding balance: USD "+current.Outstanding.ToString("0.00",CultureInfo.InvariantCulture)+"\n"+
   "Days overdue on "+Date(day)+": "+days.ToString(CultureInfo.InvariantCulture)+"\n"+
   "Balance observed UTC: "+fresh.ObservedUtc.Value.ToString("O",CultureInfo.InvariantCulture)+"\n\n"+
   "No recipient or payment instructions assigned. No message was sent.";
  return new("InternalReviewPrepared",subject,body,fresh.ObservedUtc);
 }
}
