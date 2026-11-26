using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using PracticeManager.Core;
using PracticeManager.Persistence;
internal static class SyntheticPostingPipelineTests
{
 // Test-only provider; no network, credentials, arbitrary endpoint or real API.
 // HttpClient uses this in-memory handler exclusively. It cannot send to Manager.
 private sealed class FakeProvider:HttpMessageHandler
 {
  private readonly Dictionary<Guid,(RehearsalKind Kind,JsonObject Form)> documents=new();
  public int Sends{get;private set;}
  public string Mode{get;set;}="Normal";
  public Guid? LastKey{get;private set;}
  protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
  {
   if(request.Method!=HttpMethod.Post||request.RequestUri?.Host!="synthetic.invalid")throw new InvalidOperationException("Fake provider boundary.");
   Sends++;
   var kind=request.RequestUri.AbsolutePath switch{
    "/invoice"=>RehearsalKind.Invoice,"/receipt"=>RehearsalKind.Receipt,"/expense"=>RehearsalKind.ExpensePayment,
    _=>throw new InvalidOperationException("Unknown test route.")};
   var form=JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct))!.AsObject();
   var key=Guid.NewGuid();LastKey=key;
   var date=kind==RehearsalKind.Invoice?"IssueDate":"Date";
   form[date]=form[date]!.GetValue<string>()+"T00:00:00";
   form["Key"]=key.ToString("D");form["id"]=key.ToString("D");form["text"]="Synthetic display";form["UniqueName"]="Synthetic display";
   form["TaxCodeEnabled"]=true;form["ProjectEnabled"]=true;
   // Deliberately commit before throwing in the timeout scenario.
   documents.Add(key,(kind,form));
   if(Mode=="TimeoutAfterCommit")throw new OperationCanceledException("Synthetic response lost after commit.");
   return new HttpResponseMessage(HttpStatusCode.Created){Content=new StringContent((Mode=="WrongIdentity"?Guid.NewGuid():key).ToString("D"),Encoding.UTF8,"text/plain")};
  }
  public SyntheticRecoverySweep Sweep(Guid marker)
  {
   var candidates=documents.Select(pair=>SyntheticWireReadback.Inspect(pair.Value.Kind,pair.Key,marker,true,
    JsonSerializer.SerializeToElement(pair.Value.Form)).Candidate).ToArray();
   return new(marker,candidates.All(x=>x!=null),true,candidates.OfType<SyntheticRecoveryCandidate>().ToArray());
  }
 }
 public static async Task Run(string directory,Action<bool,string> check)
 {
  var root=Path.Combine(directory,"pipeline-witness");Directory.CreateDirectory(root);
  var marker=Guid.NewGuid();var store=Guid.NewGuid();var witness=new SyntheticAttemptWitness(root,store,marker);
  check(witness.InitializeEmptyFixture().Outcome=="InitializedForReview","pipeline synthetic witness ready");
  var customer=Guid.NewGuid();var bank=Guid.NewGuid();var day=new DateOnly(2026,11,25);
  var invoice=new SyntheticWireIntent(Guid.NewGuid(),marker,RehearsalKind.Invoice,Guid.NewGuid(),125m,day,"USD",customer);
  using var provider=new FakeProvider();using var client=new HttpClient(provider,false);
  async Task<SyntheticDispatchAssessment> Exercise(SyntheticWireIntent intent,string route,string mode)
  {
   var payload=SyntheticWireCompiler.Compile(intent);var attempt=Guid.NewGuid();
   var recorded=witness.Record(intent,attempt);
   check(recorded.Outcome=="RecordedForReview"&&!recorded.MaySend,"pipeline witness recorded without real send authority");
   // Test code alone exercises the fake provider; this is NOT production dispatch.
   provider.Mode=mode;int? status=null;Guid? returned=null;bool complete=false;
   try{
    using var content=new StringContent(payload.Json,Encoding.UTF8,"application/json");
    using var response=await client.PostAsync("http://synthetic.invalid/"+route,content);
    status=(int)response.StatusCode;
    if(Guid.TryParse(await response.Content.ReadAsStringAsync(),out var key)){returned=key;complete=true;}
   }catch(OperationCanceledException){/* deliberately unresolved */}
   var first=provider.Sweep(marker);var second=provider.Sweep(marker);
   var review=SyntheticRecoveryReview.Evaluate(payload.RecoveryTarget(returned),first,second);
   var match=second.Candidates.SingleOrDefault(x=>x.DetailReference==payload.Reference)?.ExternalKey;
   var assessment=SyntheticDispatchReview.Evaluate(new(true,true,status,complete,returned,first.Complete&&second.Complete,review,match));
   check(!assessment.MayAutomaticallyRetry&&!assessment.MayMarkPosted,"pipeline never authorizes real retry or posting");
   check(witness.Record(intent,Guid.NewGuid()).Outcome=="AttemptConflictOrUnreadable","pipeline original operation cannot be recreated after simulated attempt");
   return assessment;
  }
  var first=await Exercise(invoice,"invoice","Normal");
  check(first.State==SyntheticDispatchState.ConfirmationForReview&&provider.Sends==1,"pipeline invoice exact readback is review only");
  var receipt=new SyntheticWireIntent(Guid.NewGuid(),marker,RehearsalKind.Receipt,Guid.NewGuid(),50m,day,"USD",customer,bank,provider.LastKey);
  var second=await Exercise(receipt,"receipt","Normal");
  check(second.State==SyntheticDispatchState.ConfirmationForReview&&provider.Sends==2,"pipeline receipt allocated to returned synthetic invoice");
  var payment=new SyntheticWireIntent(Guid.NewGuid(),marker,RehearsalKind.ExpensePayment,Guid.NewGuid(),20m,day,"USD",CashAccountKey:bank,VendorContact:"Synthetic vendor");
  var third=await Exercise(payment,"expense","TimeoutAfterCommit");
  check(third.State==SyntheticDispatchState.Uncertain&&provider.Sends==3&&provider.Sweep(marker).Candidates.Count==3,"pipeline timeout after remote commit retains one attempt and one synthetic document");
  var fourth=await Exercise(payment with{OperationKey=Guid.NewGuid()},"expense","WrongIdentity");
  check(fourth.State==SyntheticDispatchState.Uncertain&&provider.Sends==4,"pipeline conflicting 201 response identity remains uncertain");
 }
}
