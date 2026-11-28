using System.Text.Json;
namespace PracticeManager.Core;

// Independent summary and fixture setup are operator evidence, not fetched ledger totals.
public sealed record SyntheticSummaryEvidence(TestLedgerBalances Balances,bool SingleCurrencyAndZeroOpeningConfirmed);
public sealed partial class ManagerDesktopProbe
{
 private const string FixtureBusiness="CRC ACCOUNTING TEST";
 private sealed class AcquisitionStop(string outcome):Exception {public string Outcome{get;}=outcome;}
 private sealed record FixtureSweep(SyntheticDetailKeys Keys,JsonElement[] Documents,JsonElement[] Sources,SyntheticRegisterEvidence Register);
 private static bool UniqueTree(JsonElement e)=>e.ValueKind switch {
  JsonValueKind.Object=>e.EnumerateObject().Select(p=>p.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count()==e.EnumerateObject().Count()
    &&e.EnumerateObject().All(p=>UniqueTree(p.Value)),
  JsonValueKind.Array=>e.EnumerateArray().All(UniqueTree),_=>true};
 private static string? Str(JsonElement e,string name)=>e.ValueKind==JsonValueKind.Object&&e.TryGetProperty(name,out var p)&&p.ValueKind==JsonValueKind.String?p.GetString():null;
 private static Guid Id(JsonElement e,string name)=>Guid.TryParse(Str(e,name),out var g)&&g!=Guid.Empty?g:throw new AcquisitionStop("InvalidFixtureIdentifier");
 private static bool BusinessIsFixture(JsonElement e)=>e.TryGetProperty("business",out var b)&&Str(b,"name")==FixtureBusiness;
 private static JsonElement SelectFixture(JsonElement e,string array,string name)
 {
  if(!BusinessIsFixture(e)||!e.TryGetProperty("totalRecords",out var n)||n.ValueKind!=JsonValueKind.Number||!n.TryGetInt32(out var count)||count<1||count>20||
     !e.TryGetProperty(array,out var rows)||rows.ValueKind!=JsonValueKind.Array||rows.GetArrayLength()!=count)throw new AcquisitionStop("IncompleteFixtureRegister");
  var ids=new HashSet<Guid>();
  var matches=new List<JsonElement>();
  foreach(var row in rows.EnumerateArray()){
   if(!ids.Add(Id(row,"key")))throw new AcquisitionStop("DuplicateFixtureIdentifier");
   if(Str(row,"name")==name)matches.Add(row);
  }
  if(matches.Count!=1)throw new AcquisitionStop("FixtureSelectionAmbiguous");
  return matches[0];
 }
 public async Task<TestLedgerReconciliationResult> ReconcileSyntheticAsync(string endpoint,string secret,Guid enrolledMarker,
  Guid incomeRole,Guid expenseRole,SyntheticSummaryEvidence summary,CancellationToken cancellationToken=default)
 {
  static TestLedgerReconciliationResult Block(string reason)=>new("Blocked",Array.AsReadOnly(new[]{reason}));
  if(enrolledMarker==Guid.Empty||incomeRole==Guid.Empty||expenseRole==Guid.Empty||incomeRole==expenseRole)return Block("FixtureScopeNotConfigured");
  if(!summary.SingleCurrencyAndZeroOpeningConfirmed)return Block("OperatorEvidenceRequired");
  using var deadline=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);deadline.CancelAfter(TimeSpan.FromSeconds(90));var ct=deadline.Token;
  async Task<JsonElement> Read(AccountingDiscoveryResource? resource=null,int? customerSkip=null,AccountingDetailResource? detail=null,Guid? key=null,string? markerName=null,bool readFinancial=false)
  {
   JsonElement root=default;
   var r=await RequestAsync(endpoint,secret,false,ct,customerSkip,resource,markerName:markerName,readFinancial:readFinancial,detail:detail,detailKey:key,capture:j=>root=j).ConfigureAwait(false);
   if(!r.Success)throw new AcquisitionStop(r.Outcome);
   if(!UniqueTree(root))throw new AcquisitionStop("DuplicateFixtureFields");
   return root;
  }
  async Task Marker()
  {
   var root=await Read(AccountingDiscoveryResource.IdentityFields,markerName:BusinessMarker.TestName).ConfigureAwait(false);
   var m=BusinessMarker.Parse(root,BusinessMarker.TestName);
   if(!BusinessIsFixture(root)||!m.Success||m.MarkerKey!=enrolledMarker)throw new AcquisitionStop("MarkerMismatch");
  }
  async Task<FixtureSweep> Sweep()
  {
   var resources=new[]{AccountingDiscoveryResource.SalesInvoices,AccountingDiscoveryResource.Receipts,AccountingDiscoveryResource.Payments};
   var sources=new List<JsonElement>();var pages=new List<FinancialPage>();
   foreach(var resource in resources){
    var j=await Read(resource,readFinancial:true).ConfigureAwait(false);sources.Add(j);
    var parsed=FinancialPageParser.Parse(j,resource);
    if(!parsed.Success||parsed.Financial is not { } page||page.BusinessName!=FixtureBusiness||page.Rows.Count!=1)throw new AcquisitionStop("FixtureFinancialRegister");
    pages.Add(page);
   }
   if(FinancialPreview.Combine(pages[0],pages[1],pages[2])?.MatchesFixture!=true)throw new AcquisitionStop("FixtureAmounts");
   var customers=await Read(customerSkip:0).ConfigureAwait(false);sources.Add(customers);
   var cp=AccountingCustomerPageParser.Parse(customers,0);
   if(cp==null||cp.BusinessName!=FixtureBusiness||cp.Customers.Count!=cp.TotalRecords)throw new AcquisitionStop("IncompleteFixtureCustomers");
   var customer=cp.Customers.Where(c=>c.Name=="CRC Test Customer 001").ToArray();
   if(customer.Length!=1)throw new AcquisitionStop("FixtureCustomerAmbiguous");
   var accounts=await Read(AccountingDiscoveryResource.ChartOfAccounts).ConfigureAwait(false);sources.Add(accounts);
   var ar=AccountingAccountParser.Parse(accounts);
   if(ar==null||ar.BusinessName!=FixtureBusiness)throw new AcquisitionStop("FixtureAccounts");
   var income=ar.Rows.Where(a=>a.Code=="4000"&&a.Name=="Engineering Services Income").ToArray();
   var expense=ar.Rows.Where(a=>a.Code=="6000"&&a.Name=="Office Supplies").ToArray();
   var receivables=ar.Rows.Where(a=>a.Name.Equals("Accounts receivable",StringComparison.OrdinalIgnoreCase)).ToArray();
   if(income.Length!=1||expense.Length!=1||receivables.Length!=1||income[0].Key!=incomeRole||expense[0].Key!=expenseRole)throw new AcquisitionStop("FixtureRolesChanged");
   var bankList=await Read(AccountingDiscoveryResource.BankCashAccounts).ConfigureAwait(false);sources.Add(bankList);
   var bank=Id(SelectFixture(bankList,"bankAndCashAccounts","CRC Test Cash"),"key");
   var receiptRow=sources[1].GetProperty("receipts")[0];
   if(!receiptRow.TryGetProperty("receivedIn",out var receivedIn)||Id(receivedIn,"key")!=bank)throw new AcquisitionStop("FixtureBankMismatch");
   var keys=new SyntheticDetailKeys(pages[0].Rows[0].Key,pages[1].Rows[0].Key,pages[2].Rows[0].Key,receivables[0].Key,incomeRole,expenseRole,bank,customer[0].Key);
   var documents=new JsonElement[5];
   var kinds=new[]{AccountingDetailResource.Invoice,AccountingDetailResource.Receipt,AccountingDetailResource.Payment,AccountingDetailResource.ProfitAndLossAccount,AccountingDetailResource.ProfitAndLossAccount};
   var ids=new[]{keys.Invoice,keys.Receipt,keys.Payment,keys.Income,keys.Expense};
   for(var i=0;i<5;i++)documents[i]=await Read(detail:kinds[i],key:ids[i]).ConfigureAwait(false);
   var groupIncome=Id(documents[3],"Group");var groupExpense=Id(documents[4],"Group");
   if(groupIncome==groupExpense)throw new AcquisitionStop("FixtureGroups");
   var ig=await Read(detail:AccountingDetailResource.ProfitAndLossGroup,key:groupIncome).ConfigureAwait(false);
   var eg=await Read(detail:AccountingDetailResource.ProfitAndLossGroup,key:groupExpense).ConfigureAwait(false);
   // Live diagnostic confirmed group names; group Key/id fields have not yet been observed.
   // When supplied, require them to agree with the requested key; do not invent an absent field.
   bool GroupMatches(JsonElement j,Guid id,string[] names)=>names.Contains(Str(j,"Name"),StringComparer.OrdinalIgnoreCase)
      &&(!j.TryGetProperty("Key",out _)||Id(j,"Key")==id)&&(!j.TryGetProperty("id",out _)||Id(j,"id")==id);
   if(!GroupMatches(ig,groupIncome,new[]{"Income"})||!GroupMatches(eg,groupExpense,new[]{"Expenses","Less Expenses"}))throw new AcquisitionStop("FixtureGroups");
   sources.Add(ig);sources.Add(eg);
   var validation=SyntheticDetailValidator.Evaluate(keys,documents[0],documents[1],documents[2],documents[3],documents[4]);
   if(validation.Outcome!="PartialMatch")throw new AcquisitionStop("DetailReviewRequired");
   var register=new SyntheticRegisterEvidence(pages[0].Rows[0].Amount,pages[0].Rows[0].BalanceDue!.Value,pages[1].Rows[0].Amount,pages[2].Rows[0].Amount,true,true,summary.SingleCurrencyAndZeroOpeningConfirmed);
   return new(keys,documents,sources.ToArray(),register);
  }
  try{
   await Marker().ConfigureAwait(false);
   var first=await Sweep().ConfigureAwait(false);
   var second=await Sweep().ConfigureAwait(false);
   await Marker().ConfigureAwait(false);
   if(ct.IsCancellationRequested)return Block("Cancelled");
   // Two equal observations are not an atomic snapshot. Fail on any observed drift.
   if(first.Keys!=second.Keys||first.Register!=second.Register||
      !first.Sources.Zip(second.Sources).All(pair=>JsonElement.DeepEquals(pair.First,pair.Second))||
      !first.Documents.Zip(second.Documents).All(pair=>JsonElement.DeepEquals(pair.First,pair.Second)))return Block("FixtureChanged");
   var d=second.Documents;
   return SyntheticReconciliationBridge.Evaluate(second.Keys,d[0],d[1],d[2],d[3],d[4],second.Register,summary.Balances);
  }catch(AcquisitionStop e){return Block(e.Outcome);}
  catch(OperationCanceledException){return Block("Cancelled");}
 }
}
