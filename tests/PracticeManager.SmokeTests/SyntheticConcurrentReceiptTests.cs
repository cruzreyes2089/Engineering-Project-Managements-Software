using PracticeManager.Core;

// A disposable in-memory reference provider. Its lock and operation-key registry
// deliberately model the desired atomic contract, not an observed Manager guarantee.
// No HTTP, credentials, production database or external side effects.
internal static class SyntheticConcurrentReceiptTests
{
 sealed record CommitResult(string Outcome,Guid? Key=null);
 sealed class Provider
 {
  readonly object gate=new();
  SyntheticSettlementSnapshot ledger;
  readonly Dictionary<Guid,(string Hash,Guid Key)> operations=new();
  public Provider(SyntheticSettlementSnapshot initial){ledger=initial;}
  public SyntheticSettlementSnapshot Snapshot()
  {
   lock(gate)return ledger with{Invoices=ledger.Invoices.ToArray(),Receipts=ledger.Receipts.ToArray(),Payments=ledger.Payments.ToArray()};
  }
  public int CommitCount{get{lock(gate)return operations.Count;}}
  public CommitResult Submit(SyntheticWireIntent intent,string review,bool loseAcknowledgement=false)
  {
   lock(gate)
   {
    SyntheticWirePayload wire;
    try{wire=SyntheticWireCompiler.Compile(intent);}
    catch(ArgumentException){return new("InvalidIntent");}
    if(intent.Kind!=RehearsalKind.Receipt||intent.MarkerKey!=ledger.MarkerKey)return new("ScopeRejected");
    if(operations.TryGetValue(intent.OperationKey,out var prior))
     return new(prior.Hash==wire.Fingerprint?"DuplicateOperation":"OperationConflict");
    var reviewed=SyntheticReceiptCapacityReview.Evaluate(intent,ledger,ledger,review);
    if(reviewed.Outcome!="CapacityForReview")return new(reviewed.Outcome);
    var key=Guid.NewGuid();
    var invoice=ledger.Invoices.Single(x=>x.Key==intent.AllocatedInvoiceKey);
    var updated=ledger with{
     Invoices=ledger.Invoices.Select(x=>x.Key==invoice.Key?x with{ReportedBalance=x.ReportedBalance-intent.Amount}:x).ToArray(),
     Receipts=ledger.Receipts.Append(new TestLedgerReceipt(key,invoice.Customer,invoice.Key,ledger.Accounts.Cash,intent.Amount)).ToArray(),
     Balances=ledger.Balances with{Receivables=ledger.Balances.Receivables-intent.Amount,Cash=ledger.Balances.Cash+intent.Amount}};
    if(!TestLedgerReconciliation.Evaluate(updated.Accounts,updated.Invoices,updated.Receipts,updated.Payments,updated.Balances).Matches)
     return new("ReconciliationRejected");
    // Both effects occur under one simulated atomic critical section.
    operations.Add(intent.OperationKey,(wire.Fingerprint,key));ledger=updated;
    if(loseAcknowledgement)throw new IOException("Simulated lost acknowledgement.");
    return new("CommittedSimulation",key);
   }
  }
  public SyntheticRecoverySweep Sweep()
  {
   lock(gate)return new(ledger.MarkerKey,true,true,operations.Select(x=>new SyntheticRecoveryCandidate(
    x.Value.Key,RehearsalKind.Receipt,SyntheticRecoveryReview.ReferenceFor(x.Key),x.Value.Hash)).ToArray());
  }
 }
 public static async Task Run(Action<bool,string> check)
 {
  var a=new TestLedgerAccounts(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid());
  var marker=Guid.NewGuid();var customer=Guid.NewGuid();
  var invoice=new TestLedgerInvoice(Guid.NewGuid(),customer,a.ServiceIncome,125m,75m);
  var initial=new SyntheticSettlementSnapshot(marker,true,true,true,a,new[]{invoice},
   new[]{new TestLedgerReceipt(Guid.NewGuid(),customer,invoice.Key,a.Cash,50m)},
   new[]{new TestLedgerPayment(Guid.NewGuid(),a.Cash,a.OfficeExpense,20m)},
   new TestLedgerBalances(75m,30m,125m,20m,105m,0m,105m,105m));
  SyntheticWireIntent Intent(decimal amount)=>new(Guid.NewGuid(),marker,RehearsalKind.Receipt,a.Receivables,amount,
   new DateOnly(2026,11,25),"USD",customer,a.Cash,invoice.Key);
  string Review(SyntheticWireIntent i,SyntheticSettlementSnapshot s)=>SyntheticReceiptCapacityReview.Evaluate(i,s,s).ReviewFingerprint
   ??throw new Exception("Synthetic review unavailable.");
  async Task<CommitResult[]> Race(Provider p,SyntheticWireIntent i,string ih,SyntheticWireIntent j,string jh)
  {
   using var ready=new CountdownEvent(2);using var start=new ManualResetEventSlim();
   Task<CommitResult> Worker(SyntheticWireIntent x,string h)=>Task.Run(()=>{
    ready.Signal();if(!start.Wait(TimeSpan.FromSeconds(15)))throw new TimeoutException();
    return p.Submit(x,h);
   });
   var one=Worker(i,ih);var two=Worker(j,jh);
   if(!ready.Wait(TimeSpan.FromSeconds(15))){start.Set();await Task.WhenAll(one,two);throw new TimeoutException();}
   start.Set();return await Task.WhenAll(one,two);
  }
  var provider=new Provider(initial);
  var first=Intent(50m);var second=Intent(50m);
  var fh=Review(first,initial);var sh=Review(second,initial);
  var results=await Race(provider,first,fh,second,sh);
  check(results.Count(x=>x.Outcome=="CommittedSimulation")==1&&results.Count(x=>x.Outcome=="InsufficientInvoiceBalance")==1,
   "competing simulated receipts cannot consume more than outstanding balance");
  var after=provider.Snapshot();
  check(provider.CommitCount==1&&after.Invoices.Single().ReportedBalance==25m&&after.Balances.Cash==80m&&after.Balances.Income==125m,
   "simulated race conserves cash receivable and income without duplicate revenue");
  var winner=results[0].Outcome=="CommittedSimulation"?first:second;
  check(provider.Submit(winner,Review(winner,initial)).Outcome=="DuplicateOperation","same operation replay rejected before capacity evaluation");
  check(provider.Submit(winner with{Amount=1m},fh).Outcome=="OperationConflict","changed payload cannot reuse committed operation identity");
  check(provider.CommitCount==1,"duplicate and conflicting requests have no simulated effect");
  var small1=Intent(10m);var small2=Intent(10m);
  var smallResults=await Race(provider,small1,Review(small1,after),small2,Review(small2,after));
  check(smallResults.Count(x=>x.Outcome=="CommittedSimulation")==1&&smallResults.Count(x=>x.Outcome=="StaleReview")==1,
   "two affordable competing receipts still require fresh review of changed ledger");
  var loser=smallResults[0].Outcome=="StaleReview"?small1:small2;
  var latest=provider.Snapshot();
  check(provider.Submit(loser,Review(loser,latest)).Outcome=="CommittedSimulation","uncommitted stale candidate can be freshly reviewed in reference provider");
  check(provider.Snapshot().Invoices.Single().ReportedBalance==5m&&provider.CommitCount==3,"fresh simulated allocation uses remaining capacity");
  var final=Intent(5m);var finalHash=Review(final,provider.Snapshot());
  try{provider.Submit(final,finalHash,true);throw new Exception("FAIL: expected lost acknowledgement");}
  catch(IOException){check(true,"simulated provider commits before losing acknowledgement");}
  var wire=SyntheticWireCompiler.Compile(final);
  var sweep1=provider.Sweep();var sweep2=provider.Sweep();
  var recovery=SyntheticRecoveryReview.Evaluate(wire.RecoveryTarget(),sweep1,sweep2);
  check(recovery.Finding==SyntheticRecoveryFinding.MatchedForReview&&!recovery.MayRetry&&!recovery.MayReleaseHold,
   "lost receipt acknowledgement is correlation evidence only");
  var state=SyntheticDispatchReview.Evaluate(new(true,true,null,false,null,true,recovery));
  check(state.State==SyntheticDispatchState.Uncertain&&!state.MayAutomaticallyRetry&&!state.MayMarkPosted,
   "lost acknowledgement remains uncertain even with matching receipt");
  check(provider.Submit(final,finalHash).Outcome=="DuplicateOperation"&&provider.CommitCount==4,
   "reference provider duplicate probe does not create another receipt");
  var settled=provider.Snapshot();
  check(settled.Invoices.Single().ReportedBalance==0m&&settled.Balances.Receivables==0m&&settled.Balances.Cash==105m&&
   settled.Balances.Income==125m&&settled.Balances.Profit==105m&&
   TestLedgerReconciliation.Evaluate(a,settled.Invoices,settled.Receipts,settled.Payments,settled.Balances).Matches,
   "complete simulated settlement reconciles without excess collection");
  // Separate fixture: exact same operation submitted concurrently.
  var duplicateProvider=new Provider(initial);var duplicate=Intent(20m);var hash=Review(duplicate,initial);
  var duplicateResults=await Race(duplicateProvider,duplicate,hash,duplicate,hash);
  check(duplicateResults.Count(x=>x.Outcome=="CommittedSimulation")==1&&duplicateResults.Count(x=>x.Outcome=="DuplicateOperation")==1&&
   duplicateProvider.CommitCount==1&&duplicateProvider.Snapshot().Invoices.Single().ReportedBalance==55m,
   "parallel identical synthetic operation has exactly one effect");
 }
}
