using Microsoft.Data.Sqlite;
using System.Globalization;
using PracticeManager.Core;
namespace PracticeManager.Persistence;
public sealed record InvoiceCandidateObservation(long Id,long DraftId,string PayloadHash,string BusinessName,Guid MarkerKey,string ProfileRevision,Guid CustomerKey,Guid InvoiceKey,string Reference,DateOnly IssueDate,decimal ObservedTotal,decimal ObservedOutstanding,DateTimeOffset ObservedUtc,long ActorId);
public sealed partial class LocalPractice
{
 public IReadOnlyList<InvoiceCandidateObservation> ReadInvoiceCandidateObservations(long project,long draft)
 {
  using var c=Open();using var t=c.BeginTransaction();RequireInvoices(c,t,"Billing.Invoices.View");RequireCollections(c,t);RequireDirectory(c,t);ProjectRow(c,t,project);InvoiceDetail(c,t,project,draft);
  using var q=Command(c,t,"SELECT id,draft_id,payload_hash,business_name,marker_key,profile_revision,customer_key,invoice_key,reference,issue_date,total_cents,outstanding_cents,observed_utc,actor_id FROM invoice_candidate_observations WHERE draft_id=$i ORDER BY id DESC LIMIT 501",("$i",draft));using var r=q.ExecuteReader();var rows=new List<InvoiceCandidateObservation>();
  while(r.Read()){if(rows.Count==500)throw new ArgumentException("Invoice candidate history exceeds development limit.");rows.Add(new(r.GetInt64(0),r.GetInt64(1),r.GetString(2),r.GetString(3),Guid.ParseExact(r.GetString(4),"D"),r.GetString(5),Guid.ParseExact(r.GetString(6),"D"),Guid.ParseExact(r.GetString(7),"D"),r.GetString(8),ReadPlanDate(r.GetString(9)),r.GetInt64(10)/100m,r.GetInt64(11)/100m,DateTimeOffset.ParseExact(r.GetString(12),"O",CultureInfo.InvariantCulture),r.GetInt64(13)));}return rows.AsReadOnly();
 }
 public Task<InvoiceComparison> CaptureSavedInvoiceCandidateAsync(long project,long draftId,string expectedHash,string exactReference,bool singleUsdConfirmed,bool captureConfirmed,CancellationToken ct=default)
 =>CompareSavedInvoiceCandidateCoreAsync(project,draftId,expectedHash,exactReference,singleUsdConfirmed,true,captureConfirmed,ct);
 public bool CanCompareInvoiceCandidate()=>CanSyncDirectory()&&CanReviewCollections()&&Can("Billing.Invoices.View")&&Can("Projects.View");
 private Guid? ComparisonCustomer(SqliteConnection c,SqliteTransaction t,DirectoryContext context,long client)
 {
  if(DirectoryBusiness(c,t,context)!="CRC ACCOUNTING TEST"||ScopeBusiness(c,t,context.Actor,context.Profile.Revision)!="CRC ACCOUNTING TEST")return null;
  var value=Scalar(c,t,@"SELECT l.customer_key FROM accounting_customer_links l JOIN accounting_customer_directory d ON d.user_id=l.user_id AND d.revision=l.revision AND d.customer_key=l.customer_key JOIN clients z ON z.id=l.client_id WHERE l.user_id=$a AND l.revision=$r AND l.client_id=$i AND d.present=1 AND z.status='Active'",("$a",context.Actor),("$r",context.Profile.Revision),("$i",client));
  return value is string k&&Guid.TryParseExact(k,"D",out var id)&&id!=Guid.Empty?id:null;
 }
 public Task<InvoiceComparison> CompareSavedInvoiceCandidateAsync(long project,long draftId,string expectedHash,string exactReference,bool singleUsdConfirmed,CancellationToken ct=default)
 =>CompareSavedInvoiceCandidateCoreAsync(project,draftId,expectedHash,exactReference,singleUsdConfirmed,false,false,ct);
 private async Task<InvoiceComparison> CompareSavedInvoiceCandidateCoreAsync(long project,long draftId,string expectedHash,string exactReference,bool singleUsdConfirmed,bool capture,bool captureConfirmed,CancellationToken ct)
 {
  DirectoryContext context;InvoiceDraftSummary draft;Guid? customer;
  using(var c=Open())using(var t=c.BeginTransaction()){
   var actor=RequireInvoices(c,t,capture?"Billing.Invoices.Edit":"Billing.Invoices.View");RequireCollections(c,t);RequireDirectory(c,t);
   var p=ProjectRow(c,t,project);draft=InvoiceDetail(c,t,project,draftId).Summary;
   if(draft.State!="Approved"||draft.PayloadHash!=expectedHash)throw new ArgumentException("Exact approved draft required.");
   if(!singleUsdConfirmed)return new("OperatorScopeEvidenceRequired");
   if(capture&&!captureConfirmed)return new("OperatorCaptureConfirmationRequired");
   if(string.IsNullOrWhiteSpace(exactReference)||exactReference!=exactReference.Trim()||exactReference.Length>180||exactReference.Any(char.IsControl))throw new ArgumentException("Enter the exact external invoice reference.");
   var profile=ReadProfile(c,t,actor);var marker=ReadMarker(c,t);
   if(profile==null||marker==null)return new("FixtureScopeRequired");
   context=new(actor,sessionGeneration,profile,marker.Value);
   customer=ComparisonCustomer(c,t,context,p.Client);
   if(customer==null)return new("CurrentProvisionalCustomerLinkRequired");
  }
  var observed=await ReadSavedCollectionsAsync(true,ct);
  using var read=Open();using var tx=read.BeginTransaction(deferred:!capture);
  RequireInvoices(read,tx,capture?"Billing.Invoices.Edit":"Billing.Invoices.View");RequireCollections(read,tx);
  if(!CurrentDirectoryContext(read,tx,context))return new("ProfileOrMarkerChanged");
  if(ct.IsCancellationRequested)return new("Cancelled");
  var current=ProjectRow(read,tx,project);var currentDraft=InvoiceDetail(read,tx,project,draftId).Summary;
  if(currentDraft!=draft||ComparisonCustomer(read,tx,context,current.Client)!=customer)return new("DraftOrCustomerLinkChanged");
  var comparison=InvoiceCandidateComparison.Compare(draft,exactReference,customer.Value,observed);
  if(!capture||comparison.Outcome!="CandidateForManualReview")return comparison;
  if(comparison.InvoiceKey is not Guid invoice||invoice==Guid.Empty||comparison.ObservedUtc is not DateTimeOffset observedUtc||comparison.ObservedTotal is not decimal total||comparison.Outstanding is not decimal outstanding)throw new InvalidOperationException("Incomplete exact candidate observation.");
  if(ct.IsCancellationRequested)return new("Cancelled");
  Exec(read,tx,@"INSERT INTO invoice_candidate_observations(draft_id,payload_hash,business_name,marker_key,profile_revision,customer_key,invoice_key,reference,issue_date,total_cents,outstanding_cents,observed_utc,actor_id) VALUES($d,$h,'CRC ACCOUNTING TEST',$m,$r,$c,$i,$ref,$date,$total,$outstanding,$utc,$a)",("$d",draftId),("$h",expectedHash),("$m",context.Marker.ToString("D")),("$r",context.Profile.Revision),("$c",customer.Value.ToString("D")),("$i",invoice.ToString("D")),("$ref",exactReference),("$date",PlanDate(draft.InvoiceDate)),("$total",BillingCents(total,true)),("$outstanding",BillingCents(outstanding)),("$utc",observedUtc.ToString("O",CultureInfo.InvariantCulture)),("$a",context.Actor));
  var id=Last(read,tx);Audit(read,tx,context.Actor,"CaptureInvoiceCandidateObservation","InvoiceDraft",draftId);tx.Commit();return comparison with{ObservationId=id};
 }
}
