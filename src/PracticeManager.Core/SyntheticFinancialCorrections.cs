namespace PracticeManager.Core;

// Append-only reference event model for a bounded synthetic accrual ledger.
// No API, persistence, actual transaction authorization or provider semantics.
public enum SyntheticCorrectionKind { Invoice,Receipt,Credit,Deposit,Application,Refund,Reversal }
public sealed record SyntheticCorrectionEvent(Guid Key,Guid Customer,Guid Project,DateOnly Date,
 SyntheticCorrectionKind Kind,decimal Amount,Guid? Target=null,Guid? Deposit=null);
public sealed record SyntheticCorrectionReview(string Outcome,TestLedgerBalances? Calculated=null)
{
 public bool MayPost=>false;
 public bool MayReverse=>false;
 public bool MayMarkPaid=>false;
 public override string ToString()=>"[Synthetic financial corrections: "+Outcome+"]";
}
public static class SyntheticFinancialCorrections
{
 const decimal Limit=999999999999.99m;
 static bool Money(decimal x)=>x>=0&&x<=Limit&&decimal.Round(x,2)==x;
 public static SyntheticCorrectionReview Evaluate(IReadOnlyList<SyntheticCorrectionEvent> events,
  IReadOnlyDictionary<Guid,decimal> reportedInvoices,IReadOnlyDictionary<Guid,decimal> reportedDeposits,TestLedgerBalances observed)
 {
  static SyntheticCorrectionReview Stop(string why)=>new(why);
  if(events==null||reportedInvoices==null||reportedDeposits==null||observed==null||events.Count>500||
     reportedInvoices.Count>200||reportedDeposits.Count>200)return Stop("InvalidScope");
  var history=new Dictionary<Guid,SyntheticCorrectionEvent>();var reversed=new HashSet<Guid>();
  var invoices=new Dictionary<Guid,decimal>();var deposits=new Dictionary<Guid,decimal>();
  var invoiceLimits=new Dictionary<Guid,decimal>();
  decimal cash=0,income=0;DateOnly previous=DateOnly.MinValue;
  bool SameScope(SyntheticCorrectionEvent e,SyntheticCorrectionEvent original)=>e.Customer==original.Customer&&e.Project==original.Project;
  bool Target(SyntheticCorrectionEvent e,Guid? key,SyntheticCorrectionKind kind)
   =>key is Guid id&&history.TryGetValue(id,out var source)&&source.Kind==kind&&SameScope(e,source)&&!reversed.Contains(id);
  foreach(var e in events)
  {
   if(e==null||e.Key==Guid.Empty||e.Customer==Guid.Empty||e.Project==Guid.Empty||history.ContainsKey(e.Key)||
      e.Date.Year<1900||e.Date<previous||!Enum.IsDefined(e.Kind)||!Money(e.Amount)||e.Amount==0)return Stop("InvalidEvent");
   previous=e.Date;
   switch(e.Kind)
   {
    case SyntheticCorrectionKind.Invoice:
     if(e.Target!=null||e.Deposit!=null)return Stop("InvalidInvoice");
     invoices.Add(e.Key,e.Amount);invoiceLimits.Add(e.Key,e.Amount);income+=e.Amount;break;
    case SyntheticCorrectionKind.Deposit:
     if(e.Target!=null||e.Deposit!=null)return Stop("InvalidDeposit");
     deposits.Add(e.Key,e.Amount);cash+=e.Amount;break;
    case SyntheticCorrectionKind.Receipt:
    case SyntheticCorrectionKind.Credit:
     if(e.Deposit!=null||!Target(e,e.Target,SyntheticCorrectionKind.Invoice))return Stop("InvalidInvoiceTarget");
     var i=e.Target!.Value;
     if(e.Amount>invoices[i])return Stop("InvoiceOverAllocation");
     invoices[i]-=e.Amount;
     if(e.Kind==SyntheticCorrectionKind.Receipt)cash+=e.Amount;
     else{income-=e.Amount;invoiceLimits[i]-=e.Amount;}
     break;
    case SyntheticCorrectionKind.Application:
     if(!Target(e,e.Target,SyntheticCorrectionKind.Invoice)||!Target(e,e.Deposit,SyntheticCorrectionKind.Deposit))return Stop("InvalidApplicationTarget");
     if(e.Amount>invoices[e.Target!.Value])return Stop("InvoiceOverAllocation");
     if(e.Amount>deposits[e.Deposit!.Value])return Stop("AdvanceOverAllocation");
     invoices[e.Target.Value]-=e.Amount;deposits[e.Deposit.Value]-=e.Amount;break;
    case SyntheticCorrectionKind.Refund:
     if(e.Target!=null||!Target(e,e.Deposit,SyntheticCorrectionKind.Deposit))return Stop("InvalidRefundTarget");
     if(e.Amount>deposits[e.Deposit!.Value])return Stop("AdvanceOverAllocation");
     deposits[e.Deposit.Value]-=e.Amount;cash-=e.Amount;break;
    case SyntheticCorrectionKind.Reversal:
     if(e.Deposit!=null||e.Target is not Guid originalKey||!history.TryGetValue(originalKey,out var original)||
        !SameScope(e,original)||e.Amount!=original.Amount||reversed.Contains(originalKey))
      return Stop("InvalidReversal");
     // Whole-event reversal only. Invoice/deposit originals and reversal-of-
     // reversal require a separate correction contract; never delete history.
     switch(original.Kind)
     {
      case SyntheticCorrectionKind.Receipt:
       invoices[original.Target!.Value]+=e.Amount;cash-=e.Amount;break;
      case SyntheticCorrectionKind.Credit:
       invoices[original.Target!.Value]+=e.Amount;invoiceLimits[original.Target.Value]+=e.Amount;income+=e.Amount;break;
      case SyntheticCorrectionKind.Application:
       invoices[original.Target!.Value]+=e.Amount;deposits[original.Deposit!.Value]+=e.Amount;break;
      case SyntheticCorrectionKind.Refund:
       deposits[original.Deposit!.Value]+=e.Amount;cash+=e.Amount;break;
      default:return Stop("UnsupportedReversalKind");
     }
     reversed.Add(originalKey);break;
   }
   if(!Money(cash)||!Money(income)||invoices.Any(x=>!Money(x.Value)||x.Value>invoiceLimits[x.Key])||
      deposits.Any(x=>!Money(x.Value)||x.Value>(x.Key==e.Key?e.Amount:history[x.Key].Amount)))
    return Stop("UnsupportedIntermediateBalance");
   history.Add(e.Key,e);
  }
  if(reportedInvoices.Count!=invoices.Count||invoices.Any(x=>!reportedInvoices.TryGetValue(x.Key,out var n)||n!=x.Value))return Stop("InvoiceBalanceMismatch");
  if(reportedDeposits.Count!=deposits.Count||deposits.Any(x=>!reportedDeposits.TryGetValue(x.Key,out var n)||n!=x.Value))return Stop("AdvanceBalanceMismatch");
  var ar=invoices.Values.Sum();var liability=deposits.Values.Sum();
  var calculated=new TestLedgerBalances(ar,cash,income,0m,ar+cash,liability,income,income);
  if(!Money(ar)||!Money(liability)||!Money(calculated.Assets)||calculated.Assets!=liability+income)return Stop("UnsupportedSummary");
  return calculated==observed?new("MatchedForReview",calculated):new("SummaryMismatch",calculated);
 }
}
