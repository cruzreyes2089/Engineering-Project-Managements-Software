namespace PracticeManager.Core;

// Explicit evidence boundary: the acquisition layer must compare these facts
// against complete, stable, enrolled synthetic business reads. Not a live adapter.
public sealed record SyntheticRegisterEvidence(
 decimal InvoiceAmount,decimal BalanceDue,decimal ReceiptAmount,decimal PaymentAmount,
 bool CompleteStableTestScope,bool IndependentGroupsAndBankVerified,bool SingleCurrencyAndZeroOpeningConfirmed);
public static class SyntheticReconciliationBridge
{
 public static TestLedgerReconciliationResult Evaluate(SyntheticDetailKeys keys,System.Text.Json.JsonElement invoice,System.Text.Json.JsonElement receipt,
  System.Text.Json.JsonElement payment,System.Text.Json.JsonElement income,System.Text.Json.JsonElement expense,
  SyntheticRegisterEvidence register,TestLedgerBalances independentlyObserved)
 {
  static TestLedgerReconciliationResult Block(string reason)=>new("Blocked",Array.AsReadOnly(new[]{reason}));
  var details=SyntheticDetailValidator.Evaluate(keys,invoice,receipt,payment,income,expense);
  if(details.Outcome!="PartialMatch"||details.Failures.Count!=0)return Block("DetailReviewRequired");
  if(!register.CompleteStableTestScope||!register.IndependentGroupsAndBankVerified||!register.SingleCurrencyAndZeroOpeningConfirmed)return Block("AcquisitionEvidenceRequired");
  if(register.InvoiceAmount!=100m||register.BalanceDue!=60m||register.ReceiptAmount!=40m||register.PaymentAmount!=10m)return Block("FixtureAmounts");
  // Bank record UUID is used as cash identity for this normalized fixture,
  // not asserted to equal the general ledger cash control account UUID.
  // Receipt customer is DERIVED from verified invoice allocation, not a payer identity.
  return TestLedgerReconciliation.Evaluate(
   new(keys.Receivables,keys.ReceivingBankAccount,keys.Income,keys.Expense),
   new[]{new TestLedgerInvoice(keys.Invoice,keys.Customer,keys.Income,register.InvoiceAmount,register.BalanceDue)},
   new[]{new TestLedgerReceipt(keys.Receipt,keys.Customer,keys.Invoice,keys.ReceivingBankAccount,register.ReceiptAmount)},
   new[]{new TestLedgerPayment(keys.Payment,keys.ReceivingBankAccount,keys.Expense,register.PaymentAmount)},
   independentlyObserved);
 }
}
