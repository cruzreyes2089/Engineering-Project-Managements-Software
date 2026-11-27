using System.Security.Cryptography;
using System.Text.Json;
namespace PracticeManager.Core;

// Offline, narrow synthetic settlement review. Complete, independent scope is a
// caller claim, not proof of an authoritative ledger. No transport or mutation.
public sealed record SyntheticSettlementSnapshot(Guid MarkerKey,bool Complete,bool IndependentEvidenceVerified,
 bool SingleUsdCurrency,TestLedgerAccounts Accounts,IReadOnlyList<TestLedgerInvoice> Invoices,
 IReadOnlyList<TestLedgerReceipt> Receipts,IReadOnlyList<TestLedgerPayment> Payments,TestLedgerBalances Balances)
{
 public override string ToString()=>"[Synthetic settlement snapshot]";
}
public sealed record SyntheticReceiptCapacityResult(string Outcome,decimal? RemainingBefore=null,decimal? RemainingAfter=null,string? ReviewFingerprint=null)
{
 public bool MaySend=>false;
 public bool MayReserveFunds=>false;
 public bool MayAutomaticallyRetry=>false;
 public override string ToString()=>"[Synthetic receipt capacity: "+Outcome+"]";
}
public static class SyntheticReceiptCapacityReview
{
 static string Hash(object value)=>Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));
 static string SnapshotHash(SyntheticSettlementSnapshot s)=>Hash(new{
  s.MarkerKey,s.Accounts,Invoices=s.Invoices.OrderBy(x=>x.Key).ToArray(),
  Receipts=s.Receipts.OrderBy(x=>x.Key).ToArray(),Payments=s.Payments.OrderBy(x=>x.Key).ToArray(),s.Balances});
 public static SyntheticReceiptCapacityResult Evaluate(SyntheticWireIntent intent,SyntheticSettlementSnapshot first,
  SyntheticSettlementSnapshot second,string? expectedReviewFingerprint=null)
 {
  static SyntheticReceiptCapacityResult Block(string reason)=>new(reason);
  if(intent.Kind!=RehearsalKind.Receipt)return Block("ReceiptRequired");
  SyntheticWirePayload payload;
  try{payload=SyntheticWireCompiler.Compile(intent);}
  catch(ArgumentException){return Block("InvalidReceipt");}
  foreach(var s in new[]{first,second})
  {
   if(s.MarkerKey!=intent.MarkerKey)return Block("MarkerMismatch");
   if(!s.Complete||!s.IndependentEvidenceVerified||!s.SingleUsdCurrency)return Block("IncompleteEvidence");
   if(s.Accounts==null||s.Invoices==null||s.Receipts==null||s.Payments==null||s.Balances==null||
      s.Invoices.Any(x=>x==null)||s.Receipts.Any(x=>x==null)||s.Payments.Any(x=>x==null))
    return Block("InvalidEvidence");
   var result=TestLedgerReconciliation.Evaluate(s.Accounts,s.Invoices,s.Receipts,s.Payments,s.Balances);
   if(!result.Matches)return Block("LedgerNotReconciled");
  }
  var a=SnapshotHash(first);var b=SnapshotHash(second);
  if(a!=b)return Block("EvidenceChanged");
  if(intent.AccountKey!=second.Accounts.Receivables||intent.CashAccountKey!=second.Accounts.Cash)
   return Block("AccountMismatch");
  var invoice=second.Invoices.SingleOrDefault(x=>x.Key==intent.AllocatedInvoiceKey);
  if(invoice==null||invoice.Customer!=intent.CustomerKey)return Block("InvoiceOrCustomerMismatch");
  if(intent.Amount>invoice.ReportedBalance)return Block("InsufficientInvoiceBalance");
  var hash=Hash(new{Domain="CRC-SYNTHETIC-RECEIPT-CAPACITY-V1",Wire=payload.Fingerprint,Snapshot=b});
  if(expectedReviewFingerprint!=null&&expectedReviewFingerprint!=hash)return Block("StaleReview");
  return new("CapacityForReview",invoice.ReportedBalance,invoice.ReportedBalance-intent.Amount,hash);
 }
}
