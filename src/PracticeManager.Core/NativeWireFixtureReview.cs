using System.Text.Json;
namespace PracticeManager.Core;

// Read-only review of the separately accepted native-wire fixture. No new
// profile enrollment, dispatch, mapping, retry or write authorization.
public sealed record NativeWireBatchReview(string Outcome,IReadOnlyList<SyntheticRecoveryAssessment> Findings,TestLedgerReconciliationResult? Reconciliation=null)
{
 public bool MayPost=>false;
 public bool MayRetry=>false;
 public override string ToString()=>$"Native wire batch review: {Outcome}";
}
public sealed partial class ManagerDesktopProbe
{
 private const string NativeReviewBusiness="CRC NATIVE WRITE TEST";
 private const string NativeReviewMarker="CRC Native Write Test Identity";
 public async Task<SyntheticRecoveryAssessment> ReviewNativeWireFixtureAsync(string endpoint,string secret,SyntheticRecoveryTarget target,
  bool singleUsdCurrencyConfirmed,CancellationToken cancellationToken=default)
 {
  var result=await ReviewNativeWireBatchAsync(endpoint,secret,new[]{target},singleUsdCurrencyConfirmed,null,null,cancellationToken).ConfigureAwait(false);
  return result.Findings[0];
 }
 // The optional ledger comparison is against independently supplied accounts
 // and summary, not a claim that the document registers contain all GL activity.
 public async Task<NativeWireBatchReview> ReviewNativeWireBatchAsync(string endpoint,string secret,IReadOnlyList<SyntheticRecoveryTarget> targets,
  bool singleUsdCurrencyConfirmed,TestLedgerAccounts? expectedAccounts,TestLedgerBalances? independentSummary,CancellationToken cancellationToken=default)
 {
  var requested=targets.ToArray();
  NativeWireBatchReview Stop(string reason)=>new("ReviewRequired",Array.AsReadOnly(Enumerable.Repeat(new SyntheticRecoveryAssessment(SyntheticRecoveryFinding.ReviewRequired,reason),Math.Clamp(requested.Length,1,60)).ToArray()));
  if(!singleUsdCurrencyConfirmed||requested.Length is <1 or >60||requested.Any(x=>x==null||x.MarkerKey==Guid.Empty||x.OperationKey==Guid.Empty)||
     requested.Select(x=>x.MarkerKey).Distinct().Count()!=1||requested.Select(x=>x.OperationKey).Distinct().Count()!=requested.Length||
     (expectedAccounts==null)!=(independentSummary==null))return Stop("OperatorScopeEvidenceRequired");
  var marker=requested[0].MarkerKey;
  using var deadline=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);deadline.CancelAfter(TimeSpan.FromSeconds(90));var ct=deadline.Token;
  async Task<JsonElement> Read(AccountingDiscoveryResource? resource=null,AccountingDetailResource? detail=null,Guid? key=null,string? markerName=null)
  {
   JsonElement root=default;
   var response=await RequestAsync(endpoint,secret,false,ct,resource:resource,markerName:markerName,
    readFinancial:resource is AccountingDiscoveryResource.SalesInvoices or AccountingDiscoveryResource.Receipts or AccountingDiscoveryResource.Payments,
    detail:detail,detailKey:key,capture:j=>root=j).ConfigureAwait(false);
   if(!response.Success)throw new AcquisitionStop(response.Outcome);
   if(!UniqueTree(root))throw new AcquisitionStop("DuplicateFixtureFields");
   return root;
  }
  async Task Marker()
  {
   var j=await Read(AccountingDiscoveryResource.IdentityFields,markerName:NativeReviewMarker).ConfigureAwait(false);
   var m=BusinessMarker.Parse(j,NativeReviewMarker);
   if(!j.TryGetProperty("business",out var b)||Str(b,"name")!=NativeReviewBusiness||!m.Success||m.MarkerKey!=marker)
    throw new AcquisitionStop("NativeFixtureMarkerMismatch");
  }
  async Task<(SyntheticRecoverySweep Sweep,JsonElement[] Evidence,TestLedgerInvoice[] Invoices,TestLedgerReceipt[] Receipts,TestLedgerPayment[] Payments)> Sweep()
  {
   var rows=new List<SyntheticRecoveryCandidate>();var evidence=new List<JsonElement>();var ids=new HashSet<Guid>();
   var invoices=new List<TestLedgerInvoice>();var receipts=new List<TestLedgerReceipt>();var payments=new List<TestLedgerPayment>();
   var routes=new[]{
    (AccountingDiscoveryResource.SalesInvoices,AccountingDetailResource.Invoice,RehearsalKind.Invoice),
    (AccountingDiscoveryResource.Receipts,AccountingDetailResource.Receipt,RehearsalKind.Receipt),
    (AccountingDiscoveryResource.Payments,AccountingDetailResource.Payment,RehearsalKind.ExpensePayment)};
   foreach(var (resource,detail,kind) in routes){
    var j=await Read(resource).ConfigureAwait(false);evidence.Add(j);
    var parsed=FinancialPageParser.Parse(j,resource);
    if(!parsed.Success||parsed.Financial is not { } page||page.BusinessName!=NativeReviewBusiness)
     throw new AcquisitionStop("NativeFixtureRegisterIncompleteOrInvalid");
    // All pages must fit the existing bounded first-page contract. A larger
    // business is not eligible; never treat a truncated register as absence.
    foreach(var row in page.Rows.OrderBy(r=>r.Key)){
     if(!ids.Add(row.Key))throw new AcquisitionStop("NativeFixtureDuplicateIdentity");
     var form=await Read(detail:detail,key:row.Key).ConfigureAwait(false);evidence.Add(form);
     var verified=SyntheticWireReadback.Inspect(kind,row.Key,marker,true,form);
     if(verified.Candidate is not { } candidate)throw new AcquisitionStop("NativeFixtureDetailUnverified");
     var amount=form.GetProperty("Lines")[0].GetProperty(kind==RehearsalKind.Invoice?"SalesUnitPrice":"Amount").GetDecimal();
     if(amount!=row.Amount)throw new AcquisitionStop("NativeFixtureRegisterAmountMismatch");
     rows.Add(candidate);
     if(expectedAccounts!=null){
      var line=form.GetProperty("Lines")[0];
      var account=Id(line,"Account");
      if(kind==RehearsalKind.Invoice)invoices.Add(new(row.Key,Id(form,"Customer"),account,amount,row.BalanceDue!.Value));
      else if(kind==RehearsalKind.Receipt){
       if(account!=expectedAccounts.Receivables)throw new AcquisitionStop("NativeFixtureReceivablesAccountMismatch");
       receipts.Add(new(row.Key,Id(form,"Customer"),Id(line,"AccountsReceivableSalesInvoice"),Id(form,"ReceivedIn"),amount));
      }else payments.Add(new(row.Key,Id(form,"PaidFrom"),account,amount));
     }
    }
   }
   // The independent currency assertion is operator supplied; the marker and
   // form evidence is actually acquired over GET. No ledger certification.
   return (new(marker,true,true,rows.AsReadOnly()),evidence.ToArray(),invoices.ToArray(),receipts.ToArray(),payments.ToArray());
  }
  try{
   await Marker().ConfigureAwait(false);
   var first=await Sweep().ConfigureAwait(false);
   var second=await Sweep().ConfigureAwait(false);
   await Marker().ConfigureAwait(false);
   if(ct.IsCancellationRequested)return Stop("Cancelled");
   if(first.Evidence.Length!=second.Evidence.Length||!first.Evidence.Zip(second.Evidence).All(pair=>JsonElement.DeepEquals(pair.First,pair.Second)))
    return Stop("NativeFixtureChanged");
   var findings=requested.Select(target=>SyntheticRecoveryReview.Evaluate(target,first.Sweep,second.Sweep)).ToArray();
   TestLedgerReconciliationResult? ledger=null;
   if(expectedAccounts!=null&&independentSummary!=null)
    ledger=TestLedgerReconciliation.Evaluate(expectedAccounts,second.Invoices,second.Receipts,second.Payments,independentSummary);
   return new(findings.All(x=>x.Finding==SyntheticRecoveryFinding.MatchedForReview)&&(ledger==null||ledger.Matches)?"MatchedForReview":"ReviewRequired",Array.AsReadOnly(findings),ledger);
  }catch(AcquisitionStop e){return Stop(e.Outcome);}
   catch(OperationCanceledException){return Stop("Cancelled");}
 }
}
