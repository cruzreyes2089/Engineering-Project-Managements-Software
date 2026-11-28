using System.Text.Json;
using PracticeManager.Core;
internal static class SyntheticWireCompilerTests
{
 public static void Run(Action<bool,string> check)
 {
  var marker=Guid.NewGuid();var customer=Guid.NewGuid();var income=Guid.NewGuid();var ar=Guid.NewGuid();
  var expense=Guid.NewGuid();var cash=Guid.NewGuid();var externalInvoice=Guid.NewGuid();var date=new DateOnly(2026,11,25);
  var invoice=new SyntheticWireIntent(Guid.NewGuid(),marker,RehearsalKind.Invoice,income,125m,date,"USD",customer);
  var receipt=new SyntheticWireIntent(Guid.NewGuid(),marker,RehearsalKind.Receipt,ar,50m,date,"USD",customer,cash,externalInvoice);
  var payment=new SyntheticWireIntent(Guid.NewGuid(),marker,RehearsalKind.ExpensePayment,expense,20m,date,"USD",CashAccountKey:cash,VendorContact:"Synthetic vendor");
  var i=SyntheticWireCompiler.Compile(invoice);var r=SyntheticWireCompiler.Compile(receipt);var p=SyntheticWireCompiler.Compile(payment);
  JsonElement Parse(SyntheticWirePayload v)=>JsonDocument.Parse(v.Json).RootElement.Clone();
  bool Fields(JsonElement j,params string[] fields)=>j.EnumerateObject().Select(x=>x.Name).Order(StringComparer.Ordinal).SequenceEqual(fields.Order(StringComparer.Ordinal));
  bool Id(JsonElement j,string field,Guid id)=>j.GetProperty(field).GetGuid()==id;
  var ij=Parse(i);var il=ij.GetProperty("Lines")[0];
  check(Fields(ij,"Customer","IssueDate","Reference","Description","Lines")&&Fields(il,"Account","Qty","SalesUnitPrice"),"invoice candidate uses only observed writable fields");
  check(Id(ij,"Customer",customer)&&Id(il,"Account",income)&&il.GetProperty("Qty").GetInt32()==1&&il.GetProperty("SalesUnitPrice").GetDecimal()==125m,"invoice candidate preserves amount and relationships");
  var rj=Parse(r);var rl=rj.GetProperty("Lines")[0];
  check(Fields(rj,"Date","Reference","Description","PaidBy","Customer","ReceivedIn","Lines")&&Fields(rl,"Account","AccountsReceivableSalesInvoice","Amount"),"receipt candidate uses verified invoice allocation fields");
  check(rj.GetProperty("PaidBy").GetInt32()==1&&Id(rj,"Customer",customer)&&Id(rj,"ReceivedIn",cash)&&Id(rl,"Account",ar)&&Id(rl,"AccountsReceivableSalesInvoice",externalInvoice)&&rl.GetProperty("Amount").GetDecimal()==50m,"receipt candidate binds payer, bank and invoice");
  var pj=Parse(p);var pl=pj.GetProperty("Lines")[0];
  check(Fields(pj,"Date","Reference","Contact","PaidFrom","Lines")&&Fields(pl,"Account","Amount"),"payment candidate uses Contact not Payee");
  check(pj.GetProperty("Contact").GetString()=="Synthetic vendor"&&Id(pj,"PaidFrom",cash)&&Id(pl,"Account",expense)&&pl.GetProperty("Amount").GetDecimal()==20m,"payment candidate binds cash and expense");
  check(ij.GetProperty("IssueDate").GetString()=="2026-11-25"&&rj.GetProperty("Date").GetString()=="2026-11-25"&&pj.GetProperty("Date").GetString()=="2026-11-25","candidate dates use invariant ISO form");
  check(new[]{i,r,p}.All(x=>Parse(x).GetProperty("Reference").GetString()==SyntheticRecoveryReview.ReferenceFor(x.Intent.OperationKey)),"candidate reference derived from stable operation identity");
  check(SyntheticWireCompiler.Compile(invoice with{Amount=125.00m}).Fingerprint==i.Fingerprint,"wire fingerprint canonical decimal");
  check(SyntheticWireCompiler.Compile(receipt with{CashAccountKey=Guid.NewGuid()}).Fingerprint!=r.Fingerprint,"receipt receiving bank enters full wire hash");
  check(SyntheticWireCompiler.Compile(receipt with{AllocatedInvoiceKey=Guid.NewGuid()}).Fingerprint!=r.Fingerprint,"receipt allocation enters full wire hash");
  check(SyntheticWireCompiler.Compile(invoice with{AccountingDate=date.AddDays(1)}).Fingerprint!=i.Fingerprint,"date enters wire hash");
  check(SyntheticWireCompiler.Compile(payment with{VendorContact="Changed vendor"}).Fingerprint!=p.Fingerprint,"vendor enters wire hash");
  check(SyntheticWireCompiler.Compile(payment with{MarkerKey=Guid.NewGuid()}).Fingerprint!=p.Fingerprint,"marker enters wire hash");
  check(SyntheticWireCompiler.Compile(payment with{OperationKey=Guid.NewGuid()}).Fingerprint!=p.Fingerprint,"operation enters wire hash");
  check(p.RecoveryTarget().Fingerprint==p.Fingerprint&&p.RecoveryTarget().MarkerKey==marker,"recovery uses full wire hash rather than legacy rehearsal hash");
  void Invalid(SyntheticWireIntent value,string label){try{SyntheticWireCompiler.Compile(value);}catch(ArgumentException){check(true,label);return;}throw new Exception("FAIL: "+label);}
  Invalid(invoice with{CashAccountKey=cash},"invoice cannot silently ignore cash field");
  Invalid(receipt with{CashAccountKey=null},"receipt requires receiving bank");
  Invalid(receipt with{AllocatedInvoiceKey=null},"receipt requires invoice allocation");
  Invalid(receipt with{CashAccountKey=ar},"receipt bank cannot equal AR account");
  Invalid(receipt with{AllocatedInvoiceKey=customer},"receipt cannot confuse invoice and customer");
  Invalid(payment with{CustomerKey=customer},"direct expense cannot contain customer");
  Invalid(payment with{VendorContact=" "},"direct expense requires contact");
  Invalid(payment with{VendorContact="Synthetic\nVendor"},"control characters in contact rejected");
  Invalid(payment with{Amount=20.001m},"sub-cent wire amount rejected");
  Invalid(payment with{Currency="EUR"},"unsupported synthetic currency blocked");
  Invalid(payment with{MarkerKey=Guid.Empty},"wire intent requires marker");
  check(!p.ToString().Contains(p.Reference)&&!payment.ToString().Contains("Synthetic vendor"),"wire display redacts payload");
 }
}
