using PracticeManager.Core;
internal static class SyntheticFinancialCorrectionTests
{
 public static void Run(Action<bool,string> check)
 {
  var customer=Guid.NewGuid();var project=Guid.NewGuid();var day=new DateOnly(2026,11,1);
  SyntheticCorrectionEvent Event(SyntheticCorrectionKind kind,decimal amount,Guid? target=null,Guid? deposit=null)
   =>new(Guid.NewGuid(),customer,project,day,kind,amount,target,deposit);
  var invoice=Event(SyntheticCorrectionKind.Invoice,800m);var deposit=Event(SyntheticCorrectionKind.Deposit,1000m);
  var credit=Event(SyntheticCorrectionKind.Credit,100m,invoice.Key);
  var application=Event(SyntheticCorrectionKind.Application,500m,invoice.Key,deposit.Key);
  var receipt=Event(SyntheticCorrectionKind.Receipt,200m,invoice.Key);
  var refund=Event(SyntheticCorrectionKind.Refund,100m,deposit:deposit.Key);
  var original=new[]{invoice,deposit,credit,application,receipt,refund};
  SyntheticCorrectionReview Review(IEnumerable<SyntheticCorrectionEvent> events,decimal ar=0m,decimal advance=400m,decimal cash=1100m,decimal income=700m)
   =>SyntheticFinancialCorrections.Evaluate(events.ToArray(),new Dictionary<Guid,decimal>{{invoice.Key,ar}},
    new Dictionary<Guid,decimal>{{deposit.Key,advance}},new(ar,cash,income,0m,ar+cash,advance,income,income));
  var result=Review(original);
  check(result.Outcome=="MatchedForReview"&&!result.MayPost&&!result.MayReverse&&!result.MayMarkPaid,"credit and allocations reconcile without changing original transactions");
  var undoReceipt=Event(SyntheticCorrectionKind.Reversal,200m,receipt.Key);
  var undoCredit=Event(SyntheticCorrectionKind.Reversal,100m,credit.Key);
  var undoApplication=Event(SyntheticCorrectionKind.Reversal,500m,application.Key);
  var undoRefund=Event(SyntheticCorrectionKind.Reversal,100m,refund.Key);
  check(Review(original.Append(undoReceipt),200m,400m,900m,700m).Outcome=="MatchedForReview","receipt reversal restores receivable and reduces cash");
  check(Review(original.Append(undoCredit),100m,400m,1100m,800m).Outcome=="MatchedForReview","credit reversal restores receivable and income");
  check(Review(original.Append(undoApplication),500m,900m,1100m,700m).Outcome=="MatchedForReview","application reversal restores liability and receivable");
  check(Review(original.Append(undoRefund),0m,500m,1200m,700m).Outcome=="MatchedForReview","refund reversal restores cash and liability");
  check(Review(original.Concat(new[]{undoReceipt,undoCredit,undoApplication,undoRefund}),800m,1000m,1000m,800m).Outcome=="MatchedForReview","all corrections preserve balanced original invoice and deposit");
  check(Review(original.Concat(new[]{undoReceipt,undoReceipt with{Key=Guid.NewGuid()}})).Outcome=="InvalidReversal","second reversal of same event blocked");
  check(Review(original.Append(undoReceipt with{Amount=199m})).Outcome=="InvalidReversal","partial reversal not silently treated as full");
  check(Review(original.Append(undoReceipt with{Project=Guid.NewGuid()})).Outcome=="InvalidReversal","cross-project reversal blocked");
  check(Review(original.Append(undoReceipt with{Customer=Guid.NewGuid()})).Outcome=="InvalidReversal","cross-customer reversal blocked");
  check(Review(original.Append(undoReceipt with{Target=Guid.NewGuid()})).Outcome=="InvalidReversal","unknown original reversal blocked");
  check(Review(original.Append(Event(SyntheticCorrectionKind.Reversal,800m,invoice.Key))).Outcome=="UnsupportedReversalKind","invoice void requires separate contract");
  check(Review(original.Concat(new[]{undoReceipt,Event(SyntheticCorrectionKind.Reversal,200m,undoReceipt.Key)})).Outcome=="UnsupportedReversalKind","reversal-of-reversal denied");
  check(Review(original.Append(undoReceipt with{Date=day.AddDays(-1)})).Outcome=="InvalidEvent","backdated correction sequence rejected");
  check(Review(original.Append(undoReceipt with{Key=receipt.Key})).Outcome=="InvalidEvent","duplicate event identity blocked");
  check(Review(original.Append(Event(SyntheticCorrectionKind.Credit,1m,invoice.Key))).Outcome=="InvoiceOverAllocation","credit against fully settled invoice requires separate refund contract");
  check(Review(original.Append(Event(SyntheticCorrectionKind.Refund,401m,deposit:deposit.Key))).Outcome=="AdvanceOverAllocation","overrefund denied");
  check(Review(original,ar:1m).Outcome=="InvoiceBalanceMismatch","individual invoice reconciliation required");
  check(Review(original,advance:401m).Outcome=="AdvanceBalanceMismatch","individual deposit reconciliation required");
  check(Review(original,income:800m).Outcome=="SummaryMismatch","credits reduce recognized revenue exactly once");
  check(original.Length==6&&original[4]==receipt&&original[5]==refund,"reference review retains original facts");
 }
}
