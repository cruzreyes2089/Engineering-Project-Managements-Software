using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace PracticeManager.Core;

// Candidate form payloads from owner-assisted API2 sandbox observations.
// Pure offline compiler. No URL, HTTP method, transport, credentials, file writes,
// actual posting authorization or connection to the offline rehearsal journal.
public sealed record SyntheticWireIntent(Guid OperationKey,Guid MarkerKey,RehearsalKind Kind,Guid AccountKey,
 decimal Amount,DateOnly AccountingDate,string Currency,Guid? CustomerKey=null,Guid? CashAccountKey=null,
 Guid? AllocatedInvoiceKey=null,string? VendorContact=null)
{
 public override string ToString()=>"[Synthetic wire intent]";
}
public sealed class SyntheticWirePayload
{
 internal SyntheticWirePayload(SyntheticWireIntent intent,string reference,string json,string fingerprint)
 { Intent=intent;Reference=reference;Json=json;Fingerprint=fingerprint; }
 public SyntheticWireIntent Intent{get;}
 public string Reference{get;}
 public string Json{get;}
 public string Fingerprint{get;}
 public SyntheticRecoveryTarget RecoveryTarget(Guid? knownExternalKey=null)=>
  new(Intent.OperationKey,Intent.MarkerKey,Intent.Kind,Reference,Fingerprint,knownExternalKey);
 public override string ToString()=>"[Offline candidate form payload]";
}
public static class SyntheticWireCompiler
{
 public static SyntheticWirePayload Compile(SyntheticWireIntent intent)
 {
  bool Id(Guid? value)=>value.HasValue&&value.Value!=Guid.Empty;
  bool vendor= !string.IsNullOrWhiteSpace(intent.VendorContact)&&intent.VendorContact.Length<=200&&
   intent.VendorContact==intent.VendorContact.Trim()&&!intent.VendorContact.Any(c=>char.IsControl(c)||char.IsSurrogate(c));
  // USD is a synthetic assumption and is not emitted as an undocumented API field.
  if(intent.OperationKey==Guid.Empty||intent.MarkerKey==Guid.Empty||intent.AccountKey==Guid.Empty||
     !Enum.IsDefined(intent.Kind)||intent.Amount<=0m||intent.Amount>999999999999.99m||
     decimal.Round(intent.Amount,2)!=intent.Amount||intent.AccountingDate==DateOnly.MinValue||intent.Currency!="USD")
   throw new ArgumentException("Invalid synthetic wire intent.");
  var allowed=intent.Kind switch
  {
   RehearsalKind.Invoice=>Id(intent.CustomerKey)&&intent.CashAccountKey==null&&intent.AllocatedInvoiceKey==null&&intent.VendorContact==null,
   RehearsalKind.Receipt=>Id(intent.CustomerKey)&&Id(intent.CashAccountKey)&&Id(intent.AllocatedInvoiceKey)&&intent.VendorContact==null,
   RehearsalKind.ExpensePayment=>intent.CustomerKey==null&&Id(intent.CashAccountKey)&&intent.AllocatedInvoiceKey==null&&vendor,
   _=>false
  };
  var ids=new Guid?[]{intent.OperationKey,intent.MarkerKey,intent.AccountKey,intent.CustomerKey,intent.CashAccountKey,intent.AllocatedInvoiceKey}
   .Where(x=>x.HasValue).Select(x=>x!.Value).ToArray();
  if(!allowed||ids.Distinct().Count()!=ids.Length)throw new ArgumentException("Invalid synthetic wire relationships.");
  var reference=SyntheticRecoveryReview.ReferenceFor(intent.OperationKey);
  var date=intent.AccountingDate.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
  // Canonical two-decimal scale, invariant across equivalent decimal inputs.
  var amount=decimal.Parse(intent.Amount.ToString("0.00",CultureInfo.InvariantCulture),CultureInfo.InvariantCulture);
  var account=intent.AccountKey.ToString("D");
  object body=intent.Kind switch
  {
   RehearsalKind.Invoice=>new{Customer=intent.CustomerKey!.Value.ToString("D"),IssueDate=date,Reference=reference,
    Description="CRC synthetic invoice",Lines=new[]{new{Account=account,Qty=1,SalesUnitPrice=amount}}},
   RehearsalKind.Receipt=>new{Date=date,Reference=reference,Description="CRC synthetic receipt",PaidBy=1,
    Customer=intent.CustomerKey!.Value.ToString("D"),ReceivedIn=intent.CashAccountKey!.Value.ToString("D"),
    Lines=new[]{new{Account=account,AccountsReceivableSalesInvoice=intent.AllocatedInvoiceKey!.Value.ToString("D"),Amount=amount}}},
   RehearsalKind.ExpensePayment=>new{Date=date,Reference=reference,Contact=intent.VendorContact,
    PaidFrom=intent.CashAccountKey!.Value.ToString("D"),Lines=new[]{new{Account=account,Amount=amount}}},
   _=>throw new ArgumentException("Unknown kind.")
  };
  var json=JsonSerializer.Serialize(body);
  // Bind kind, marker, source operation, declared currency and every emitted field.
  // This is a new domain; never reinterpret old rehearsal fingerprints.
  var envelope=string.Join("|","CRC-SYNTHETIC-WIRE-V1",intent.MarkerKey.ToString("D"),intent.OperationKey.ToString("D"),
   intent.Kind.ToString(),intent.Currency,Convert.ToBase64String(Encoding.UTF8.GetBytes(json)));
  var hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(envelope)));
  return new(intent,reference,json,hash);
 }
}
