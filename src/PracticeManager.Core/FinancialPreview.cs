using System.Text.Json;
using System.Globalization;
using System.Text.RegularExpressions;
namespace PracticeManager.Core;

public sealed record FinancialRow(Guid Key,decimal Amount,decimal? BalanceDue)
{
 public override string ToString()=>"[Financial row]";
}
public sealed record FinancialPage(AccountingDiscoveryResource Resource,string BusinessName,IReadOnlyList<FinancialRow> Rows)
{
 public override string ToString()=>$"Financial page: {Resource}, {Rows.Count} rows";
}
public sealed record FinancialPreview(int InvoiceCount,int ReceiptCount,int PaymentCount,decimal Invoiced,decimal Outstanding,decimal Receipts,decimal Payments)
{
 public decimal NetReceiptLessPayment=>Receipts-Payments;
 public bool MatchesFixture=>InvoiceCount==1&&ReceiptCount==1&&PaymentCount==1&&Invoiced==100m&&Outstanding==60m&&Receipts==40m&&Payments==10m;
 public override string ToString()=>"Financial preview [amounts omitted]";
 public static FinancialPreview? Combine(FinancialPage invoices,FinancialPage receipts,FinancialPage payments)
 {
  if(invoices.Resource!=AccountingDiscoveryResource.SalesInvoices||receipts.Resource!=AccountingDiscoveryResource.Receipts||payments.Resource!=AccountingDiscoveryResource.Payments||
     !string.Equals(invoices.BusinessName,receipts.BusinessName,StringComparison.Ordinal)||!string.Equals(invoices.BusinessName,payments.BusinessName,StringComparison.Ordinal)||
     invoices.Rows.Any(r=>r.BalanceDue==null)||receipts.Rows.Any(r=>r.BalanceDue!=null)||payments.Rows.Any(r=>r.BalanceDue!=null))return null;
  return new(invoices.Rows.Count,receipts.Rows.Count,payments.Rows.Count,invoices.Rows.Sum(r=>r.Amount),invoices.Rows.Sum(r=>r.BalanceDue!.Value),receipts.Rows.Sum(r=>r.Amount),payments.Rows.Sum(r=>r.Amount));
 }
}
internal static class FinancialPageParser
{
 static bool Unique(JsonElement v)=>v.ValueKind==JsonValueKind.Object&&v.EnumerateObject().Select(p=>p.Name).Distinct(StringComparer.Ordinal).Count()==v.EnumerateObject().Count();
 static bool Int(JsonElement v,string name,out int n){n=0;return v.TryGetProperty(name,out var p)&&p.ValueKind==JsonValueKind.Number&&p.TryGetInt32(out n);}
 static string? Text(JsonElement v,string name,int max)=>v.TryGetProperty(name,out var p)&&p.ValueKind==JsonValueKind.String&&p.GetString() is string s&&s.Length<=max&&!s.Any(char.IsControl)?s:null;
 // Original fixture retains null-only currency; dedicated due fixture requires exact USD.
 static bool Money(JsonElement r,string name,out decimal value,bool explicitUsd)
 {
  value=0;
  if(!r.TryGetProperty(name,out var p)||!Unique(p)||!p.TryGetProperty("currency",out var currency)||(explicitUsd?currency.ValueKind!=JsonValueKind.String||currency.GetString()!="USD":currency.ValueKind!=JsonValueKind.Null)||!p.TryGetProperty("value",out var n)||n.ValueKind!=JsonValueKind.Number)return false;
  var raw=n.GetRawText();
  // Deliberately bounded precision and magnitude. Reject exponent notation, excessive scale and negative values rather than guessing semantics.
  return Regex.IsMatch(raw,@"\A(?:0|[1-9][0-9]{0,11})(?:\.[0-9]{1,6})?\z",RegexOptions.CultureInvariant)&&decimal.TryParse(raw,NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out value);
 }
 internal static AccountingProbeResult Parse(JsonElement root,AccountingDiscoveryResource resource,bool explicitUsd=false)
 {
  AccountingProbeResult Invalid()=>new("InvalidFinancialPage",200);
  var arrayName=resource switch{AccountingDiscoveryResource.SalesInvoices=>"salesInvoices",AccountingDiscoveryResource.Receipts=>"receipts",AccountingDiscoveryResource.Payments=>"payments",_=>""};
  if(arrayName==""||!Unique(root)||!root.TryGetProperty("business",out var b)||!Unique(b)||Text(b,"name",512) is not string business||string.IsNullOrWhiteSpace(business)||
     !Int(root,"skip",out var skip)||skip!=0||!Int(root,"pageSize",out var size)||size<1||size>20||!Int(root,"totalRecords",out var total)||total<0||
     !root.TryGetProperty(arrayName,out var a)||a.ValueKind!=JsonValueKind.Array)return Invalid();
  if(total>size)return new("FinancialPageIncomplete",200);
  if(a.GetArrayLength()!=total)return Invalid();
  var rows=new List<FinancialRow>();var ids=new HashSet<Guid>();
  foreach(var row in a.EnumerateArray()){
   if(!Unique(row)||!Guid.TryParse(Text(row,"key",64),out var id)||id==Guid.Empty||!ids.Add(id))return Invalid();
   var invoice=resource==AccountingDiscoveryResource.SalesInvoices;
   if(!Money(row,invoice?"invoiceAmount":"amount",out var amount,explicitUsd))return new("UnsupportedFinancialAmount",200);
   decimal? balance=null;
   if(invoice){if(!Money(row,"balanceDue",out var due,explicitUsd)||due>amount)return new("UnsupportedFinancialAmount",200);balance=due;}
   rows.Add(new(id,amount,balance));
  }
  return new("Connected",200,"Object",Financial:new(resource,business,rows.AsReadOnly()));
 }
}
