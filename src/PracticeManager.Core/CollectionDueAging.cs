namespace PracticeManager.Core;

// Explicit dates from the same acquisition. Provider mapping is not yet qualified.
// A draft date, default payment term or manually guessed date is not evidence.
public sealed record ObservedInvoiceDueDate(Guid InvoiceKey,string Reference,Guid? CustomerKey,DateOnly Issued,DateOnly DueDate,DateTimeOffset ObservedUtc)
{
 public override string ToString()=>"[Invoice due-date observation]";
}
public sealed record CollectionDueAgeRow(Guid InvoiceKey,string Reference,DateOnly Issued,DateOnly? DueDate,decimal Outstanding,int? DaysOverdue,string Status)
{
 public override string ToString()=>"[Invoice due-age row]";
}
public sealed record CollectionDueAgeReport(DateOnly EvaluationDate,DateTimeOffset BalanceObservedUtc,IReadOnlyList<CollectionDueAgeRow> Rows,decimal Outstanding,decimal FutureIssued,decimal UnknownDue,decimal NotYetDue,decimal DueToday,decimal Overdue1To30,decimal Overdue31To60,decimal Overdue61To90,decimal Overdue91Plus)
{
 public decimal KnownOverdue=>Overdue1To30+Overdue31To60+Overdue61To90+Overdue91Plus;
 public bool CompleteDueDateCoverage=>UnknownDue==0;
 public override string ToString()=>"[Due-date aging review]";
}
public static class CollectionDueAging
{
 // Pure calculation for the future qualified adapter; not an authority for posting,
 // payment requests or automated communications. Evaluation date is not a historical
 // balance date: every balance remains associated with the acquisition timestamp.
 public static CollectionDueAgeReport Calculate(CollectionPreview observation,IReadOnlyList<ObservedInvoiceDueDate> dueDates,DateOnly evaluationDate)
 {
  if(dueDates==null||dueDates.Count>20||evaluationDate.Year<1900)throw new ArgumentException("Valid bounded due-date evidence required.");
  _=CollectionAging.Calculate(observation,evaluationDate);
  var invoices=observation.Invoices!.ToDictionary(x=>x.Key);
  var observed=observation.ObservedUtc!.Value;
  var dates=new Dictionary<Guid,DateOnly>();
  foreach(var d in dueDates){
   if(d==null||!invoices.TryGetValue(d.InvoiceKey,out var invoice)||d.Reference!=invoice.Reference||d.CustomerKey!=invoice.CustomerKey||d.Issued!=invoice.Issued||d.ObservedUtc!=observed||d.DueDate<d.Issued||!dates.TryAdd(d.InvoiceKey,d.DueDate))throw new ArgumentException("Due-date identity, timing or supported date range mismatch.");
  }
  var rows=new List<CollectionDueAgeRow>();
  decimal total=0,future=0,unknown=0,notDue=0,today=0,a=0,b=0,c=0,e=0;
  foreach(var invoice in observation.Invoices!){
   if(invoice.Outstanding==0)continue;
   var amount=invoice.Outstanding;total+=amount;
   DateOnly? due=dates.TryGetValue(invoice.Key,out var found)?found:null;
   int? days=null;string status;
   if(invoice.Issued>evaluationDate){status="Future issue date";future+=amount;}
   else if(due==null){status="Due date unavailable";unknown+=amount;}
   else{
    var difference=evaluationDate.DayNumber-due.Value.DayNumber;
    if(difference<0){status="Not yet due";notDue+=amount;}
    else if(difference==0){status="Due today";today+=amount;days=0;}
    else{days=difference;
     if(difference<=30){status="1–30 overdue";a+=amount;}
     else if(difference<=60){status="31–60 overdue";b+=amount;}
     else if(difference<=90){status="61–90 overdue";c+=amount;}
     else{status="91+ overdue";e+=amount;}
    }
   }
   rows.Add(new(invoice.Key,invoice.Reference,invoice.Issued,due,amount,days,status));
  }
  if(total!=future+unknown+notDue+today+a+b+c+e)throw new InvalidOperationException("Due-age buckets do not reconcile.");
  return new(evaluationDate,observed,rows.AsReadOnly(),total,future,unknown,notDue,today,a,b,c,e);
 }
}
