using PracticeManager.Core;
internal static class CollectionDueAgingTests
{
 public static void Run(Action<bool,string> check)
 {
  var day=new DateOnly(2026,11,26);var utc=new DateTimeOffset(2026,11,26,12,0,0,TimeSpan.Zero);var customer=Guid.NewGuid();
  var invoices=new List<CollectionInvoice>();var dates=new List<ObservedInvoiceDueDate>();
  void Row(int? days,decimal balance,bool future=false)
  {
   var invoice=new CollectionInvoice(Guid.NewGuid(),"SYNTH-"+invoices.Count,day.AddDays(future?1:-100),100m,balance,customer);
   invoices.Add(invoice);
   if(days.HasValue)dates.Add(new(invoice.Key,invoice.Reference,invoice.CustomerKey,invoice.Issued,day.AddDays(-days.Value),utc));
  }
  Row(-5,1);Row(0,2);Row(1,3);Row(30,4);Row(31,5);Row(60,6);Row(61,7);Row(90,8);Row(91,9);Row(null,10);Row(-2,11,true);Row(2,0);
  var observation=new CollectionPreview("ObservedForReview",invoices.AsReadOnly(),utc);
  var report=CollectionDueAging.Calculate(observation,dates,day);
  check(report.Rows.Count==11&&report.Outstanding==66m&&report.FutureIssued==11m&&report.UnknownDue==10m&&report.NotYetDue==1m&&report.DueToday==2m,"due aging separates future, unknown, not due and due today");
  check(report.Overdue1To30==7m&&report.Overdue31To60==11m&&report.Overdue61To90==15m&&report.Overdue91Plus==9m&&report.KnownOverdue==42m&&!report.CompleteDueDateCoverage,"overdue boundaries and incomplete coverage");
  check(report.Rows.Single(x=>x.Status=="Due date unavailable").DaysOverdue==null&&report.Rows.Single(x=>x.Status=="Due today").DaysOverdue==0,"unknown due date is not zero days overdue");
  check(CollectionDueAging.Calculate(observation,Array.Empty<ObservedInvoiceDueDate>(),day).UnknownDue==55m,"no due evidence does not assume immediate or Net 30");
  void Invalid(Action action,string name){try{action();}catch(ArgumentException){check(true,name);return;}throw new Exception("FAIL: "+name);}
  Invalid(()=>CollectionDueAging.Calculate(observation,dates.Append(dates[0]).ToArray(),day),"duplicate due evidence rejected");
  Invalid(()=>CollectionDueAging.Calculate(observation,new[]{dates[0] with{InvoiceKey=Guid.NewGuid()}},day),"unobserved invoice due evidence rejected");
  Invalid(()=>CollectionDueAging.Calculate(observation,new[]{dates[0] with{Reference="Changed"}},day),"wrong reference due evidence rejected");
  Invalid(()=>CollectionDueAging.Calculate(observation,new[]{dates[0] with{CustomerKey=Guid.NewGuid()}},day),"wrong customer due evidence rejected");
  Invalid(()=>CollectionDueAging.Calculate(observation,new[]{dates[0] with{ObservedUtc=utc.AddSeconds(-1)}},day),"stale due evidence rejected");
  Invalid(()=>CollectionDueAging.Calculate(observation,new[]{dates[0] with{DueDate=dates[0].Issued.AddDays(-1)}},day),"due date before issue unsupported");
  Invalid(()=>CollectionDueAging.Calculate(new("Blocked"),dates,day),"unavailable balances not treated as zero");
  check(report.BalanceObservedUtc==utc&&report.EvaluationDate==day,"balance timestamp and evaluation date kept separate");
 }
}
