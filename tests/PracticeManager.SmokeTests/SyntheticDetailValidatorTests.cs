using System.Text.Json;
using PracticeManager.Core;
internal static class SyntheticDetailValidatorTests
{
 public static void Run(Action<bool,string> check)
 {
  var k=new SyntheticDetailKeys(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid());
  JsonElement Doc(object x)=>JsonSerializer.SerializeToElement(x);
  var invoice=Doc(new{Key=k.Invoice,id=k.Invoice,Customer=k.Customer,TaxCodeEnabled=false,ProjectEnabled=false,Lines=new[]{new{Account=k.Income,Qty=1m,SalesUnitPrice=100m}}});
  var receipt=Doc(new{Key=k.Receipt,id=k.Receipt,TaxCodeEnabled=false,ProjectEnabled=false,ReceivedIn=k.ReceivingBankAccount,Lines=new[]{new{Account=k.Receivables,AccountsReceivableSalesInvoice=k.Invoice,Amount=40m}}});
  var payment=Doc(new{Key=k.Payment,id=k.Payment,TaxCodeEnabled=false,ProjectEnabled=false,PaidFrom=k.ReceivingBankAccount,Lines=new[]{new{Account=k.Expense,Amount=10m}}});
  var income=Doc(new{Key=k.Income,id=k.Income,Code="4000",Name="Engineering Services Income",IsProfitAndLossAccount=true,Group=Guid.NewGuid()});
  var expense=Doc(new{Key=k.Expense,id=k.Expense,Code="6000",Name="Office Supplies",IsProfitAndLossAccount=true,Group=Guid.NewGuid()});
  SyntheticDetailValidation Eval(JsonElement? i=null,JsonElement? r=null,JsonElement? p=null,JsonElement? inc=null,JsonElement? exp=null,SyntheticDetailKeys? keys=null)=>SyntheticDetailValidator.Evaluate(keys??k,i??invoice,r??receipt,p??payment,inc??income,exp??expense);
  JsonElement Replace(JsonElement j,string old,string value)=>JsonDocument.Parse(j.GetRawText().Replace(old,value,StringComparison.Ordinal)).RootElement.Clone();
  check(Eval().MatchesObservedRelationships&&Eval().Outcome=="PartialMatch","observed detail fields and customer match fixture but do not certify group");
  check(Eval(i:Replace(invoice,k.Invoice.ToString(),Guid.NewGuid().ToString())).Failures.Contains("InvoiceIdentity"),"wrong returned invoice identity blocked");
  check(Eval(r:Replace(receipt,k.Invoice.ToString(),Guid.NewGuid().ToString())).Failures.Contains("ReceiptAllocation"),"wrong receipt invoice UUID blocked");
  check(Eval(r:Replace(receipt,k.Receivables.ToString(),k.Income.ToString())).Failures.Contains("ReceiptAllocation"),"wrong receipt ledger account blocked");
  check(Eval(r:Replace(receipt,k.ReceivingBankAccount.ToString(),Guid.NewGuid().ToString())).Failures.Contains("ReceiptBankAccount"),"wrong receiving bank account blocked");
  check(Eval(p:Replace(payment,k.ReceivingBankAccount.ToString(),Guid.NewGuid().ToString())).Failures.Contains("PaymentBankAccount"),"wrong payment bank account blocked");
  check(Eval(p:Replace(payment,k.Expense.ToString(),k.Income.ToString())).Failures.Contains("PaymentLine"),"wrong payment expense role blocked");
  var tax=Eval(i:Replace(invoice,"\"TaxCodeEnabled\":false","\"TaxCodeEnabled\":true"));
  check(tax.Outcome=="PartialMatch","enabled tax feature with no assignments matches observed fixture");
  var project=Eval(i:Replace(invoice,"\"ProjectEnabled\":false","\"ProjectEnabled\":true"));
  check(project.Outcome=="PartialMatch","enabled project feature with no assignments matches observed fixture");
  JsonElement Enable(JsonElement x)=>Replace(Replace(x,"\"TaxCodeEnabled\":false","\"TaxCodeEnabled\":true"),"\"ProjectEnabled\":false","\"ProjectEnabled\":true");
  check(Eval(i:Enable(invoice),r:Enable(receipt),p:Enable(payment)).Outcome=="PartialMatch","all enabled flags with absent assignments match live fixture");
  check(Eval(i:Replace(invoice,"\"Customer\":","\"TaxCode\":null,\"Customer\":")).RequiresFeatureReview,"null tax assignment requires review");
  check(Eval(r:Replace(receipt,"\"Amount\":40","\"Project\":\"\",\"Amount\":40")).RequiresFeatureReview,"empty project line assignment requires review");
  check(Eval(p:Replace(payment,"\"Amount\":10","\"TaxAmount\":0,\"Amount\":10")).RequiresFeatureReview,"zero tax line requires review");
  check(Eval(i:Replace(invoice,"\"Qty\":1","\"Discount\":0,\"Qty\":1")).RequiresFeatureReview,"unverified discount field requires review");
  check(Eval(r:Replace(receipt,"\"Amount\":40","\"project\":null,\"Amount\":40")).RequiresFeatureReview,"case variation cannot bypass schema review");
  check(Eval(p:Replace(payment,"\"PaidFrom\":","\"Unexpected\":{},\"PaidFrom\":")).RequiresFeatureReview,"unknown root container requires review");
  check(Eval(i:Replace(invoice,k.Customer.ToString(),Guid.NewGuid().ToString())).Failures.Contains("InvoiceCustomer"),"wrong invoice customer blocked");
  check(Eval(i:Replace(invoice,"\"Customer\":\""+k.Customer.ToString()+"\",","")).Failures.Contains("InvoiceCustomer"),"absent invoice customer blocked");
  check(Eval(i:Replace(invoice,"\"TaxCodeEnabled\":false,","")).RequiresFeatureReview,"missing flag requires review");
  check(Eval(i:Replace(invoice,"\"ProjectEnabled\":false","\"ProjectEnabled\":\"false\"")).RequiresFeatureReview,"nonboolean flag requires review");
  check(Eval(i:Replace(invoice,"\"TaxCodeEnabled\":false","\"TaxCodeEnabled\":true"),r:Replace(receipt,k.Invoice.ToString(),Guid.NewGuid().ToString())).Outcome=="Blocked","feature flag review cannot mask bad allocation");
  check(Eval(i:Replace(invoice,"\"Qty\":1","\"Qty\":2")).Failures.Contains("InvoiceLine"),"unexpected invoice quantity blocked");
  check(Eval(r:Replace(receipt,"\"Amount\":40","\"Amount\":\"40\"")).Failures.Contains("ReceiptAllocation"),"numeric string rejected");
  check(Eval(inc:Replace(income,"\"IsProfitAndLossAccount\":true","\"IsProfitAndLossAccount\":false")).Failures.Contains("IncomeForm"),"non-P&L account blocked");
  check(Eval(exp:Replace(expense,"\"6000\"","\"4000\"")).Failures.Contains("ExpenseForm"),"wrong expense code blocked");
  check(Eval(keys:k with{Expense=k.Income}).Failures.Contains("InvalidExpectedKeys"),"duplicate expected identities blocked");
  check(Eval(i:Replace(invoice,"\"Key\":","\"Key\":null,\"Key\":")).Failures.Contains("InvoiceIdentity"),"duplicate form identity rejected");
  var register=new SyntheticRegisterEvidence(100m,60m,40m,10m,true,true,true);
  var summary=new TestLedgerBalances(60m,30m,100m,10m,90m,0m,90m,90m);
  TestLedgerReconciliationResult Bridge(SyntheticRegisterEvidence e,TestLedgerBalances s,JsonElement? i=null,JsonElement? r=null)=>SyntheticReconciliationBridge.Evaluate(k,i??Enable(invoice),r??Enable(receipt),Enable(payment),income,expense,e,s);
  check(Bridge(register,summary).Matches,"verified synthetic details and independent summary reconcile");
  check(Bridge(register with{CompleteStableTestScope=false},summary).Outcome=="Blocked","incomplete acquisition blocked");
  check(Bridge(register with{IndependentGroupsAndBankVerified=false},summary).Outcome=="Blocked","unverified classifications blocked");
  check(Bridge(register with{SingleCurrencyAndZeroOpeningConfirmed=false},summary).Outcome=="Blocked","unverified currency/opening balances blocked");
  check(Bridge(register with{ReceiptAmount=39m},summary).Outcome=="Blocked","changed register amounts blocked");
  check(Bridge(register,summary with{Cash=31m}).Outcome=="Mismatch","independent summary mismatch detected");
  check(Bridge(register,summary,i:Replace(invoice,"\"Qty\":1","\"Project\":null,\"Qty\":1")).Outcome=="Blocked","unknown assignment cannot reach reconciliation");
  check(Bridge(register,summary,r:Replace(receipt,k.Invoice.ToString(),Guid.NewGuid().ToString())).Outcome=="Blocked","incorrect receipt allocation cannot reach reconciliation");
 }
}
