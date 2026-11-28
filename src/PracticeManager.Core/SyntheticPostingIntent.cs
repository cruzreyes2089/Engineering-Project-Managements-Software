using System.Globalization;
using System.Security.Cryptography;
using System.Text;
namespace PracticeManager.Core;

// A synthetic operation only. A future approved operational draft must retain
// its original OperationKey; regenerating it on retry defeats deduplication.
public enum RehearsalKind { Invoice,Receipt,ExpensePayment }
public sealed record SyntheticPostingIntent(Guid OperationKey,RehearsalKind Kind,Guid CustomerKey,Guid AccountKey,
 Guid? AllocatedInvoiceKey,decimal Amount,DateOnly AccountingDate,string Currency,Guid? CashAccountKey=null,string? VendorContact=null)
{
 public override string ToString()=>"[Synthetic posting intent]";
 public string Fingerprint()
 {
  if(OperationKey==Guid.Empty||AccountKey==Guid.Empty||
    !Enum.IsDefined(Kind)||Amount<=0||Amount>999999999999.99m||decimal.Round(Amount,2)!=Amount||
    AccountingDate==DateOnly.MinValue||Currency==null||Currency.Length!=3||Currency.Any(c=>c<'A'||c>'Z')||
    (Kind!=RehearsalKind.ExpensePayment&&(CustomerKey==Guid.Empty||CashAccountKey!=null||VendorContact!=null))||
    (Kind==RehearsalKind.Invoice&&AllocatedInvoiceKey!=null)||
    (Kind==RehearsalKind.Receipt&&(!AllocatedInvoiceKey.HasValue||AllocatedInvoiceKey==Guid.Empty))||
    (Kind==RehearsalKind.ExpensePayment&&(CustomerKey!=Guid.Empty||AllocatedInvoiceKey!=null||!CashAccountKey.HasValue||CashAccountKey==Guid.Empty||CashAccountKey==AccountKey||
     string.IsNullOrWhiteSpace(VendorContact)||VendorContact.Length>200||VendorContact!=VendorContact.Trim()||VendorContact.Any(c=>char.IsControl(c)||char.IsSurrogate(c)))))
   throw new ArgumentException("Invalid synthetic intent.");
  // Preserve all existing invoice/receipt hashes byte-for-byte; expense has its own domain.
  var text=string.Join("|",Kind==RehearsalKind.ExpensePayment?"CRC-SYNTHETIC-EXPENSE-V1":"CRC-SYNTHETIC-INTENT-V1",OperationKey.ToString("D"),Kind.ToString(),CustomerKey.ToString("D"),
   AccountKey.ToString("D"),AllocatedInvoiceKey?.ToString("D")??"-",Amount.ToString("0.00",CultureInfo.InvariantCulture),
   AccountingDate.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),Currency);
  if(Kind==RehearsalKind.ExpensePayment)text+="|"+CashAccountKey!.Value.ToString("D")+"|"+Convert.ToBase64String(Encoding.UTF8.GetBytes(VendorContact!));
  return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
 }
}
public sealed record RehearsalResult(string Outcome,string? State=null,Guid? AttemptKey=null)
{
 public override string ToString()=>$"Posting rehearsal: {Outcome}";
}
public enum SimulatedOutcome { Uncertain,Confirmed }
public enum LifecycleHoldReason { Clone,Restore,SuspectedIdentityChange }
