using PracticeManager.Core;
internal static class TestLedgerReconciliationTests
{
 public static void Run(Action<bool,string> check)
 {
  var a=new TestLedgerAccounts(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid());
  var customer=Guid.NewGuid();
  var invoice=new TestLedgerInvoice(Guid.NewGuid(),customer,a.ServiceIncome,100m,60m);
  var receipt=new TestLedgerReceipt(Guid.NewGuid(),customer,invoice.Key,a.Cash,40m);
  var payment=new TestLedgerPayment(Guid.NewGuid(),a.Cash,a.OfficeExpense,10m);
  var ledger=new TestLedgerBalances(60m,30m,100m,10m,90m,0m,90m,90m);
  TestLedgerReconciliationResult Eval(TestLedgerInvoice? i=null,TestLedgerReceipt? r=null,TestLedgerPayment? p=null,TestLedgerBalances? l=null,TestLedgerAccounts? roles=null)
   =>TestLedgerReconciliation.Evaluate(roles??a,new[]{i??invoice},new[]{r??receipt},new[]{p??payment},l??ledger);
  check(Eval().Matches&&Eval().Calculated==ledger,"normalized synthetic ledger agrees with independent summary");
  check(Eval(r:receipt with{Customer=Guid.NewGuid()}).Outcome=="Blocked","same receipt total wrong customer blocked");
  check(Eval(r:receipt with{Invoice=Guid.NewGuid()}).Outcome=="Blocked","same receipt total wrong invoice blocked");
  check(Eval(r:receipt with{CashAccount=Guid.NewGuid()}).Outcome=="Blocked","wrong receiving account blocked");
  check(Eval(i:invoice with{IncomeAccount=a.OfficeExpense}).Outcome=="Blocked","wrong invoice income account blocked");
  check(Eval(p:payment with{ExpenseAccount=a.ServiceIncome}).Outcome=="Blocked","wrong expense account blocked");
  check(Eval(p:payment with{CashAccount=Guid.NewGuid()}).Outcome=="Blocked","wrong payment cash account blocked");
  check(Eval(r:receipt with{Amount=101m}).Differences.Contains("OverAllocation"),"invoice over-allocation blocked");
  check(Eval(i:invoice with{ReportedBalance=59m}).Differences.Contains("InvoiceBalance"),"individual invoice balance discrepancy detected");
  check(Eval(l:ledger with{Cash=31m}).Differences.Contains("Cash"),"independent cash discrepancy detected");
  check(Eval(l:ledger with{Equity=89m}).Differences.Contains("BalanceSheetEquation"),"independent balance sheet equation checked");
  check(Eval(l:ledger with{Profit=89m}).Differences.Contains("ProfitEquation"),"independent profit equation checked");
  check(Eval(roles:a with{Cash=a.Receivables}).Outcome=="Blocked","conflicting account identities blocked");
  check(Eval(r:receipt with{Amount=40.001m}).Outcome=="Blocked","unsupported precision rejected");
  check(Eval(p:payment with{Amount=-10m}).Outcome=="Blocked","negative payment rejected");
  check(TestLedgerReconciliation.Evaluate(a,new[]{invoice},new[]{receipt,receipt},new[]{payment},ledger).Outcome=="Blocked","duplicate receipt identifier blocked");
  check(Eval(p:payment with{Key=receipt.Key}).Outcome=="Blocked","duplicate document identity blocked");
  var second=invoice with{Key=Guid.NewGuid(),Amount=50m,ReportedBalance=50m};
  var wrong1=invoice with{ReportedBalance=100m};
  var wrong2=second with{ReportedBalance=10m};
  var summary=ledger with{Receivables=110m,Income=150m,Assets=140m,Equity=140m,Profit=140m};
  check(TestLedgerReconciliation.Evaluate(a,new[]{wrong1,wrong2},new[]{receipt},new[]{payment},summary).Differences.Contains("InvoiceBalance"),"offsetting invoice discrepancies detected despite equal totals");
  check(TestLedgerReconciliation.Evaluate(a,new[]{invoice},new[]{receipt with{Amount=15m},receipt with{Key=Guid.NewGuid(),Amount=25m}},new[]{payment},ledger).Matches,"multiple explicit allocations reconcile");
  check(!Eval().ToString().Contains(customer.ToString()),"reconciliation status does not expose identifiers");
 }
}
