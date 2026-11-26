using System.Text.Json;
using Microsoft.Data.Sqlite;
using PracticeManager.Core;
using PracticeManager.Persistence;
internal static class SyntheticWireDraftTests
{
 public static async Task Run(string path,string pw,Action<bool,string> check)
 {
  var s=new LocalPractice(path);s.Bootstrap("Synthetic Wire Draft Firm","admin",pw,"CL");
  var marker=Guid.NewGuid();var customer=Guid.NewGuid();var bank=Guid.NewGuid();var date=new DateOnly(2026,11,25);
  var receipt=new SyntheticWireIntent(Guid.NewGuid(),marker,RehearsalKind.Receipt,Guid.NewGuid(),50m,date,"USD",customer,bank,Guid.NewGuid());
  var hash=SyntheticWireCompiler.Compile(receipt).Fingerprint;
  void Denied(Action action,string label){try{action();}catch(UnauthorizedAccessException){check(true,label);return;}throw new Exception("FAIL: "+label);}
  Denied(()=>s.PrepareSyntheticWireDraft(receipt),"wire draft unauthorized preparation denied");
  check(s.Login("admin",pw)&&s.CanPrepareWireDraft()&&s.CanApproveWireDraft(),"wire draft grants seeded");
  check(s.PrepareSyntheticWireDraft(receipt).Outcome=="FixtureScopeRequired","wire draft requires original synthetic fixture scope");
  string Reply(string req)=>req.StartsWith("GET /api2/text-custom-fields?")?JsonSerializer.Serialize(new{business=new{name="CRC ACCOUNTING TEST"},skip=0,pageSize=2,totalRecords=1,textCustomFields=new[]{new{key=marker,name=BusinessMarker.TestName}}}):JsonSerializer.Serialize(new{business=new{name="CRC ACCOUNTING TEST"},skip=0,pageSize=20,totalRecords=1,customers=new[]{new{key=customer,name="CRC Test Customer 001",status="Paid"}}});
  await AccountingProbeTests.WithServer(200,"{}",async(url,_)=>{
   check((await s.SaveAccountingProfileAsync(url,"synthetic-key")).Success,"wire draft profile saved");
   check((await s.EnrollOrVerifyMarkerAsync(true)).Success,"wire draft marker enrolled");
   check((await s.SyncCustomerDirectoryAsync()).Outcome=="Synchronized","wire draft fixture synchronized");
  },6,responseFor:Reply);
  check(s.PrepareSyntheticWireDraft(receipt with{MarkerKey=Guid.NewGuid()}).Outcome=="MarkerMismatch","wire draft marker mismatch denied");
  var peer=new LocalPractice(path);check(peer.Login("admin",pw),"wire draft peer session");
  var prepared=await Task.WhenAll(Task.Run(()=>s.PrepareSyntheticWireDraft(receipt)),Task.Run(()=>peer.PrepareSyntheticWireDraft(receipt)));
  check(prepared.Count(x=>x.Outcome=="Prepared")==1&&prepared.Count(x=>x.Outcome=="AlreadyRecorded")==1,"concurrent wire draft creates one row");
  check(s.PrepareSyntheticWireDraft(receipt with{CashAccountKey=Guid.NewGuid()}).Outcome=="IntentConflict","wire draft binds receipt bank");
  var loaded=peer.ReadSyntheticWireDraft(receipt.OperationKey);
  check(loaded.State=="Draft"&&loaded.Payload?.Fingerprint==hash&&loaded.Payload.Intent.CashAccountKey==bank&&!loaded.MaySend,"wire draft reconstructs full immutable payload");
  var legacy=new SyntheticPostingIntent(receipt.OperationKey,RehearsalKind.Receipt,customer,receipt.AccountKey,receipt.AllocatedInvoiceKey,50m,date,"USD");
  check(s.ReserveSyntheticPosting(legacy).Outcome=="WireDraftConflict","legacy cannot reuse full wire operation");
  var other=legacy with{OperationKey=Guid.NewGuid()};
  check(s.ReserveSyntheticPosting(other).Outcome=="Reserved"&&s.PrepareSyntheticWireDraft(receipt with{OperationKey=other.OperationKey}).Outcome=="LegacyOperationConflict","wire draft cannot reuse legacy operation");
  check(s.ApproveSyntheticWireDraft(receipt.OperationKey,"wrong").Outcome=="IntentConflict","wire approval bound to reviewed hash");
  var approvals=await Task.WhenAll(Task.Run(()=>s.ApproveSyntheticWireDraft(receipt.OperationKey,hash)),Task.Run(()=>peer.ApproveSyntheticWireDraft(receipt.OperationKey,hash)));
  check(approvals.Count(x=>x.Outcome=="Approved")==1&&approvals.Count(x=>x.Outcome=="AlreadyApproved")==1,"concurrent full-wire approval recorded once");
  check(peer.ReadSyntheticWireDraft(receipt.OperationKey).ApprovalCurrentlyValid&&!peer.ReadSyntheticWireDraft(receipt.OperationKey).MaySend,"offline approval grants no send authority");
  void Sql(string sql){using var db=new SqliteConnection($"Data Source={path}");db.Open();using var q=db.CreateCommand();q.CommandText=sql;q.ExecuteNonQuery();}
  void BlockSql(string sql,string label){try{Sql(sql);}catch(SqliteException){check(true,label);return;}throw new Exception("FAIL: "+label);}
  BlockSql("UPDATE accounting_wire_drafts SET payload_hash=payload_hash","immutable full wire columns cannot be updated");
  BlockSql("DELETE FROM accounting_wire_drafts","wire history cannot be deleted");
  BlockSql("UPDATE accounting_wire_drafts SET state='Draft',approved_by=NULL,approved_utc=NULL","approval cannot be reset");
  Sql("UPDATE role_permissions SET allowed=0 WHERE permission_code='Accounting.WireDraft.Approve'");
  check(!s.ReadSyntheticWireDraft(receipt.OperationKey).ApprovalCurrentlyValid,"revoked approver grant visible");
  Denied(()=>s.CancelSyntheticWireDraft(receipt.OperationKey,hash),"approved wire cancellation requires approval permission");
  Denied(()=>s.ApproveSyntheticWireDraft(receipt.OperationKey,hash),"approval permission enforced");
  Sql("UPDATE role_permissions SET allowed=1 WHERE permission_code='Accounting.WireDraft.Approve'");
  s.RemoveAccountingProfile();
  check(!s.ReadSyntheticWireDraft(receipt.OperationKey).ApprovalCurrentlyValid,"missing profile invalidates current offline approval readiness");
  check(s.ApproveSyntheticWireDraft(receipt.OperationKey,hash).Outcome=="FixtureScopeRequired","approval cannot proceed without configured profile");
  await AccountingProbeTests.WithServer(200,"{}",async(url,_)=>{
   check((await s.SaveAccountingProfileAsync(url,"synthetic-key")).Success,"wire draft profile restored for same marker");
   check((await s.SyncCustomerDirectoryAsync()).Outcome=="Synchronized","wire draft directory revalidated");
  },6,responseFor:Reply);
  check(s.CancelSyntheticWireDraft(receipt.OperationKey,hash).Outcome=="Cancelled","approved but unsent synthetic draft cancelled");
  check(s.PrepareSyntheticWireDraft(receipt).State=="Cancelled"&&s.ApproveSyntheticWireDraft(receipt.OperationKey,hash).Outcome=="ApprovalNotAllowed","cancelled operation permanently consumed");
  var invoice=new SyntheticWireIntent(Guid.NewGuid(),marker,RehearsalKind.Invoice,Guid.NewGuid(),125m,date,"USD",customer);
  var expense=new SyntheticWireIntent(Guid.NewGuid(),marker,RehearsalKind.ExpensePayment,Guid.NewGuid(),20m,date,"USD",CashAccountKey:bank,VendorContact:"Synthetic vendor");
  check(s.PrepareSyntheticWireDraft(invoice).Outcome=="Prepared"&&s.PrepareSyntheticWireDraft(expense).Outcome=="Prepared","all three full wire kinds supported");
  var reopened=new LocalPractice(path);check(reopened.Login("admin",pw)&&reopened.ReadSyntheticWireDraft(expense.OperationKey).Payload?.Fingerprint==SyntheticWireCompiler.Compile(expense).Fingerprint,"full wire payload retained after reopen");
  check(reopened.DeclareAccountingLifecycleHold(LifecycleHoldReason.Restore).Outcome=="LifecycleHold","wire draft test hold declared");
  check(s.ApproveSyntheticWireDraft(invoice.OperationKey,SyntheticWireCompiler.Compile(invoice).Fingerprint).Outcome=="LifecycleHold"&&s.PrepareSyntheticWireDraft(expense with{OperationKey=Guid.NewGuid()}).Outcome=="LifecycleHold","lifecycle hold blocks wire draft changes");
  check(s.ReadSyntheticWireDraft(receipt.OperationKey).State=="Cancelled","hold retains inspection");
  Sql("UPDATE role_permissions SET allowed=0 WHERE permission_code='Accounting.WireDraft.Prepare'");
  Denied(()=>s.ReadSyntheticWireDraft(receipt.OperationKey),"wire draft inspection requires dedicated grant");
 }
}
