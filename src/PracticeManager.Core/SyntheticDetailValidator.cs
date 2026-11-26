using System.Text.Json;
namespace PracticeManager.Core;

// Source-only validator of the observed API2 forms. It never returns credentials,
// identifiers or raw JSON, and does not establish customer or group classification.
public sealed record SyntheticDetailKeys(Guid Invoice,Guid Receipt,Guid Payment,Guid Receivables,Guid Income,Guid Expense,Guid ReceivingBankAccount);
public sealed record SyntheticDetailValidation(string Outcome,IReadOnlyList<string> Failures)
{
 public bool MatchesObservedRelationships=>Outcome=="PartialMatch";
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
 public static SyntheticDetailValidation Evaluate(SyntheticDetailKeys k,JsonElement invoice,JsonElement receipt,JsonElement payment,JsonElement income,JsonElement expense)
 {
  var fail=new List<string>();
  var keys=new[]{k.Invoice,k.Receipt,k.Payment,k.Receivables,k.Income,k.Expense,k.ReceivingBankAccount};
  if(keys.Contains(Guid.Empty)||keys.Distinct().Count()!=keys.Length)return new("Blocked",Array.AsReadOnly(new[]{"InvalidExpectedKeys"}));
  void Check(bool ok,string field){if(!ok)fail.Add(field);}
  // Fixture specifically requires one line in each document, no tax or project flags.
  Check(Identity(invoice,k.Invoice),"InvoiceIdentity");
  Check(Identity(receipt,k.Receipt),"ReceiptIdentity");
  Check(Identity(payment,k.Payment),"PaymentIdentity");
  Check(Identity(income,k.Income),"IncomeIdentity");
  Check(Identity(expense,k.Expense),"ExpenseIdentity");
  if(fail.Count>0)return new("Blocked",fail.AsReadOnly());
  Check(BooleanField(invoice,"TaxCodeEnabled",false)&&BooleanField(receipt,"TaxCodeEnabled",false)&&BooleanField(payment,"TaxCodeEnabled",false),"TaxFlags");
  Check(BooleanField(invoice,"ProjectEnabled",false)&&BooleanField(receipt,"ProjectEnabled",false)&&BooleanField(payment,"ProjectEnabled",false),"ProjectFlags");
  Check(OneLine(invoice,out var il)&&GuidField(il,"Account",k.Income)&&NumberField(il,"Qty",1m)&&NumberField(il,"SalesUnitPrice",100m),"InvoiceLine");
  Check(OneLine(receipt,out var rl)&&GuidField(rl,"Account",k.Receivables)&&GuidField(rl,"AccountsReceivableSalesInvoice",k.Invoice)&&NumberField(rl,"Amount",40m),"ReceiptAllocation");
  Check(GuidField(receipt,"ReceivedIn",k.ReceivingBankAccount),"ReceiptBankAccount");
  Check(OneLine(payment,out var pl)&&GuidField(pl,"Account",k.Expense)&&NumberField(pl,"Amount",10m),"PaymentLine");
  Check(GuidField(payment,"PaidFrom",k.ReceivingBankAccount),"PaymentBankAccount");
  Check(BooleanField(income,"IsProfitAndLossAccount",true)&&TextField(income,"Code","4000")&&TextField(income,"Name","Engineering Services Income"),"IncomeForm");
  Check(BooleanField(expense,"IsProfitAndLossAccount",true)&&TextField(expense,"Code","6000")&&TextField(expense,"Name","Office Supplies"),"ExpenseForm");
  // Group IDs do not certify account classification. Customer UUID absent from
  // observed invoice detail; never synthesize it from a name or receipt allocation.
  return new(fail.Count==0?"PartialMatch":"Blocked",fail.AsReadOnly());
 }
}
