using PracticeManager.Core;
internal static class SyntheticReceiptCapacityTests
{
 public static void Run(Action<bool,string> check)
 {
  var a=new TestLedgerAccounts(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid());
  var marker=Guid.NewGuid();var customer=Guid.NewGuid();
  var invoice=new TestLedgerInvoice(Guid.NewGuid(),customer,a.ServiceIncome,125m,75m);
  var receipt=new TestLedgerReceipt(Guid.NewGuid(),customer,invoice.Key,a.Cash,50m);
  var payment=new TestLedgerPayment(Guid.NewGuid(),a.Cash,a.OfficeExpense,20m);
  var ledger=new TestLedgerBalances(75m,30m,125m,20m,105m,0m,105m,105m);
  var snapshot=new SyntheticSettlementSnapshot(marker,true,true,true,a,new[]{invoice},new[]{receipt},new[]{payment},ledger);
  var intent=new SyntheticWireIntent(Guid.NewGuid(),marker,RehearsalKind.Receipt,a.Receivables,40m,new DateOnly(2026,11,25),"USD",customer,a.Cash,invoice.Key);
  SyntheticReceiptCapacityResult Review(SyntheticWireIntent? i=null,SyntheticSettlementSnapshot? s=null,string? prior=null)
   =>SyntheticReceiptCapacityReview.Evaluate(i??intent,s??snapshot,s??snapshot,prior);
  var accepted=Review();
  check(accepted.Outcome=="CapacityForReview"&&accepted.RemainingBefore==75m&&accepted.RemainingAfter==35m&&
   accepted.ReviewFingerprint?.Length==64&&!accepted.MaySend&&!accepted.MayReserveFunds&&!accepted.MayAutomaticallyRetry,
   "synthetic receipt exact capacity review grants no financial authority");
  check(Review(prior:accepted.ReviewFingerprint)==accepted,"same receipt review deterministic");
  check(Review(i:intent with{Amount=75m}).RemainingAfter==0m,"exact full receipt capacity");
  check(Review(i:intent with{Amount=75.01m}).Outcome=="InsufficientInvoiceBalance","one-cent excess receipt denied");
  foreach(var invalid in new[]{0m,-1m,1.001m})
   check(Review(i:intent with{Amount=invalid}).Outcome=="InvalidReceipt","invalid receipt amount denied");
  check(Review(i:intent with{AccountKey=Guid.NewGuid()}).Outcome=="AccountMismatch","wrong receivable account denied");
  check(Review(i:intent with{CashAccountKey=Guid.NewGuid()}).Outcome=="AccountMismatch","wrong receiving account denied");
  check(Review(i:intent with{CustomerKey=Guid.NewGuid()}).Outcome=="InvoiceOrCustomerMismatch","wrong customer denied");
  check(Review(i:intent with{AllocatedInvoiceKey=Guid.NewGuid()}).Outcome=="InvoiceOrCustomerMismatch","wrong invoice denied");
  check(Review(s:snapshot with{MarkerKey=Guid.NewGuid()}).Outcome=="MarkerMismatch","wrong accounting business denied");
  foreach(var invalid in new[]{snapshot with{Complete=false},snapshot with{IndependentEvidenceVerified=false},snapshot with{SingleUsdCurrency=false}})
   check(Review(s:invalid).Outcome=="IncompleteEvidence","incomplete or non-USD evidence denied");
  check(Review(s:snapshot with{Balances=ledger with{Cash=31m}}).Outcome=="LedgerNotReconciled","unreconciled summary blocks receipt");
  check(Review(s:snapshot with{Invoices=new[]{invoice with{ReportedBalance=74m}}}).Outcome=="LedgerNotReconciled","wrong invoice balance blocks receipt");
  check(Review(s:snapshot with{Receipts=new[]{receipt,receipt}}).Outcome=="LedgerNotReconciled","duplicate receipt evidence blocks review");
  check(Review(i:intent with{OperationKey=Guid.NewGuid()},prior:accepted.ReviewFingerprint).Outcome=="StaleReview","receipt review cannot transfer operation identity");
  check(Review(i:intent with{Amount=39m},prior:accepted.ReviewFingerprint).Outcome=="StaleReview","receipt review cannot transfer changed amount");
  // Another receipt settles 50 while the original proposed 40 is still under review.
  var newer=snapshot with{Invoices=new[]{invoice with{ReportedBalance=25m}},
   Receipts=new[]{receipt,new TestLedgerReceipt(Guid.NewGuid(),customer,invoice.Key,a.Cash,50m)},
   Balances=ledger with{Receivables=25m,Cash=80m}};
  check(SyntheticReceiptCapacityReview.Evaluate(intent,snapshot,newer).Outcome=="EvidenceChanged","between-sweep receipt change detected");
  check(Review(s:newer,prior:accepted.ReviewFingerprint).Outcome=="InsufficientInvoiceBalance","later receipt consumes capacity; old review not a reservation");
  var small=intent with{Amount=20m};var smallReview=Review(i:small);
  check(Review(i:small,s:newer,prior:smallReview.ReviewFingerprint).Outcome=="StaleReview","still-affordable changed ledger requires fresh review");
  check(Review(i:small,s:newer).RemainingAfter==5m,"fresh smaller review reflects authoritative synthetic balance");
  var reversed=newer with{Receipts=newer.Receipts.Reverse().ToArray()};
  check(SyntheticReceiptCapacityReview.Evaluate(small,newer,reversed)==Review(i:small,s:newer),"independent sweep ordering does not change evidence");
  check(!accepted.ToString().Contains(invoice.Key.ToString()),"receipt review display redacts identity");
 }
}
