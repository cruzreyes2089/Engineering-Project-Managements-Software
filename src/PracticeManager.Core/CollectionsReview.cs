using System.Globalization;
using System.Text.Json;
namespace PracticeManager.Core;

public sealed record CollectionInvoice(Guid Key,string Reference,DateOnly Issued,decimal Amount,decimal Outstanding,Guid? CustomerKey=null)
{
 public override string ToString()=>"[Synthetic collection invoice]";
}
public sealed record CollectionPreview(string Outcome,IReadOnlyList<CollectionInvoice>? Invoices=null,DateTimeOffset? ObservedUtc=null,IReadOnlyList<ObservedInvoiceDueDate>? DueDates=null,Guid? DueFixtureMarker=null)
{
 public decimal? TotalOutstanding=>Invoices?.Sum(x=>x.Outstanding);
 public override string ToString()=>"Collection preview: "+Outcome;
}
public sealed partial class ManagerDesktopProbe
{
 // This adapter qualifies the first synthetic fixture only. The date and
 // outstanding amount are observations, not a legal due date or a ledger.
 public Task<CollectionPreview> ReadSyntheticCollectionsAsync(string endpoint,string secret,Guid marker,bool singleUsdConfirmed,CancellationToken cancellationToken=default)
  =>ReadCollectionsScopeAsync(endpoint,secret,marker,singleUsdConfirmed,false,cancellationToken);
 // Dedicated operator-confirmed read-only fixture, not enrolled in the original
 // accounting profile. Only this exact business and three known invoices qualify.
 public Task<CollectionPreview> ReadSyntheticDueDateFixtureAsync(string endpoint,string secret,bool explicitlyConfirmed,CancellationToken cancellationToken=default)
  =>ReadCollectionsScopeAsync(endpoint,secret,null,explicitlyConfirmed,true,cancellationToken);
 private async Task<CollectionPreview> ReadCollectionsScopeAsync(string endpoint,string secret,Guid? marker,bool singleUsdConfirmed,bool dueFixture,CancellationToken cancellationToken)
 {
  CollectionPreview Stop(string outcome)=>new(outcome);
  if(marker==Guid.Empty||!singleUsdConfirmed||!dueFixture&&marker==null)return Stop("OperatorScopeEvidenceRequired");
  var business=dueFixture?"CRC DUE DATE TEST":FixtureBusiness;
  var markerName=dueFixture?"CRC Due Date Test Identity":BusinessMarker.TestName;
  using var deadline=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);deadline.CancelAfter(TimeSpan.FromSeconds(90));var ct=deadline.Token;
  async Task<JsonElement> Read(AccountingDiscoveryResource? resource=null,AccountingDetailResource? detail=null,Guid? key=null,string? markerName=null)
  {
   JsonElement root=default;
   var response=await RequestAsync(endpoint,secret,false,ct,resource:resource,markerName:markerName,readFinancial:resource==AccountingDiscoveryResource.SalesInvoices,detail:detail,detailKey:key,capture:j=>root=j).ConfigureAwait(false);
   if(!response.Success)throw new AcquisitionStop(response.Outcome);
   if(!UniqueTree(root))throw new AcquisitionStop("DuplicateCollectionFields");
   return root;
  }
  async Task Marker()
  {
   var root=await Read(AccountingDiscoveryResource.IdentityFields,markerName:markerName).ConfigureAwait(false);
   var parsed=BusinessMarker.Parse(root,markerName);
   if(!root.TryGetProperty("business",out var b)||b.ValueKind!=JsonValueKind.Object||Str(b,"name")!=business||!parsed.Success||!parsed.MarkerKey.HasValue||marker.HasValue&&parsed.MarkerKey!=marker)throw new AcquisitionStop("CollectionMarkerMismatch");
   if(dueFixture&&(!root.TryGetProperty("totalRecords",out var total)||!total.TryGetInt32(out var count)||count!=1))throw new AcquisitionStop("CollectionMarkerMismatch");
   marker=parsed.MarkerKey;
  }
  async Task<(CollectionInvoice[] Rows,JsonElement[] Evidence,DateOnly[] Dates)> Sweep()
  {
   var root=await Read(AccountingDiscoveryResource.SalesInvoices).ConfigureAwait(false);
   var parsed=FinancialPageParser.Parse(root,AccountingDiscoveryResource.SalesInvoices,explicitUsd:dueFixture);
   if(!parsed.Success||parsed.Financial is not { } page||page.BusinessName!=business)throw new AcquisitionStop("CollectionRegisterIncompleteOrInvalid");
   if(dueFixture&&page.Rows.Count!=3)throw new AcquisitionStop("DueFixtureCountMismatch");
   var evidence=new List<JsonElement>{root};var rows=new List<CollectionInvoice>();var dates=new List<DateOnly>();var references=new HashSet<string>(StringComparer.Ordinal);
   foreach(var row in page.Rows.OrderBy(x=>x.Key)){
    var detail=await Read(detail:AccountingDetailResource.Invoice,key:row.Key).ConfigureAwait(false);evidence.Add(detail);
    if(Id(detail,"Key")!=row.Key||Id(detail,"id")!=row.Key)throw new AcquisitionStop("CollectionInvoiceIdentityMismatch");
    var raw=Str(detail,"IssueDate");
    if(!DateTime.TryParseExact(raw,"yyyy-MM-dd'T'HH:mm:ss",CultureInfo.InvariantCulture,DateTimeStyles.None,out var issued)||issued.TimeOfDay!=TimeSpan.Zero||issued.Year<1900)throw new AcquisitionStop("CollectionIssueDateUnverified");
    if(row.BalanceDue is not decimal balance||decimal.Round(row.Amount,2)!=row.Amount||decimal.Round(balance,2)!=balance)throw new AcquisitionStop("CollectionCurrencyPrecisionUnsupported");
    var reference=Str(detail,"Reference");
    if(detail.TryGetProperty("Reference",out var refNode)&&refNode.ValueKind!=JsonValueKind.String&&refNode.ValueKind!=JsonValueKind.Null)throw new AcquisitionStop("CollectionReferenceUnverified");
    if(reference!=null&&(reference.Length>180||reference.Any(char.IsControl)))throw new AcquisitionStop("CollectionReferenceUnverified");
    var registerRow=root.GetProperty("salesInvoices").EnumerateArray().Single(x=>Id(x,"key")==row.Key);
    if(registerRow.TryGetProperty("reference",out var registerRef)&&registerRef.ValueKind!=JsonValueKind.Null&&(registerRef.ValueKind!=JsonValueKind.String||registerRef.GetString()!=reference))throw new AcquisitionStop("CollectionReferenceMismatch");
    Guid? customer=null;
    if(detail.TryGetProperty("Customer",out var customerNode)&&customerNode.ValueKind!=JsonValueKind.Null){
     if(customerNode.ValueKind!=JsonValueKind.String||!Guid.TryParseExact(customerNode.GetString(),"D",out var customerId)||customerId==Guid.Empty)throw new AcquisitionStop("CollectionCustomerIdentityInvalid");
     customer=customerId;
    }
    var day=DateOnly.FromDateTime(issued);
    if(dueFixture){
     if(reference==null||!references.Add(reference)||customer==null||day!=new DateOnly(2026,11,1)||row.Amount!=100m||balance!=100m||Str(registerRow,"reference")!=reference||Str(registerRow,"issueDate")!="2026-11-01")throw new AcquisitionStop("DueFixtureIdentityMismatch");
     var expected=reference switch{"CRC-DUE-IMMEDIATE"=>new DateOnly(2026,11,1),"CRC-DUE-NET30"=>new DateOnly(2026,12,1),"CRC-DUE-FIXED"=>new DateOnly(2026,12,15),_=>throw new AcquisitionStop("DueFixtureReferenceMismatch")};
     var parsedDue=ManagerInvoiceTerms.Parse(detail,day);
     if(parsedDue==null||parsedDue.DueDate!=expected||parsedDue.Mode!=(reference switch{"CRC-DUE-IMMEDIATE"=>"Immediate","CRC-DUE-NET30"=>"NetDays",_=>"FixedDate"})||reference=="CRC-DUE-NET30"&&parsedDue.NetDays!=30)throw new AcquisitionStop("DueFixtureTermsMismatch");
     dates.Add(parsedDue.DueDate);
    }
    rows.Add(new(row.Key,reference??"",day,row.Amount,balance,customer));
   }
   if(dueFixture&&rows.Select(x=>x.CustomerKey).Distinct().Count()!=1)throw new AcquisitionStop("DueFixtureCustomerMismatch");
   return(rows.ToArray(),evidence.ToArray(),dates.ToArray());
  }
  try{
   await Marker().ConfigureAwait(false);var a=await Sweep().ConfigureAwait(false);var b=await Sweep().ConfigureAwait(false);await Marker().ConfigureAwait(false);
   if(ct.IsCancellationRequested)return Stop("Cancelled");
   if(a.Evidence.Length!=b.Evidence.Length||!a.Evidence.Zip(b.Evidence).All(x=>JsonElement.DeepEquals(x.First,x.Second))||!a.Rows.SequenceEqual(b.Rows))return Stop("CollectionRegisterChanged");
   var utc=DateTimeOffset.UtcNow;
   if(dueFixture&&!a.Dates.SequenceEqual(b.Dates))return Stop("CollectionRegisterChanged");
   var dueDates=dueFixture?Array.AsReadOnly(b.Rows.Select((x,i)=>new ObservedInvoiceDueDate(x.Key,x.Reference,x.CustomerKey,x.Issued,b.Dates[i],utc)).ToArray()):null;
   return new("ObservedForReview",Array.AsReadOnly(b.Rows),utc,dueDates,dueFixture?marker:null);
  }catch(AcquisitionStop e){return Stop(e.Outcome);}catch(OperationCanceledException){return Stop("Cancelled");}
 }
}
public sealed record CollectionAgeRow(string Reference,DateOnly Issued,decimal Amount,decimal Outstanding,int? DaysSinceIssue,string AgeBand);
public sealed record CollectionAgeReport(IReadOnlyList<CollectionAgeRow> Rows,decimal Outstanding,decimal FutureDated,decimal Age0To30,decimal Age31To60,decimal Age61To90,decimal AgeOver90);
public static class CollectionAging
{
 // Age since issue, not days overdue. No assumed payment terms.
 public static CollectionAgeReport Calculate(CollectionPreview observation,DateOnly asOf)
 {
  if(observation.Outcome!="ObservedForReview"||observation.Invoices==null||observation.ObservedUtc==null||asOf==DateOnly.MinValue||observation.Invoices.Count>20)throw new ArgumentException("Complete current observation required.");
  var ids=new HashSet<Guid>();var rows=new List<CollectionAgeRow>();decimal future=0,a=0,b=0,c=0,d=0,total=0;
  foreach(var invoice in observation.Invoices){
   if(invoice.Key==Guid.Empty||!ids.Add(invoice.Key)||invoice.Issued.Year<1900||invoice.Amount<0||invoice.Outstanding<0||invoice.Outstanding>invoice.Amount||decimal.Round(invoice.Amount,2)!=invoice.Amount||decimal.Round(invoice.Outstanding,2)!=invoice.Outstanding)throw new ArgumentException("Invalid collection observation.");
   if(invoice.Outstanding==0)continue;
   var days=asOf.DayNumber-invoice.Issued.DayNumber;string band;total+=invoice.Outstanding;
   if(days<0){band="Future issue date";future+=invoice.Outstanding;}
   else if(days<=30){band="0–30";a+=invoice.Outstanding;}
   else if(days<=60){band="31–60";b+=invoice.Outstanding;}
   else if(days<=90){band="61–90";c+=invoice.Outstanding;}
   else{band="91+";d+=invoice.Outstanding;}
   rows.Add(new(invoice.Reference,invoice.Issued,invoice.Amount,invoice.Outstanding,days<0?null:days,band));
  }
  return new(rows.AsReadOnly(),total,future,a,b,c,d);
 }
}
