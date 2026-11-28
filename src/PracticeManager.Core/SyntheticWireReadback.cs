using System.Globalization;
using System.Text.Json;
namespace PracticeManager.Core;

// Narrow observed readback contract. No network or approval. The caller must
// independently acquire the form, register key, enrolled marker and currency
// evidence; a matching form is not proof of provider identity or ledger posting.
public sealed record SyntheticWireReadbackResult(string Outcome,SyntheticRecoveryCandidate? Candidate=null)
{
 public override string ToString()=>$"Synthetic wire readback: {Outcome}";
}
public static class SyntheticWireReadback
{
 private static bool Exact(JsonElement e,params string[] names)
 {
  if(e.ValueKind!=JsonValueKind.Object)return false;
  var actual=e.EnumerateObject().Select(p=>p.Name).ToArray();
  return actual.Length==names.Length&&actual.Distinct(StringComparer.OrdinalIgnoreCase).Count()==actual.Length&&
   actual.Order(StringComparer.Ordinal).SequenceEqual(names.Order(StringComparer.Ordinal));
 }
 private static string? Str(JsonElement e,string name)=>e.TryGetProperty(name,out var p)&&p.ValueKind==JsonValueKind.String?p.GetString():null;
 private static Guid? Id(JsonElement e,string name)=>Guid.TryParseExact(Str(e,name),"D",out var id)&&id!=Guid.Empty?id:null;
 private static decimal? Number(JsonElement e,string name)=>e.TryGetProperty(name,out var p)&&p.ValueKind==JsonValueKind.Number&&p.TryGetDecimal(out var value)?value:null;
 private static bool True(JsonElement e,string name)=>e.TryGetProperty(name,out var p)&&p.ValueKind==JsonValueKind.True;
 private static bool Metadata(JsonElement e,string name)=>Str(e,name) is string s&&s.Length<=512&&!s.Any(char.IsControl);
 public static SyntheticWireReadbackResult Inspect(RehearsalKind kind,Guid registerKey,Guid independentlyObservedMarker,
  bool singleUsdCurrencyConfirmed,JsonElement detail)
 {
  static SyntheticWireReadbackResult Stop(string reason)=>new(reason);
  if(registerKey==Guid.Empty||independentlyObservedMarker==Guid.Empty||!singleUsdCurrencyConfirmed||!Enum.IsDefined(kind))
   return Stop("ScopeEvidenceRequired");
  string[] common=["Reference","Lines","text","UniqueName","TaxCodeEnabled","ProjectEnabled","Key","id"];
  var specific=kind switch{
   RehearsalKind.Invoice=>new[]{"IssueDate","Customer","Description"},
   RehearsalKind.Receipt=>new[]{"Date","PaidBy","Customer","ReceivedIn","Description"},
   RehearsalKind.ExpensePayment=>new[]{"Date","PaidFrom","Contact"},_=>Array.Empty<string>()};
  if(!Exact(detail,common.Concat(specific).ToArray()))return Stop("UnverifiedHeaderFields");
  if(Id(detail,"Key")!=registerKey||Id(detail,"id")!=registerKey)return Stop("DocumentIdentityMismatch");
  // Both flags were true and no assignment fields were present in the observed sandbox.
  // Other values or unexpected fields require new evidence; never infer tax treatment.
  if(!True(detail,"TaxCodeEnabled")||!True(detail,"ProjectEnabled")||
     !Metadata(detail,"text")||!Metadata(detail,"UniqueName"))return Stop("UnverifiedDefaults");
  var reference=Str(detail,"Reference");
  // Existing CRC-SB references are intentionally not mapped to invented operation IDs.
  if(reference==null||reference.Length!=40||!reference.StartsWith("CRC-DEV-",StringComparison.Ordinal)||
     !Guid.TryParseExact(reference[8..],"N",out var operation)||operation==Guid.Empty||
     reference!=SyntheticRecoveryReview.ReferenceFor(operation))return Stop("UnverifiedOperationReference");
  var date=Str(detail,kind==RehearsalKind.Invoice?"IssueDate":"Date");
  if(date==null||date.Length!=19||!date.EndsWith("T00:00:00",StringComparison.Ordinal)||
     !DateOnly.TryParseExact(date[..10],"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var day))
   return Stop("UnverifiedDate");
  if(!detail.TryGetProperty("Lines",out var lines)||lines.ValueKind!=JsonValueKind.Array||lines.GetArrayLength()!=1)
   return Stop("UnverifiedLines");
  var line=lines[0];
  var fields=kind switch{
   RehearsalKind.Invoice=>new[]{"Account","Qty","SalesUnitPrice"},
   RehearsalKind.Receipt=>new[]{"Account","AccountsReceivableSalesInvoice","Amount"},
   RehearsalKind.ExpensePayment=>new[]{"Account","Amount"},_=>Array.Empty<string>()};
  if(!Exact(line,fields))return Stop("UnverifiedLineFields");
  var account=Id(line,"Account");
  var amount=Number(line,kind==RehearsalKind.Invoice?"SalesUnitPrice":"Amount");
  if(account==null||amount==null)return Stop("UnverifiedAmountOrAccount");
  Guid? customer=null,cash=null,allocation=null;string? vendor=null;
  if(kind==RehearsalKind.Invoice){
   if(Number(line,"Qty")!=1m||Str(detail,"Description")!="CRC synthetic invoice")return Stop("UnverifiedInvoiceFields");
   customer=Id(detail,"Customer");
  }else if(kind==RehearsalKind.Receipt){
   if(Number(detail,"PaidBy")!=1m||Str(detail,"Description")!="CRC synthetic receipt")return Stop("UnverifiedReceiptFields");
   customer=Id(detail,"Customer");cash=Id(detail,"ReceivedIn");allocation=Id(line,"AccountsReceivableSalesInvoice");
  }else{cash=Id(detail,"PaidFrom");vendor=Str(detail,"Contact");}
  // Rebuild the canonical wire form from observed values, not the requested target.
  // The provider-generated metadata is not an emitted field and is not hashed.
  try{
   var reconstructed=SyntheticWireCompiler.Compile(new(operation,independentlyObservedMarker,kind,account.Value,amount.Value,day,"USD",customer,cash,allocation,vendor));
   return new("CandidateForReview",new(registerKey,kind,reference,reconstructed.Fingerprint));
  }catch(ArgumentException){return Stop("InvalidObservedRelationships");}
 }
}
