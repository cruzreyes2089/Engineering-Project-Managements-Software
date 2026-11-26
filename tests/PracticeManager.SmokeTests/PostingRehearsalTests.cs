using System.Text.Json;
using Microsoft.Data.Sqlite;
using PracticeManager.Core;
using PracticeManager.Persistence;
internal static class PostingRehearsalTests
{
 public static async Task Run(string path,string pw,Action<bool,string> check)
 {
  var s=new LocalPractice(path);s.Bootstrap("Synthetic Rehearsal Firm","admin",pw,"CL");check(s.Login("admin",pw)&&s.CanRehearsePosting(),"separate rehearsal permission seeded");
  var marker=Guid.NewGuid();var customer=Guid.NewGuid();var account=Guid.NewGuid();
  var i=new SyntheticPostingIntent(Guid.NewGuid(),RehearsalKind.Invoice,customer,account,null,100m,new DateOnly(2026,11,25),"USD");
  check(i.Fingerprint()==(i with{Amount=100.00m}).Fingerprint(),"canonical decimal fingerprint");
  check(i.Fingerprint()!=(i with{Amount=101m}).Fingerprint(),"changed payload fingerprint");
  try{(i with{Amount=0.001m}).Fingerprint();throw new Exception("FAIL: subcent intent");}catch(ArgumentException){check(true,"subcent amount rejected");}
  try{(i with{Kind=RehearsalKind.Receipt}).Fingerprint();throw new Exception("FAIL: missing allocation");}catch(ArgumentException){check(true,"receipt requires allocation");}
  var payment=new SyntheticPostingIntent(Guid.NewGuid(),RehearsalKind.ExpensePayment,Guid.Empty,Guid.NewGuid(),null,20m,new DateOnly(2026,11,25),"USD",Guid.NewGuid(),"Synthetic Office Supplies Vendor");
  check(payment.Fingerprint()==(payment with{Amount=20.00m}).Fingerprint(),"expense canonical fingerprint");
  check(payment.Fingerprint()!=(payment with{VendorContact="Different synthetic vendor"}).Fingerprint(),"expense vendor bound to fingerprint");
  check(payment.Fingerprint()!=(payment with{CashAccountKey=Guid.NewGuid()}).Fingerprint(),"expense bank bound to fingerprint");
  void Invalid(SyntheticPostingIntent value,string name){try{value.Fingerprint();}catch(ArgumentException){check(true,name);return;}throw new Exception("FAIL: "+name);}
  Invalid(payment with{CashAccountKey=null},"expense requires bank");
  Invalid(payment with{CashAccountKey=payment.AccountKey},"expense bank and ledger account must differ");
  Invalid(payment with{VendorContact=" "},"expense requires vendor contact");
  Invalid(payment with{CustomerKey=customer},"expense must not impersonate customer");
  Invalid(payment with{AllocatedInvoiceKey=i.OperationKey},"direct expense cannot allocate sales invoice");
  Invalid(i with{VendorContact="Synthetic vendor"},"legacy intent cannot silently ignore expense fields");
  check(s.ReserveSyntheticPosting(i).Outcome=="FixtureScopeRequired","reservation requires configured scope");
  check(s.ReadPostingRehearsalStatus()==new PostingRehearsalStatus(null,0,0,0,0,0),"initial offline safety status");
  string Reply(string req)=>req.StartsWith("GET /api2/text-custom-fields?")?JsonSerializer.Serialize(new{business=new{name="CRC ACCOUNTING TEST"},skip=0,pageSize=2,totalRecords=1,textCustomFields=new[]{new{key=marker,name=BusinessMarker.TestName}}}):JsonSerializer.Serialize(new{business=new{name="CRC ACCOUNTING TEST"},skip=0,pageSize=20,totalRecords=1,customers=new[]{new{key=customer,name="CRC Test Customer 001",status="Paid"}}});
  var key="synthetic-key";
  await AccountingProbeTests.WithServer(200,"{}",async(url,_)=>{
   check((await s.SaveAccountingProfileAsync(url,key)).Success,"rehearsal test profile saved");
   check((await s.EnrollOrVerifyMarkerAsync(true)).Success,"rehearsal test marker enrolled");
   check((await s.SyncCustomerDirectoryAsync()).Outcome=="Synchronized","rehearsal test directory synchronized");
  },6,responseFor:Reply);
  var peer=new LocalPractice(path);check(peer.Login("admin",pw),"second session on same local database");
  var reserves=await Task.WhenAll(Task.Run(()=>s.ReserveSyntheticPosting(i)),Task.Run(()=>peer.ReserveSyntheticPosting(i)));
  check(reserves.Count(r=>r.Outcome=="Reserved")==1&&reserves.Count(r=>r.Outcome=="AlreadyRecorded")==1,"concurrent reservations create exactly one durable intent");
  check(s.ReserveSyntheticPosting(i with{Amount=101m}).Outcome=="IntentConflict","changed payload cannot reuse operation");
  var begins=await Task.WhenAll(Task.Run(()=>s.BeginSyntheticPosting(i)),Task.Run(()=>peer.BeginSyntheticPosting(i)));
  check(begins.Count(r=>r.Outcome=="Started")==1&&begins.Count(r=>r.AttemptKey.HasValue)==1,"only one concurrent begin returns attempt key");
  var attempt=begins.Single(r=>r.AttemptKey.HasValue).AttemptKey!.Value;
  check(peer.ReadSyntheticPostingState(i.OperationKey).State=="InFlight"&&peer.ReadSyntheticPostingState(i.OperationKey).AttemptKey==null,"read does not reissue attempt");
  check(peer.BeginSyntheticPosting(i).Outcome=="DispatchNotAllowed","inflight retry blocked");
  check(peer.CancelSyntheticPosting(i).Outcome=="CancellationNotAllowed","cannot cancel inflight as unsent");
  check(peer.FinishSyntheticPosting(i,Guid.NewGuid(),SimulatedOutcome.Confirmed,Guid.NewGuid()).Outcome=="DispatchNotAllowed","wrong attempt rejected");
  var external=Guid.NewGuid();
  check(s.FinishSyntheticPosting(i,attempt,SimulatedOutcome.Confirmed,external).Outcome=="SimulatedConfirmed","simulated confirmation recorded");
  check(s.BeginSyntheticPosting(i).Outcome=="DispatchNotAllowed"&&s.ReserveSyntheticPosting(i).State=="SimulatedConfirmed","confirmed operation cannot dispatch again");
  check(s.ReserveSyntheticPosting(payment with{OperationKey=i.OperationKey}).Outcome=="IntentConflict","operation key cannot cross transaction kinds");
  check(s.ReserveSyntheticPosting(payment).Outcome=="Reserved","expense rehearsal reserved");
  check(peer.ReserveSyntheticPosting(payment with{VendorContact="Changed vendor"}).Outcome=="IntentConflict","expense vendor change cannot reuse key");
  var expenseBegins=await Task.WhenAll(Task.Run(()=>s.BeginSyntheticPosting(payment)),Task.Run(()=>peer.BeginSyntheticPosting(payment)));
  check(expenseBegins.Count(r=>r.Outcome=="Started")==1&&expenseBegins.Count(r=>r.AttemptKey.HasValue)==1,"only one expense rehearsal begin");
  var expenseAttempt=expenseBegins.Single(r=>r.AttemptKey.HasValue).AttemptKey!.Value;
  check(s.FinishSyntheticPosting(payment,expenseAttempt,SimulatedOutcome.Confirmed,external).Outcome=="SimulatedExternalConflict","expense cannot reuse simulated invoice ID");
  check(s.FinishSyntheticPosting(payment,expenseAttempt,SimulatedOutcome.Confirmed,Guid.NewGuid()).Outcome=="SimulatedConfirmed","expense rehearsal confirmed offline");
  check(peer.BeginSyntheticPosting(payment).Outcome=="DispatchNotAllowed","expense repeat blocked");
  var c=i with{OperationKey=Guid.NewGuid()};
  check(s.ReserveSyntheticPosting(c).Outcome=="Reserved"&&s.CancelSyntheticPosting(c).Outcome=="Cancelled","only reserved intent may cancel");
  check(s.BeginSyntheticPosting(c).Outcome=="DispatchNotAllowed","cancelled key permanently consumed");
  var second=payment with{OperationKey=Guid.NewGuid()};
  check(s.ReserveSyntheticPosting(second).Outcome=="Reserved","second operation reserved");
  var secondAttempt=s.BeginSyntheticPosting(second).AttemptKey!.Value;
  check(s.FinishSyntheticPosting(second,secondAttempt,SimulatedOutcome.Confirmed,external).Outcome=="SimulatedExternalConflict","same simulated external ID cannot map twice");
  check(s.FinishSyntheticPosting(second,secondAttempt,SimulatedOutcome.Uncertain).Outcome=="Uncertain","ambiguous outcome durably quarantined");
  var third=i with{OperationKey=Guid.NewGuid()};
  check(s.ReserveSyntheticPosting(third).Outcome=="Reserved"&&s.BeginSyntheticPosting(third).Outcome=="UnresolvedRehearsal","uncertainty blocks later dispatch");
  check(s.ReadPostingRehearsalStatus()==new PostingRehearsalStatus(null,1,0,1,2,1),"status accurately counts simulated intent states");
  s.RemoveAccountingProfile();
  check(s.ReadSyntheticPostingState(second.OperationKey).State=="Uncertain","profile removal retains intent history");
  await AccountingProbeTests.WithServer(200,"{}",async(url,_)=>{
   check((await s.SaveAccountingProfileAsync(url,key)).Success,"same-marker replacement profile saved");
   check((await s.SyncCustomerDirectoryAsync()).Outcome=="Synchronized","replacement scope synchronized");
  },6,responseFor:Reply);
  var reopened=new LocalPractice(path);check(reopened.Login("admin",pw),"journal reopened");
  check(reopened.ReserveSyntheticPosting(i).Outcome=="AlreadyRecorded"&&reopened.BeginSyntheticPosting(second).Outcome=="DispatchNotAllowed","restart and profile rotation cannot replay");
  check(reopened.ReserveSyntheticPosting(payment).Outcome=="AlreadyRecorded"&&reopened.BeginSyntheticPosting(payment).Outcome=="DispatchNotAllowed","expense replay blocked after restart");
  check(reopened.BeginSyntheticPosting(third).Outcome=="UnresolvedRehearsal","unresolved state persists across restart");
  check(reopened.DeclareAccountingLifecycleHold(LifecycleHoldReason.Restore).Outcome=="LifecycleHold","restore declaration creates sticky hold");
  check(s.IsAccountingLifecycleHeld()&&s.ReserveSyntheticPosting(i with{OperationKey=Guid.NewGuid()}).Outcome=="LifecycleHold","hold blocks other sessions");
  var afterHold=new LocalPractice(path);check(afterHold.Login("admin",pw)&&afterHold.IsAccountingLifecycleHeld(),"hold survives restart");
  check(afterHold.ReadSyntheticPostingState(i.OperationKey).State=="SimulatedConfirmed","hold preserves history for investigation");
  check(afterHold.ReadPostingRehearsalStatus()==new PostingRehearsalStatus("Restore",1,0,1,2,1),"recovery screen retains hold reason and counts");
  using(var db=new SqliteConnection($"Data Source={path}")){db.Open();using var q=db.CreateCommand();q.CommandText="SELECT COUNT(*) FROM accounting_posting_rehearsals";check(Convert.ToInt64(q.ExecuteScalar())==5,"no duplicate durable intents");q.CommandText="UPDATE role_permissions SET allowed=0 WHERE permission_code='Accounting.Posting.Rehearse'";q.ExecuteNonQuery();}
  check(!afterHold.CanRehearsePosting(),"rehearsal grant revoked");
  try{afterHold.ReadSyntheticPostingState(i.OperationKey);throw new Exception("FAIL: unauthorized rehearsal");}catch(UnauthorizedAccessException){check(true,"rehearsal history requires authorization");}
  var again=new LocalPractice(path);check(again.Login("admin",pw)&&!again.CanRehearsePosting(),"reopen preserves revoked grant");
 }
}
