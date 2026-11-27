namespace PracticeManager.Core;

// Narrow accrual reference model, not a provider posting contract. Every advance
// is a refundable unearned customer liability, not a fee earned on receipt.
public sealed record SyntheticAdvanceInvoice(Guid Key,Guid Customer,Guid Project,decimal Amount,decimal Outstanding);
public sealed record SyntheticAdvanceDeposit(Guid Key,Guid Customer,Guid Project,decimal Amount,decimal Remaining,DateOnly Date);
public sealed record SyntheticAdvanceReceipt(Guid Key,Guid Invoice,Guid Customer,Guid Project,decimal Amount);
public sealed record SyntheticAdvanceMovement(Guid Key,Guid Deposit,Guid Customer,Guid Project,decimal Amount,DateOnly Date,Guid? Invoice);
public sealed record SyntheticAdvanceResult(string Outcome,TestLedgerBalances? Calculated=null)
{
 public bool MayPost=>false;
 public bool MayRefund=>false;
 public bool MayApply=>false;
 public override string ToString()=>"[Synthetic advance review: "+Outcome+"]";
}
public static class SyntheticAdvanceReconciliation
{
 const decimal Limit=999999999999.99m;
 static bool Money(decimal x)=>x>=0&&x<=Limit&&decimal.Round(x,2)==x;
 public static SyntheticAdvanceResult Evaluate(IReadOnlyList<SyntheticAdvanceInvoice> invoices,
  IReadOnlyList<SyntheticAdvanceDeposit> deposits,IReadOnlyList<SyntheticAdvanceReceipt> receipts,
  IReadOnlyList<SyntheticAdvanceMovement> movements,TestLedgerBalances observed)
 {
  static SyntheticAdvanceResult Stop(string reason)=>new(reason);
  if(invoices==null||deposits==null||receipts==null||movements==null||observed==null||
   invoices.Count>200||deposits.Count>200||receipts.Count>200||movements.Count>200)return Stop("InvalidScope");
  var ids=new HashSet<Guid>();
  bool Identity(Guid key,Guid customer,Guid project)=>key!=Guid.Empty&&customer!=Guid.Empty&&project!=Guid.Empty&&ids.Add(key);
  var byInvoice=new Dictionary<Guid,SyntheticAdvanceInvoice>();var outstanding=new Dictionary<Guid,decimal>();
  var byDeposit=new Dictionary<Guid,SyntheticAdvanceDeposit>();var remaining=new Dictionary<Guid,decimal>();
  foreach(var i in invoices)
  {
   if(i==null||!Identity(i.Key,i.Customer,i.Project)||!Money(i.Amount)||i.Amount==0||!Money(i.Outstanding)||i.Outstanding>i.Amount)return Stop("InvalidInvoice");
   byInvoice.Add(i.Key,i);outstanding.Add(i.Key,i.Amount);
  }
  foreach(var d in deposits)
  {
   if(d==null||!Identity(d.Key,d.Customer,d.Project)||!Money(d.Amount)||d.Amount==0||
      !Money(d.Remaining)||d.Remaining>d.Amount||d.Date.Year<1900)return Stop("InvalidDeposit");
   byDeposit.Add(d.Key,d);remaining.Add(d.Key,d.Amount);
  }
  foreach(var r in receipts)
  {
   if(r==null||!Identity(r.Key,r.Customer,r.Project)||!Money(r.Amount)||r.Amount==0||
      !byInvoice.TryGetValue(r.Invoice,out var i)||i.Customer!=r.Customer||i.Project!=r.Project)return Stop("InvalidReceipt");
   if(r.Amount>outstanding[r.Invoice])return Stop("InvoiceOverAllocation");
   outstanding[r.Invoice]-=r.Amount;
  }
  decimal refunds=0;
  foreach(var m in movements)
  {
   if(m==null||!Identity(m.Key,m.Customer,m.Project)||!Money(m.Amount)||m.Amount==0||
      !byDeposit.TryGetValue(m.Deposit,out var d)||d.Customer!=m.Customer||d.Project!=m.Project||m.Date<d.Date)
    return Stop("InvalidAdvanceMovement");
   if(m.Amount>remaining[m.Deposit])return Stop("AdvanceOverAllocation");
   if(m.Invoice is Guid key)
   {
    if(!byInvoice.TryGetValue(key,out var i)||i.Customer!=m.Customer||i.Project!=m.Project)return Stop("InvalidInvoiceApplication");
    if(m.Amount>outstanding[key])return Stop("InvoiceOverAllocation");
    outstanding[key]-=m.Amount;
   }
   else refunds+=m.Amount;
   remaining[m.Deposit]-=m.Amount;
  }
  if(invoices.Any(i=>outstanding[i.Key]!=i.Outstanding))return Stop("InvoiceBalanceMismatch");
  if(deposits.Any(d=>remaining[d.Key]!=d.Remaining))return Stop("AdvanceBalanceMismatch");
  var cash=deposits.Sum(d=>d.Amount)+receipts.Sum(r=>r.Amount)-refunds;
  var ar=outstanding.Values.Sum();var liability=remaining.Values.Sum();var income=invoices.Sum(i=>i.Amount);
  // No expenses, tax, opening balances, credits or other cash movements in this fixture.
  var calculated=new TestLedgerBalances(ar,cash,income,0m,ar+cash,liability,income,income);
  if(new[]{ar,cash,income,ar+cash,liability}.Any(x=>!Money(x))||calculated.Assets!=liability+income)return Stop("UnsupportedBalance");
  return calculated==observed?new("MatchedForReview",calculated):new("SummaryMismatch",calculated);
 }
}
