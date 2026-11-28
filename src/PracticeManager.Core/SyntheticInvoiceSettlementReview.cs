namespace PracticeManager.Core;

// Offline comparison of independently supplied synthetic evidence. No inference
// of actual issuance, payment, or authority from a draft or a register balance.
public sealed record SyntheticInvoiceSettlementResult(string Outcome,decimal? ObservedTotal=null,
 decimal? AllocatedReceipts=null,decimal? Outstanding=null,DateOnly? ObservedDueDate=null)
{
 public bool MayMarkIssued=>false;
 public bool MayMarkPaid=>false;
 public bool MayPost=>false;
 public bool MaySendReminder=>false;
 public override string ToString()=>"[Synthetic invoice settlement review: "+Outcome+"]";
}
public static class SyntheticInvoiceSettlementReview
{
 public static SyntheticInvoiceSettlementResult Evaluate(InvoiceDraftSummary draft,string reference,Guid customer,
  Guid expectedMarker,CollectionPreview observed,SyntheticSettlementSnapshot ledger,DateTimeOffset ledgerObservedUtc)
 {
  static SyntheticInvoiceSettlementResult Stop(string reason)=>new(reason);
  if(draft.ReconciliationHold)return Stop("DraftReconciliationHold");
  if(expectedMarker==Guid.Empty||ledger.MarkerKey!=expectedMarker||observed.DueFixtureMarker!=expectedMarker)
   return Stop("ScopeMismatch");
  if(!ledger.Complete||!ledger.IndependentEvidenceVerified||!ledger.SingleUsdCurrency||
     observed.ObservedUtc==null||observed.ObservedUtc!=ledgerObservedUtc)
   return Stop("SameAcquisitionEvidenceRequired");
  if(observed.Invoices==null||observed.Invoices.Any(x=>x==null)||ledger.Accounts==null||
     ledger.Invoices==null||ledger.Receipts==null||ledger.Payments==null||ledger.Balances==null||
     ledger.Invoices.Any(x=>x==null)||ledger.Receipts.Any(x=>x==null)||ledger.Payments.Any(x=>x==null))
   return Stop("InvalidEvidence");
  var candidate=InvoiceCandidateComparison.Compare(draft,reference,customer,observed);
  if(candidate.Outcome!="CandidateForManualReview"||candidate.InvoiceKey is not Guid key)
   return Stop(candidate.Outcome);
  if(observed.DueDates==null)return Stop("DueDateEvidenceRequired");
  try{_=CollectionDueAging.Calculate(observed,observed.DueDates,draft.InvoiceDate);}
  catch(ArgumentException){return Stop("InvalidDueDateEvidence");}
  var due=observed.DueDates.SingleOrDefault(x=>x.InvoiceKey==key);
  if(due==null)return Stop("DueDateEvidenceRequired");
  if(due.DueDate!=draft.DueDate)return Stop("DueDateMismatch");
  if(!TestLedgerReconciliation.Evaluate(ledger.Accounts,ledger.Invoices,ledger.Receipts,ledger.Payments,ledger.Balances).Matches)
   return Stop("LedgerNotReconciled");
  // Both bounded registers must cover exactly the same invoices, not just
  // equal totals. Missing or additional invoices prevent settlement review.
  if(observed.Invoices.Count!=ledger.Invoices.Count)return Stop("RegisterScopeMismatch");
  var byKey=ledger.Invoices.ToDictionary(x=>x.Key);
  foreach(var row in observed.Invoices)
   if(!byKey.TryGetValue(row.Key,out var entry)||row.CustomerKey!=entry.Customer||
      row.Amount!=entry.Amount||row.Outstanding!=entry.ReportedBalance)
    return Stop("RegisterLedgerMismatch");
  var invoice=byKey[key];
  var allocated=ledger.Receipts.Where(x=>x.Invoice==key).Sum(x=>x.Amount);
  if(invoice.Amount-allocated!=invoice.ReportedBalance)return Stop("SettlementMismatch");
  var outcome=allocated==0?"NoAllocatedReceiptsForReview":invoice.ReportedBalance==0?
   "FullAllocationForReview":"PartialAllocationForReview";
  return new(outcome,invoice.Amount,allocated,invoice.ReportedBalance,due.DueDate);
 }
}
