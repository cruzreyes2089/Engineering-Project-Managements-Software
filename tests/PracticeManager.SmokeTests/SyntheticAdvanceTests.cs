using PracticeManager.Core;
internal static class SyntheticAdvanceTests
{
 public static void Run(Action<bool,string> check)
 {
  var customer=Guid.NewGuid();var project=Guid.NewGuid();var day=new DateOnly(2026,11,1);
  var deposit=new SyntheticAdvanceDeposit(Guid.NewGuid(),customer,project,1000m,1000m,day);
  var invoice=new SyntheticAdvanceInvoice(Guid.NewGuid(),customer,project,800m,200m);
  var apply=new SyntheticAdvanceMovement(Guid.NewGuid(),deposit.Key,customer,project,600m,day.AddDays(1),invoice.Key);
  var refund=new SyntheticAdvanceMovement(Guid.NewGuid(),deposit.Key,customer,project,200m,day.AddDays(2),null);
  var retained=deposit with{Remaining=200m};
  var summary=new TestLedgerBalances(200m,800m,800m,0m,1000m,200m,800m,800m);
  var emptyInvoices=Array.Empty<SyntheticAdvanceInvoice>();var emptyReceipts=Array.Empty<SyntheticAdvanceReceipt>();var emptyMoves=Array.Empty<SyntheticAdvanceMovement>();
  var opening=SyntheticAdvanceReconciliation.Evaluate(emptyInvoices,new[]{deposit},emptyReceipts,emptyMoves,new(0m,1000m,0m,0m,1000m,1000m,0m,0m));
  check(opening.Outcome=="MatchedForReview"&&!opening.MayPost&&!opening.MayApply&&!opening.MayRefund,"unearned deposit is liability not revenue");
  SyntheticAdvanceResult Review(SyntheticAdvanceInvoice? i=null,SyntheticAdvanceDeposit? d=null,SyntheticAdvanceMovement[]? m=null,SyntheticAdvanceReceipt[]? r=null,TestLedgerBalances? b=null)
   =>SyntheticAdvanceReconciliation.Evaluate(new[]{i??invoice},new[]{d??retained},r??emptyReceipts,m??new[]{apply,refund},b??summary);
  check(Review().Outcome=="MatchedForReview"&&Review().Calculated==summary,"advance application and refund reconcile without duplicate cash or revenue");
  check(Review(m:new[]{apply with{Project=Guid.NewGuid()},refund}).Outcome=="InvalidAdvanceMovement","cross-project advance movement denied");
  check(Review(i:invoice with{Project=Guid.NewGuid()}).Outcome=="InvalidInvoiceApplication","cross-project invoice application denied");
  check(Review(i:invoice with{Customer=Guid.NewGuid()}).Outcome=="InvalidInvoiceApplication","cross-customer invoice application denied");
  check(Review(m:new[]{apply,refund with{Amount=401m}}).Outcome=="AdvanceOverAllocation","excess advance refund denied");
  check(Review(m:new[]{apply with{Amount=801m},refund}).Outcome=="InvoiceOverAllocation","excess invoice application denied");
  check(Review(m:new[]{apply,apply}).Outcome=="InvalidAdvanceMovement","duplicate movement key denied");
  check(Review(m:new[]{apply,refund with{Date=day.AddDays(-1)}}).Outcome=="InvalidAdvanceMovement","refund before deposit denied");
  check(Review(m:new[]{apply,refund with{Amount=0.001m}}).Outcome=="InvalidAdvanceMovement","fractional cent refund denied");
  check(Review(d:retained with{Remaining=201m}).Outcome=="AdvanceBalanceMismatch","individual advance balance must reconcile");
  check(Review(i:invoice with{Outstanding=201m}).Outcome=="InvoiceBalanceMismatch","individual invoice balance must reconcile");
  check(Review(b:summary with{Income=1800m}).Outcome=="SummaryMismatch","deposit cannot be counted again as earned income");
  check(Review(b:summary with{Cash=1400m}).Outcome=="SummaryMismatch","application cannot create cash");
  var receipt=new SyntheticAdvanceReceipt(Guid.NewGuid(),invoice.Key,customer,project,200m);
  var settled=Review(i:invoice with{Outstanding=0m},r:new[]{receipt},b:summary with{Receivables=0m,Cash=1000m});
  check(settled.Outcome=="MatchedForReview","direct receipt and advance application settle same invoice");
  check(Review(r:new[]{receipt with{Amount=201m}}).Outcome=="InvoiceOverAllocation","combined receipt and advance cannot overallocate invoice");
  check(!settled.ToString().Contains(customer.ToString()),"advance review redacts identity");
 }
}
