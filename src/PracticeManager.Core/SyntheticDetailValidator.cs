using System.Text.Json;
namespace PracticeManager.Core;

// Source-only validator of the observed API2 forms. It never returns credentials,
// identifiers or raw JSON. Group classification and register scope are separate checks.
public sealed record SyntheticDetailKeys(Guid Invoice,Guid Receipt,Guid Payment,Guid Receivables,Guid Income,Guid Expense,Guid ReceivingBankAccount,Guid Customer);
public sealed record SyntheticDetailValidation(string Outcome,IReadOnlyList<string> Failures)
{
 public bool MatchesObservedRelationships=>Outcome is "PartialMatch" or "FeatureReviewRequired";
 public bool RequiresFeatureReview=>Outcome=="FeatureReviewRequired";
}
public static class SyntheticDetailValidator
{
 static bool Unique(JsonElement v)=>v.ValueKind==JsonValueKind.Object&&v.EnumerateObject().Select(x=>x.Name).Distinct(StringComparer.Ordinal).Count()==v.EnumerateObject().Count();
 static bool GuidField(JsonElement v,string name,Guid expected)=>v.TryGetProperty(name,out var p)&&p.ValueKind==JsonValueKind.String&&Guid.TryParse(p.GetString(),out var id)&&id!=Guid.Empty&&id==expected;
 static bool TextField(JsonElement v,string name,string expected)=>v.TryGetProperty(name,out var p)&&p.ValueKind==JsonValueKind.String&&p.GetString()==expected;
 static bool BooleanField(JsonElement v,string name,bool expected)=>v.TryGetProperty(name,out var p)&&p.ValueKind==(expected?JsonValueKind.True:JsonValueKind.False);
 static bool NumberField(JsonElement v,string name,decimal expected)=>v.TryGetProperty(name,out var p)&&p.ValueKind==JsonValueKind.Number&&p.TryGetDecimal(out var n)&&n==expected;
 static bool OneLine(JsonElement v,out JsonElement line)
 {
  line=default;
  if(!v.TryGetProperty("Lines",out var a)||a.ValueKind!=JsonValueKind.Array||a.GetArrayLength()!=1)return false;
  line=a[0];return Unique(line);
 }
 static bool Identity(JsonElement v,Guid key)=>Unique(v)&&GuidField(v,"Key",key)&&GuidField(v,"id",key);
 static bool TypedFlag(JsonElement v,string name)=>v.TryGetProperty(name,out var p)&&p.ValueKind is JsonValueKind.True or JsonValueKind.False;
 // Exact fixture schema: unknown root/line fields require review. Even null,
 // zero or empty assignment fields are not accepted without a verified contract.
 static bool KnownFields(JsonElement v,string allowed)=>Unique(v)&&v.EnumerateObject().All(p=>allowed.Split(' ').Contains(p.Name,StringComparer.Ordinal));
 static bool HasAssignment(JsonElement v)=>v.EnumerateObject().Any(p=>(p.Name.StartsWith("Tax",StringComparison.OrdinalIgnoreCase)||p.Name.StartsWith("Project",StringComparison.OrdinalIgnoreCase))&&p.Name!="TaxCodeEnabled"&&p.Name!="ProjectEnabled");
 static bool FixtureFields(JsonElement document,string root,string line)
 {
  if(!KnownFields(document,root+" Key id Lines TaxCodeEnabled ProjectEnabled")||HasAssignment(document))return false;
  return OneLine(document,out var row)&&KnownFields(row,line)&&!HasAssignment(row);
 }
 public static SyntheticDetailValidation Evaluate(SyntheticDetailKeys k,JsonElement invoice,JsonElement receipt,JsonElement payment,JsonElement income,JsonElement expense)
 {
  var fail=new List<string>();
  var keys=new[]{k.Invoice,k.Receipt,k.Payment,k.Receivables,k.Income,k.Expense,k.ReceivingBankAccount,k.Customer};
  if(keys.Contains(Guid.Empty)||keys.Distinct().Count()!=keys.Length)return new("Blocked",Array.AsReadOnly(new[]{"InvalidExpectedKeys"}));
  void Check(bool ok,string field){if(!ok)fail.Add(field);}
  // Fixture requires one line in each document. Feature flags are not proof of applied tax or project allocation.
  Check(Identity(invoice,k.Invoice),"InvoiceIdentity");
  Check(Identity(receipt,k.Receipt),"ReceiptIdentity");
  Check(Identity(payment,k.Payment),"PaymentIdentity");
  Check(Identity(income,k.Income),"IncomeIdentity");
  Check(Identity(expense,k.Expense),"ExpenseIdentity");
  if(fail.Count>0)return new("Blocked",fail.AsReadOnly());
  Check(GuidField(invoice,"Customer",k.Customer),"InvoiceCustomer");
  // The live synthetic forms have both flags true but no assignment fields.
  // Require typed flags, reject any unverified fields instead of ignoring them.
  var review=new List<string>();
  if(!TypedFlag(invoice,"TaxCodeEnabled")||!TypedFlag(receipt,"TaxCodeEnabled")||!TypedFlag(payment,"TaxCodeEnabled"))review.Add("TaxFlags");
  if(!TypedFlag(invoice,"ProjectEnabled")||!TypedFlag(receipt,"ProjectEnabled")||!TypedFlag(payment,"ProjectEnabled"))review.Add("ProjectFlags");
  if(!FixtureFields(invoice,"Customer Description DueDate DueDateDate IssueDate Reference text UniqueName","Account Qty SalesUnitPrice")||
     !FixtureFields(receipt,"Date Description PaidBy ReceivedIn","Account AccountsReceivableSalesInvoice Amount")||
     !FixtureFields(payment,"Contact Date PaidFrom","Account Amount"))review.Add("UnverifiedDocumentFields");
  Check(OneLine(invoice,out var il)&&GuidField(il,"Account",k.Income)&&NumberField(il,"Qty",1m)&&NumberField(il,"SalesUnitPrice",100m),"InvoiceLine");
  Check(OneLine(receipt,out var rl)&&GuidField(rl,"Account",k.Receivables)&&GuidField(rl,"AccountsReceivableSalesInvoice",k.Invoice)&&NumberField(rl,"Amount",40m),"ReceiptAllocation");
  Check(GuidField(receipt,"ReceivedIn",k.ReceivingBankAccount),"ReceiptBankAccount");
  Check(OneLine(payment,out var pl)&&GuidField(pl,"Account",k.Expense)&&NumberField(pl,"Amount",10m),"PaymentLine");
  Check(GuidField(payment,"PaidFrom",k.ReceivingBankAccount),"PaymentBankAccount");
  Check(BooleanField(income,"IsProfitAndLossAccount",true)&&TextField(income,"Code","4000")&&TextField(income,"Name","Engineering Services Income"),"IncomeForm");
  Check(BooleanField(expense,"IsProfitAndLossAccount",true)&&TextField(expense,"Code","6000")&&TextField(expense,"Name","Office Supplies"),"ExpenseForm");
  // Group IDs alone do not certify classification; caller must separately
  // validate groups and acquisition scope. No posting or ledger authorization.
  if(fail.Count>0)return new("Blocked",fail.AsReadOnly());
  return review.Count>0?new("FeatureReviewRequired",review.AsReadOnly()):new("PartialMatch",Array.AsReadOnly(Array.Empty<string>()));
 }
}
