namespace PracticeManager.Core;

// Normalized provider-neutral model for the zero-opening-balance, single-currency,
// no-tax, no-credit, no-discount synthetic ledger only. No API or posting methods.
public sealed record TestLedgerAccounts(Guid Receivables,Guid Cash,Guid ServiceIncome,Guid OfficeExpense);
public sealed record TestLedgerInvoice(Guid Key,Guid Customer,Guid IncomeAccount,decimal Amount,decimal ReportedBalance);
public sealed record TestLedgerReceipt(Guid Key,Guid Customer,Guid Invoice,Guid CashAccount,decimal Amount);
public sealed record TestLedgerPayment(Guid Key,Guid CashAccount,Guid ExpenseAccount,decimal Amount);
public sealed record TestLedgerBalances(decimal Receivables,decimal Cash,decimal Income,decimal Expenses,decimal Assets,decimal Liabilities,decimal Equity,decimal Profit)
{
 public override string ToString()=>"[Synthetic ledger balances]";
}
public sealed record TestLedgerReconciliationResult(string Outcome,IReadOnlyList<string> Differences,TestLedgerBalances? Calculated=null)
{
 public bool Matches=>Outcome=="Matched";
 public override string ToString()=>$"Synthetic reconciliation: {Outcome}";
}
public static class TestLedgerReconciliation
{
 private const decimal Limit=999999999999.99m;
 static bool Amount(decimal n)=>n>=0&&n<=Limit&&decimal.Round(n,2)==n;
 static TestLedgerReconciliationResult Block(string reason)=>new("Blocked",Array.AsReadOnly(new[]{reason}));
 public static TestLedgerReconciliationResult Evaluate(
  TestLedgerAccounts accounts,IReadOnlyList<TestLedgerInvoice> invoices,
  IReadOnlyList<TestLedgerReceipt> receipts,IReadOnlyList<TestLedgerPayment> payments,
  TestLedgerBalances independentBalances)
 {
  // Completeness and common currency must be established by a future adapter.
  // This method is only a domain test, not an assertion of completeness of any live ledger.
  if(invoices.Count>200||receipts.Count>200||payments.Count>200)return Block("FixtureLimit");
  var accountIds=new[]{accounts.Receivables,accounts.Cash,accounts.ServiceIncome,accounts.OfficeExpense};
  if(accountIds.Contains(Guid.Empty)||accountIds.Distinct().Count()!=4)return Block("AccountIdentity");
  var ids=new HashSet<Guid>();
  var byInvoice=new Dictionary<Guid,TestLedgerInvoice>();
  var applied=new Dictionary<Guid,decimal>();
  foreach(var i in invoices){
   if(i.Key==Guid.Empty||!ids.Add(i.Key)||i.Customer==Guid.Empty||i.IncomeAccount!=accounts.ServiceIncome||
      !Amount(i.Amount)||i.Amount==0||!Amount(i.ReportedBalance)||i.ReportedBalance>i.Amount)return Block("InvalidInvoice");
   byInvoice.Add(i.Key,i);applied.Add(i.Key,0m);
  }
  foreach(var r in receipts){
   if(r.Key==Guid.Empty||!ids.Add(r.Key)||r.CashAccount!=accounts.Cash||!Amount(r.Amount)||r.Amount==0||
      !byInvoice.TryGetValue(r.Invoice,out var i)||i.Customer!=r.Customer)return Block("InvalidReceiptAllocation");
   var next=applied[r.Invoice]+r.Amount;
   if(next>i.Amount)return Block("OverAllocation");
   applied[r.Invoice]=next;
  }
  foreach(var p in payments)
   if(p.Key==Guid.Empty||!ids.Add(p.Key)||p.CashAccount!=accounts.Cash||p.ExpenseAccount!=accounts.OfficeExpense||
      !Amount(p.Amount)||p.Amount==0)return Block("InvalidPayment");
  if(new[]{independentBalances.Receivables,independentBalances.Cash,independentBalances.Income,
     independentBalances.Expenses,independentBalances.Assets,independentBalances.Liabilities,
     independentBalances.Equity,independentBalances.Profit}.Any(n=>!Amount(n)))return Block("UnsupportedSummaryAmount");
  var differences=new List<string>();
  foreach(var i in invoices)
   if(i.Amount-applied[i.Key]!=i.ReportedBalance){differences.Add("InvoiceBalance");break;}
  var income=invoices.Sum(i=>i.Amount);var expense=payments.Sum(p=>p.Amount);
  var cash=receipts.Sum(r=>r.Amount)-expense;
  var ar=invoices.Sum(i=>i.Amount-applied[i.Key]);
  var profit=income-expense;
  if(cash<0||profit<0)return Block("UnsupportedNegativeBalance");
  var calculated=new TestLedgerBalances(ar,cash,income,expense,ar+cash,0m,profit,profit);
  void Compare(string name,decimal calculated,decimal observed){if(calculated!=observed)differences.Add(name);}
  Compare("Receivables",calculated.Receivables,independentBalances.Receivables);
  Compare("Cash",calculated.Cash,independentBalances.Cash);
  Compare("Income",calculated.Income,independentBalances.Income);
  Compare("Expenses",calculated.Expenses,independentBalances.Expenses);
  Compare("Assets",calculated.Assets,independentBalances.Assets);
  Compare("Liabilities",calculated.Liabilities,independentBalances.Liabilities);
  Compare("Equity",calculated.Equity,independentBalances.Equity);
  Compare("Profit",calculated.Profit,independentBalances.Profit);
  if(independentBalances.Assets!=independentBalances.Liabilities+independentBalances.Equity)differences.Add("BalanceSheetEquation");
  if(independentBalances.Income-independentBalances.Expenses!=independentBalances.Profit)differences.Add("ProfitEquation");
  if(independentBalances.Cash+independentBalances.Receivables!=independentBalances.Assets)differences.Add("AssetComposition");
  return new(differences.Count==0?"Matched":"Mismatch",differences.AsReadOnly(),calculated);
 }
}
