namespace PracticeManager.Core;

// Comparison only, not an issued-invoice link, posting acknowledgement, or AR ledger.
public sealed record InvoiceComparison(string Outcome,decimal? ObservedTotal=null,decimal? Outstanding=null,DateTimeOffset? ObservedUtc=null)
{
 public bool MayPost=>false;
 public bool MayMarkIssued=>false;
 public bool MaySendReminder=>false;
}
public static class InvoiceCandidateComparison
{
 public static InvoiceComparison Compare(InvoiceDraftSummary draft,string reference,Guid customer,CollectionPreview observation)
 {
  if(draft.State!="Approved"||customer==Guid.Empty||string.IsNullOrWhiteSpace(reference)||reference!=reference.Trim()||reference.Length>180||reference.Any(char.IsControl)||draft.Total<=0||decimal.Round(draft.Total,2)!=draft.Total)return new("ApprovedDraftAndExactReferenceRequired");
  if(observation.Outcome!="ObservedForReview"||observation.Invoices==null||observation.ObservedUtc==null||observation.Invoices.Count>20)return new("CompleteObservationRequired");
  var rows=observation.Invoices;
  if(rows.Any(x=>x.Key==Guid.Empty||x.Amount<0||x.Outstanding<0||x.Outstanding>x.Amount||decimal.Round(x.Amount,2)!=x.Amount||decimal.Round(x.Outstanding,2)!=x.Outstanding)||rows.Select(x=>x.Key).Distinct().Count()!=rows.Count)return new("InvalidObservation");
  var candidates=rows.Where(x=>string.Equals(x.Reference,reference,StringComparison.OrdinalIgnoreCase)).ToArray();
  if(candidates.Length==0)return new("ReferenceNotObserved");
  if(candidates.Length!=1)return new("DuplicateReference");
  var row=candidates[0];
  if(row.Reference!=reference)return new("ExactReferenceMismatch");
  if(row.CustomerKey==null)return new("CustomerIdentityUnavailable");
  if(row.CustomerKey!=customer)return new("CustomerMismatch");
  if(row.Issued!=draft.InvoiceDate)return new("IssueDateMismatch");
  if(row.Amount!=draft.Total)return new("InvoiceTotalMismatch");
  return new("CandidateForManualReview",row.Amount,row.Outstanding,observation.ObservedUtc);
 }
}
