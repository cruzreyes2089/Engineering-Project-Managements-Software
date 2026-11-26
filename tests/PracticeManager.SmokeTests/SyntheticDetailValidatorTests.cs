using System.Text.Json;
using PracticeManager.Core;
internal static class SyntheticDetailValidatorTests
{
 public static void Run(Action<bool,string> check)
 {
  var k=new SyntheticDetailKeys(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid());
  JsonElement Doc(object x)=>JsonSerializer.SerializeToElement(x);
  var invoice=Doc(new{Key=k.Invoice,id=k.Invoice,TaxCodeEnabled=false,ProjectEnabled=false,Lines=new[]{new{Account=k.Income,Qty=1m,SalesUnitPrice=100m}}});
  var receipt=Doc(new{Key=k.Receipt,id=k.Receipt,TaxCodeEnabled=false,ProjectEnabled=false,ReceivedIn=k.ReceivingBankAccount,Lines=new[]{new{Account=k.Receivables,AccountsReceivableSalesInvoice=k.Invoice,Amount=40m}}});
  var payment=Doc(new{Key=k.Payment,id=k.Payment,TaxCodeEnabled=false,ProjectEnabled=false,PaidFrom=k.ReceivingBankAccount,Lines=new[]{new{Account=k.Expense,Amount=10m}}});
  var income=Doc(new{Key=k.Income,id=k.Income,Code="4000",Name="Engineering Services Income",IsProfitAndLossAccount=true,Group=Guid.NewGuid()});
  var expense=Doc(new{Key=k.Expense,id=k.Expense,Code="6000",Name="Office Supplies",IsProfitAndLossAccount=true,Group=Guid.NewGuid()});
  SyntheticDetailValidation Eval(JsonElement? i=null,JsonElement? r=null,JsonElement? p=null,JsonElement? inc=null,JsonElement? exp=null,SyntheticDetailKeys? keys=null)=>SyntheticDetailValidator.Evaluate(keys??k,i??invoice,r??receipt,p??payment,inc??income,exp??expense);
  JsonElement Replace(JsonElement j,string old,string value)=>JsonDocument.Parse(j.GetRawText().Replace(old,value,StringComparison.Ordinal)).RootElement.Clone();
  check(Eval().MatchesObservedRelationships&&Eval().Outcome=="PartialMatch","observed detail fields match fixture but do not certify customer or group");
  check(Eval(i:Replace(invoice,k.Invoice.ToString(),Guid.NewGuid().ToString())).Failures.Contains("InvoiceIdentity"),"wrong returned invoice identity blocked");
  check(Eval(r:Replace(receipt,k.Invoice.ToString(),Guid.NewGuid().ToString())).Failures.Contains("ReceiptAllocation"),"wrong receipt invoice UUID blocked");
  check(Eval(r:Replace(receipt,k.Receivables.ToString(),k.Income.ToString())).Failures.Contains("ReceiptAllocation"),"wrong receipt ledger account blocked");
  check(Eval(r:Replace(receipt,k.ReceivingBankAccount.ToString(),Guid.NewGuid().ToString())).Failures.Contains("ReceiptBankAccount"),"wrong receiving bank account blocked");
  check(Eval(p:Replace(payment,k.ReceivingBankAccount.ToString(),Guid.NewGuid().ToString())).Failures.Contains("PaymentBankAccount"),"wrong payment bank account blocked");
  check(Eval(p:Replace(payment,k.Expense.ToString(),k.Income.ToString())).Failures.Contains("PaymentLine"),"wrong payment expense role blocked");
  check(Eval(i:Replace(invoice,"\"TaxCodeEnabled\":false","\"TaxCodeEnabled\":true")).Failures.Contains("TaxFlags"),"unsupported tax flag blocked");
  check(Eval(i:Replace(invoice,"\"ProjectEnabled\":false","\"ProjectEnabled\":true")).Failures.Contains("ProjectFlags"),"unsupported project flag blocked");
  check(Eval(i:Replace(invoice,"\"Qty\":1","\"Qty\":2")).Failures.Contains("InvoiceLine"),"unexpected invoice quantity blocked");
  check(Eval(r:Replace(receipt,"\"Amount\":40","\"Amount\":\"40\"")).Failures.Contains("ReceiptAllocation"),"numeric string rejected");
  check(Eval(inc:Replace(income,"\"IsProfitAndLossAccount\":true","\"IsProfitAndLossAccount\":false")).Failures.Contains("IncomeForm"),"non-P&L account blocked");
  check(Eval(exp:Replace(expense,"\"6000\"","\"4000\"")).Failures.Contains("ExpenseForm"),"wrong expense code blocked");
  check(Eval(keys:k with{Expense=k.Income}).Failures.Contains("InvalidExpectedKeys"),"duplicate expected identities blocked");
  check(Eval(i:Replace(invoice,"\"Key\":","\"Key\":null,\"Key\":")).Failures.Contains("InvoiceIdentity"),"duplicate form identity rejected");
 }
}
