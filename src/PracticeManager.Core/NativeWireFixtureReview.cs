using System.Text.Json;
namespace PracticeManager.Core;

// Read-only review of the separately accepted native-wire fixture. No new
// profile enrollment, dispatch, mapping, retry or write authorization.
public sealed partial class ManagerDesktopProbe
{
 private const string NativeReviewBusiness="CRC NATIVE WRITE TEST";
 private const string NativeReviewMarker="CRC Native Write Test Identity";
 public async Task<SyntheticRecoveryAssessment> ReviewNativeWireFixtureAsync(string endpoint,string secret,SyntheticRecoveryTarget target,
  bool singleUsdCurrencyConfirmed,CancellationToken cancellationToken=default)
 {
  static SyntheticRecoveryAssessment Stop(string reason)=>new(SyntheticRecoveryFinding.ReviewRequired,reason);
  if(!singleUsdCurrencyConfirmed||target.MarkerKey==Guid.Empty||target.OperationKey==Guid.Empty)return Stop("OperatorScopeEvidenceRequired");
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
   if(!j.TryGetProperty("business",out var b)||Str(b,"name")!=NativeReviewBusiness||!m.Success||m.MarkerKey!=target.MarkerKey)
    throw new AcquisitionStop("NativeFixtureMarkerMismatch");
  }
  async Task<(SyntheticRecoverySweep Sweep,JsonElement[] Evidence)> Sweep()
  {
   var rows=new List<SyntheticRecoveryCandidate>();var evidence=new List<JsonElement>();var ids=new HashSet<Guid>();
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
     var verified=SyntheticWireReadback.Inspect(kind,row.Key,target.MarkerKey,true,form);
     if(verified.Candidate is not { } candidate)throw new AcquisitionStop("NativeFixtureDetailUnverified");
     var amount=form.GetProperty("Lines")[0].GetProperty(kind==RehearsalKind.Invoice?"SalesUnitPrice":"Amount").GetDecimal();
     if(amount!=row.Amount)throw new AcquisitionStop("NativeFixtureRegisterAmountMismatch");
     rows.Add(candidate);
    }
   }
   // The independent currency assertion is operator supplied; the marker and
   // form evidence is actually acquired over GET. No ledger certification.
   return (new(target.MarkerKey,true,true,rows.AsReadOnly()),evidence.ToArray());
  }
  try{
   await Marker().ConfigureAwait(false);
   var first=await Sweep().ConfigureAwait(false);
   var second=await Sweep().ConfigureAwait(false);
   await Marker().ConfigureAwait(false);
   if(ct.IsCancellationRequested)return Stop("Cancelled");
   if(first.Evidence.Length!=second.Evidence.Length||!first.Evidence.Zip(second.Evidence).All(pair=>JsonElement.DeepEquals(pair.First,pair.Second)))
    return Stop("NativeFixtureChanged");
   return SyntheticRecoveryReview.Evaluate(target,first.Sweep,second.Sweep);
  }catch(AcquisitionStop e){return Stop(e.Outcome);}
   catch(OperationCanceledException){return Stop("Cancelled");}
 }
}
