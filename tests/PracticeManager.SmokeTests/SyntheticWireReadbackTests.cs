using System.Text.Json;
using System.Text.Json.Nodes;
using PracticeManager.Core;
internal static class SyntheticWireReadbackTests
{
 public static void Run(Action<bool,string> check)
 {
  var marker=Guid.NewGuid();var customer=Guid.NewGuid();var bank=Guid.NewGuid();var externalInvoice=Guid.NewGuid();var day=new DateOnly(2026,11,25);
  var i=new SyntheticWireIntent(Guid.NewGuid(),marker,RehearsalKind.Invoice,Guid.NewGuid(),125m,day,"USD",customer);
  var r=new SyntheticWireIntent(Guid.NewGuid(),marker,RehearsalKind.Receipt,Guid.NewGuid(),50m,day,"USD",customer,bank,externalInvoice);
  var p=new SyntheticWireIntent(Guid.NewGuid(),marker,RehearsalKind.ExpensePayment,Guid.NewGuid(),20m,day,"USD",CashAccountKey:bank,VendorContact:"Synthetic vendor");
  JsonObject Form(SyntheticWireIntent x,Guid key)
  {
   var obj=JsonNode.Parse(SyntheticWireCompiler.Compile(x).Json)!.AsObject();
   var date=x.Kind==RehearsalKind.Invoice?"IssueDate":"Date";
   obj[date]=obj[date]!.GetValue<string>()+"T00:00:00";
   obj["Key"]=key.ToString("D");obj["id"]=key.ToString("D");
   obj["text"]="Synthetic display";obj["UniqueName"]="Synthetic display";
   obj["TaxCodeEnabled"]=true;obj["ProjectEnabled"]=true;return obj;
  }
  SyntheticWireReadbackResult Inspect(SyntheticWireIntent x,Guid key,JsonObject obj,Guid? scope=null,bool currency=true)
   =>SyntheticWireReadback.Inspect(x.Kind,key,scope??marker,currency,JsonSerializer.SerializeToElement(obj));
  foreach(var intent in new[]{i,r,p}){
   var key=Guid.NewGuid();var result=Inspect(intent,key,Form(intent,key));
   check(result.Outcome=="CandidateForReview"&&result.Candidate?.ExternalKey==key&&result.Candidate.Fingerprint==SyntheticWireCompiler.Compile(intent).Fingerprint,"readback reconstructs "+intent.Kind+" wire hash from observed fields");
  }
  var k=Guid.NewGuid();var form=Form(r,k);
  void Block(Action<JsonObject> mutate,string label){var obj=Form(r,k);mutate(obj);check(Inspect(r,k,obj).Candidate==null,label);}
  Block(o=>o["id"]=Guid.NewGuid().ToString("D"),"readback mismatched id blocked");
  Block(o=>o["TaxCodeEnabled"]=false,"readback unverified flag blocked");
  Block(o=>o["ProjectEnabled"]="true","readback nonboolean flag blocked");
  Block(o=>o["TaxCode"]=null,"readback unknown tax assignment blocked");
  Block(o=>o["Lines"]![0]!["Project"]=null,"readback unknown project assignment blocked");
  Block(o=>o["Date"]="2026-11-25T00:00:00Z","readback unverified timezone blocked");
  Block(o=>o["Date"]="2026-11-25T01:00:00","readback non-midnight time blocked");
  Block(o=>o["Reference"]="CRC-SB-RCT-0001","readback legacy reference not treated as operation identity");
  Block(o=>o["PaidBy"]=2,"readback unexpected payer blocked");
  Block(o=>o["Description"]="Changed description","readback changed emitted description blocked");
  Block(o=>o["Lines"]![0]!["Amount"]="50.00","readback string amount blocked");
  Block(o=>o["Lines"]![0]!["Amount"]=50.001m,"readback subcent amount blocked");
  Block(o=>o["Lines"]![0]!["AccountsReceivableCustomer"]=customer.ToString("D"),"readback unverified allocation field blocked");
  Block(o=>o.Remove("ReceivedIn"),"readback missing bank blocked");
  Block(o=>o["reference"]=o["Reference"]!.GetValue<string>(),"readback case duplicate blocked");
  check(Inspect(r,k,form,currency:false).Candidate==null,"readback currency evidence required");
  check(Inspect(r,k,form,scope:Guid.Empty).Candidate==null,"readback marker evidence required");
  var changed=Form(r,k);changed["ReceivedIn"]=Guid.NewGuid().ToString("D");
  var candidate=Inspect(r,k,changed).Candidate!;
  check(candidate.Fingerprint!=SyntheticWireCompiler.Compile(r).Fingerprint,"readback changed bank cannot echo target hash");
  var target=SyntheticWireCompiler.Compile(r).RecoveryTarget(k);
  var sweep=new SyntheticRecoverySweep(marker,true,true,new[]{candidate});
  check(SyntheticRecoveryReview.Evaluate(target,sweep,sweep).Finding==SyntheticRecoveryFinding.Conflict,"readback mismatch cannot pass recovery review");
  candidate=Inspect(r,k,form).Candidate!;
  sweep=new(marker,true,true,new[]{candidate});var review=SyntheticRecoveryReview.Evaluate(target,sweep,sweep);
  check(review.Finding==SyntheticRecoveryFinding.MatchedForReview&&!review.MayRetry&&!review.MayReleaseHold,"readback exact match still never authorizes replay or hold release");
  var raw=JsonSerializer.Serialize(form).Replace("\"PaidBy\":1","\"PaidBy\":1,\"PaidBy\":1",StringComparison.Ordinal);
  using var doc=JsonDocument.Parse(raw);
  check(SyntheticWireReadback.Inspect(r.Kind,k,marker,true,doc.RootElement).Candidate==null,"readback duplicate JSON property blocked");
 }
}
