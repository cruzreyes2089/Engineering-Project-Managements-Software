using PracticeManager.Core;
internal static class SyntheticInvoiceSettlementTests
{
 public static void Run(Action<bool,string> check)
 {
  var marker=Guid.NewGuid();var customer=Guid.NewGuid();var key=Guid.NewGuid();
  var a=new TestLedgerAccounts(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid());
  var day=new DateOnly(2026,11,1);var due=day.AddDays(30);
  var utc=new DateTimeOffset(2026,11,27,20,0,0,TimeSpan.Zero);
  var draft=new InvoiceDraftSummary(1,2,"DRAFT-1",day,due,125m,"Approved","synthetic");
  var invoice=new CollectionInvoice(key,"SYN-1",day,125m,75m,customer);
  var dates=new[]{new ObservedInvoiceDueDate(key,"SYN-1",customer,day,due,utc)};
  var observed=new CollectionPreview("ObservedForReview",new[]{invoice},utc,dates,marker);
  var ledger=new SyntheticSettlementSnapshot(marker,true,true,true,a,
   new[]{new TestLedgerInvoice(key,customer,a.ServiceIncome,125m,75m)},
   new[]{new TestLedgerReceipt(Guid.NewGuid(),customer,key,a.Cash,50m)},
   new[]{new TestLedgerPayment(Guid.NewGuid(),a.Cash,a.OfficeExpense,20m)},
   new TestLedgerBalances(75m,30m,125m,20m,105m,0m,105m,105m));
  SyntheticInvoiceSettlementResult Review(InvoiceDraftSummary? d=null,CollectionPreview? o=null,
   SyntheticSettlementSnapshot? l=null,DateTimeOffset? time=null)
   =>SyntheticInvoiceSettlementReview.Evaluate(d??draft,"SYN-1",customer,marker,o??observed,l??ledger,time??utc);
  var partial=Review();
  check(partial.Outcome=="PartialAllocationForReview"&&partial.AllocatedReceipts==50m&&partial.Outstanding==75m&&partial.ObservedDueDate==due,
   "partial synthetic settlement matches exact invoice and terms");
  check(!partial.MayMarkIssued&&!partial.MayMarkPaid&&!partial.MayPost&&!partial.MaySendReminder,"settlement review never authorizes state changes");
  check(Review(d:draft with{ReconciliationHold=true}).Outcome=="DraftReconciliationHold","held invoice excluded");
  check(Review(d:draft with{DueDate=due.AddDays(1)}).Outcome=="DueDateMismatch","approved due date discrepancy detected");
  check(Review(o:observed with{DueDates=null}).Outcome=="DueDateEvidenceRequired","missing terms not inferred");
  check(Review(o:observed with{DueDates=new[]{dates[0] with{CustomerKey=Guid.NewGuid()}}}).Outcome=="InvalidDueDateEvidence","wrong due-date customer rejected");
  check(Review(o:observed with{DueFixtureMarker=Guid.NewGuid()}).Outcome=="ScopeMismatch","wrong observation marker rejected");
  check(Review(time:utc.AddSeconds(1)).Outcome=="SameAcquisitionEvidenceRequired","different acquisition times rejected");
  check(Review(l:ledger with{Complete=false}).Outcome=="SameAcquisitionEvidenceRequired","incomplete settlement evidence rejected");
  check(Review(l:ledger with{IndependentEvidenceVerified=false}).Outcome=="SameAcquisitionEvidenceRequired","independent evidence required");
  check(Review(l:ledger with{SingleUsdCurrency=false}).Outcome=="SameAcquisitionEvidenceRequired","currency scope required");
  check(Review(l:ledger with{Receipts=Array.Empty<TestLedgerReceipt>()}).Outcome=="LedgerNotReconciled","balance reduction alone is not receipt evidence");
  check(Review(o:observed with{Invoices=new[]{invoice with{Outstanding=74m}}}).Outcome=="RegisterLedgerMismatch","register balance differs from ledger");
  check(Review(o:observed with{Invoices=new[]{invoice,invoice with{Key=Guid.NewGuid(),Reference="SYN-2"}}}).Outcome=="RegisterScopeMismatch","extra register invoice rejected");
  var full=ledger with{Invoices=new[]{ledger.Invoices[0] with{ReportedBalance=0m}},
   Receipts=ledger.Receipts.Append(new TestLedgerReceipt(Guid.NewGuid(),customer,key,a.Cash,75m)).ToArray(),
   Balances=ledger.Balances with{Receivables=0m,Cash=105m}};
  var settled=Review(o:observed with{Invoices=new[]{invoice with{Outstanding=0m}}},l:full);
  check(settled.Outcome=="FullAllocationForReview"&&settled.AllocatedReceipts==125m&&settled.Outstanding==0m&&!settled.MayMarkPaid,
   "full allocation remains review only");
  var unpaid=ledger with{Invoices=new[]{ledger.Invoices[0] with{ReportedBalance=125m}},
   Receipts=Array.Empty<TestLedgerReceipt>(),Payments=Array.Empty<TestLedgerPayment>(),
   Balances=new TestLedgerBalances(125m,0m,125m,0m,125m,0m,125m,125m)};
  check(Review(o:observed with{Invoices=new[]{invoice with{Outstanding=125m}}},l:unpaid).Outcome=="NoAllocatedReceiptsForReview",
   "unpaid synthetic invoice distinguished from partial allocation");
  check(!settled.ToString().Contains(key.ToString()),"settlement review redacts identity");
 }
}
