using Microsoft.Data.Sqlite;
using PracticeManager.Core;
namespace PracticeManager.Persistence;
public sealed partial class LocalPractice
{
 public bool CanCompareInvoiceCandidate()=>CanSyncDirectory()&&CanReviewCollections()&&Can("Billing.Invoices.View")&&Can("Projects.View");
 private Guid? ComparisonCustomer(SqliteConnection c,SqliteTransaction t,DirectoryContext context,long client)
 {
  if(DirectoryBusiness(c,t,context)!="CRC ACCOUNTING TEST"||ScopeBusiness(c,t,context.Actor,context.Profile.Revision)!="CRC ACCOUNTING TEST")return null;
  var value=Scalar(c,t,@"SELECT l.customer_key FROM accounting_customer_links l JOIN accounting_customer_directory d ON d.user_id=l.user_id AND d.revision=l.revision AND d.customer_key=l.customer_key JOIN clients z ON z.id=l.client_id WHERE l.user_id=$a AND l.revision=$r AND l.client_id=$i AND d.present=1 AND z.status='Active'",("$a",context.Actor),("$r",context.Profile.Revision),("$i",client));
  return value is string k&&Guid.TryParseExact(k,"D",out var id)&&id!=Guid.Empty?id:null;
 }
 public async Task<InvoiceComparison> CompareSavedInvoiceCandidateAsync(long project,long draftId,string expectedHash,string exactReference,bool singleUsdConfirmed,CancellationToken ct=default)
 {
  DirectoryContext context;InvoiceDraftSummary draft;Guid? customer;
  using(var c=Open())using(var t=c.BeginTransaction()){
   var actor=RequireInvoices(c,t,"Billing.Invoices.View");RequireCollections(c,t);RequireDirectory(c,t);
   var p=ProjectRow(c,t,project);draft=InvoiceDetail(c,t,project,draftId).Summary;
   if(draft.State!="Approved"||draft.PayloadHash!=expectedHash)throw new ArgumentException("Exact approved draft required.");
   if(!singleUsdConfirmed)return new("OperatorScopeEvidenceRequired");
   if(string.IsNullOrWhiteSpace(exactReference)||exactReference!=exactReference.Trim()||exactReference.Length>180||exactReference.Any(char.IsControl))throw new ArgumentException("Enter the exact external invoice reference.");
   var profile=ReadProfile(c,t,actor);var marker=ReadMarker(c,t);
   if(profile==null||marker==null)return new("FixtureScopeRequired");
   context=new(actor,sessionGeneration,profile,marker.Value);
   customer=ComparisonCustomer(c,t,context,p.Client);
   if(customer==null)return new("CurrentProvisionalCustomerLinkRequired");
  }
  var observed=await ReadSavedCollectionsAsync(true,ct);
  using var read=Open();using var tx=read.BeginTransaction();
  RequireInvoices(read,tx,"Billing.Invoices.View");RequireCollections(read,tx);
  if(!CurrentDirectoryContext(read,tx,context))return new("ProfileOrMarkerChanged");
  if(ct.IsCancellationRequested)return new("Cancelled");
  var current=ProjectRow(read,tx,project);var currentDraft=InvoiceDetail(read,tx,project,draftId).Summary;
  if(currentDraft!=draft||ComparisonCustomer(read,tx,context,current.Client)!=customer)return new("DraftOrCustomerLinkChanged");
  return InvoiceCandidateComparison.Compare(draft,exactReference,customer.Value,observed);
 }
}
