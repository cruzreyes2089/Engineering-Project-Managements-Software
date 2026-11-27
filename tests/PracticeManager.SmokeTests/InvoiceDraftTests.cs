using System.Text.Json;
using Microsoft.Data.Sqlite;
using PracticeManager.Core;
using PracticeManager.Persistence;
internal static class InvoiceDraftTests
{
 public static async Task Run(string path,string pw,Action<bool,string> check)
 {
  var s=new LocalPractice(path);s.Bootstrap("Synthetic Billing Firm","admin",pw,"CL");check(s.Login("admin",pw),"invoice admin authenticated");
  void Invalid(Action a,string label){try{a();}catch(ArgumentException){check(true,label);return;}throw new Exception("FAIL: "+label);}
  void Denied(Action a,string label){try{a();}catch(UnauthorizedAccessException){check(true,label);return;}throw new Exception("FAIL: "+label);}
  var role=s.ReadStaffRoles().Single(r=>r.Name=="Engineer");s.SetStaffGrant(role.Id,"Work.Own",true,false);var account=s.CreateStaffAccount("worker",role.Id,"Synthetic-worker-temp-0001");var employee=s.CreateEmployee(new("Synthetic","Worker","","",true),account);
  var worker=new LocalPractice(path);check(worker.ChangeRequiredPassword("worker","Synthetic-worker-temp-0001","Synthetic-worker-password-0001")&&worker.Login("worker","Synthetic-worker-password-0001"),"invoice employee authenticated");
  var client=s.SaveClient(new Client(0,"","Business / Company","Synthetic Client","","","","","","","","","","","Active"));var prop=s.CreateProposal(new(client,"Synthetic","Site","Scope",500m));var hash=ConflictReviewTests.Issue(s,prop,1);var project=s.RecordProposalDecision(prop,hash,true,"Synthetic").ProjectId!.Value;
  var a=s.RecordSignedAgreement(project,hash,"signed.pdf","%PDF-1.7\nSynthetic\n%%EOF"u8.ToArray(),"Reviewed",true);ConflictReviewTests.Clear(s,project);s.AuthorizeProjectStart(project,hash,a.Id,a.PdfHash,true,"Synthetic");
  var day=DateOnly.FromDateTime(DateTime.Today);var phase=s.CreateProjectPhase(project,"Design",day.AddDays(-1),day.AddDays(1));var task=s.CreateProjectTask(project,new(phase,employee,"Design",day.AddDays(-1),day.AddDays(1),120));worker.AdvanceMyTask(task,1,"InProgress");
  var time=worker.SubmitWork(new(Guid.NewGuid(),task,day,"Time",61,null,null,"Design time"));var expense=worker.SubmitWork(new(Guid.NewGuid(),task,day,"Expense",null,12.34m,"Supplies","Receipt reference"));
  Invalid(()=>s.ReadInvoiceCandidates(project,day),"no implicit billing policy");
  s.RecordBillingPolicy(new(project,"Hybrid",100m,500m,true,"Synthetic agreement terms"));
  check(s.ReadInvoiceCandidates(project,day).Count==0,"unreviewed work excluded");
  s.ReviewWork(time,true,"Reviewed");s.ClassifyWork(time,true,"Billable");s.ReviewWork(expense,true,"Reviewed");s.ClassifyWork(expense,true,"Billable");
  check(s.ReadInvoiceCandidates(project,day).Single(x=>x.SourceId==time).Status=="Positive billing rate required","missing rate is explicit");
  check(s.ReadProjectFinancialReport(project,day,day).EstimatedLaborCost==null&&s.ReadProjectFinancialReport(project,day,day).MissingCostRates==1,"missing cost rate does not become zero cost");
  s.RecordEmployeeRate(new(employee,day.AddDays(-1),180m,35m));
  Invalid(()=>s.RecordEmployeeRate(new(employee,day.AddDays(-2),190m,36m)),"backdated rate insertion rejected");
  Denied(()=>worker.ReadEmployeeRates(employee),"employee cannot see rate history");
  Denied(()=>worker.ReadInvoiceDrafts(project),"employee cannot see invoice register");
  check(s.ReadInvoiceCandidates(project,day).Single(x=>x.SourceId==time).Amount==183m,"time pricing exact to cents");
  var input=new InvoiceDraftInput(Guid.NewGuid(),project,day,day.AddDays(30),new[]{new FixedFeeInput("Milestone",100m)},new[]{time,expense});var draft=s.CreateInvoiceDraft(input);var detail=s.ReadInvoiceDraft(project,draft);
  check(detail.Summary.Total==295.34m&&detail.Lines.Count==3,"mixed billing draft total and line history");
  check(s.CreateInvoiceDraft(input)==draft,"draft operation idempotent");
  Invalid(()=>s.CreateInvoiceDraft(input with{DueDate=day.AddDays(31)}),"draft operation conflict");
  Invalid(()=>s.CreateInvoiceDraft(input with{OperationKey=Guid.NewGuid()}),"allocated sources unavailable");
  check(s.ReadInvoiceCandidates(project,day).Count==0,"allocated sources hidden from candidate queue");
  Invalid(()=>s.DecideInvoiceDraft(project,draft,"wrong",true,"Reviewed"),"wrong payload rejected");
  check(s.DecideInvoiceDraft(project,draft,detail.Summary.PayloadHash,false,"Recreate")=="Cancelled","unsent draft cancellation retained");
  check(s.ReadInvoiceCandidates(project,day).Count==2,"cancelled unsent draft releases work allocation");
  var next=s.CreateInvoiceDraft(input with{OperationKey=Guid.NewGuid()});var nextDetail=s.ReadInvoiceDraft(project,next);
  Denied(()=>s.DecideInvoiceDraft(project,next,nextDetail.Summary.PayloadHash,true,"Approved internal draft",true),"invoice self approval disabled by default");
  s.SetOwnerApprovalPolicy(true,null,"Synthetic single-owner authorization");
  Denied(()=>s.DecideInvoiceDraft(project,next,nextDetail.Summary.PayloadHash,true,"Approved internal draft"),"invoice self approval needs explicit confirmation");
  check(s.DecideInvoiceDraft(project,next,nextDetail.Summary.PayloadHash,true,"Approved internal draft",true)=="Approved","draft approved under explicit owner exception without posting");
  check(s.DecideInvoiceDraft(project,next,nextDetail.Summary.PayloadHash,false,"Cancel")=="AlreadyApproved","approved draft cannot silently cancel");
  check(s.ReadOwnerDecisionEvidence().Single().Kind=="Invoice approval"&&s.ReadOwnerDecisionEvidence().Single().PolicyEventId.HasValue,"invoice exception evidence visible");
  Invalid(()=>s.CreateInvoiceDraft(new(Guid.NewGuid(),project,day,day.AddDays(30),new[]{new FixedFeeInput("Over cap",1m)},Array.Empty<long>())),"fixed component contractual cap enforced");
  check(s.ReadInvoiceDrafts(project).Count==2,"invoice draft history retained");
  s.RecordEmployeeRate(new(employee,day.AddDays(1),200m,40m));
  check(s.ReadInvoiceDraft(project,next).Lines.Single(x=>x.Kind=="Time").Amount==183m,"new rate does not alter approved history");
  var ops=s.ReadProjectOperations(project,day,day);check(ops.ApprovedMinutes==61&&ops.SubmittedMinutes==61&&ops.ExpenseSubmissions==1&&ops.StartedTasks==1,"operational report matches approved facts");
  var fin=s.ReadProjectFinancialReport(project,day,day);
  check(fin.ApprovedMinutes==61&&fin.BillableMinutes==61&&fin.EstimatedLaborCost==35.58m&&fin.ApprovedExpenseCost==12.34m&&fin.TotalOperationalCost==47.92m,"operational cost report uses effective rate and cents");
  check(fin.ApprovedInternalDraftValue==295.34m&&fin.CancelledDraftValue==295.34m&&fin.DraftValue==0,"approved and cancelled drafts reported separately");
  Denied(()=>worker.ReadProjectFinancialReport(project,day,day),"employee cannot read cost report");
  Denied(()=>worker.ExportInvoiceDraftCsv(project,next,nextDetail.Summary.PayloadHash),"employee cannot export billing draft");
  Invalid(()=>s.ExportInvoiceDraftCsv(project,draft,detail.Summary.PayloadHash),"cancelled draft cannot export");
  Denied(()=>worker.PrepareInvoiceReviewDocx(project,next,nextDetail.Summary.PayloadHash),"employee cannot generate invoice review document");
  Invalid(()=>s.PrepareInvoiceReviewDocx(project,draft,detail.Summary.PayloadHash),"cancelled invoice draft cannot generate document");
  var doc=s.PrepareInvoiceReviewDocx(project,next,nextDetail.Summary.PayloadHash);var docText=ReviewDocxTests.Text(doc.Bytes);
  check(doc.Id==s.PrepareInvoiceReviewDocx(project,next,nextDetail.Summary.PayloadHash).Id&&docText.Contains("295.34 USD")&&docText.Contains("NOT AN ISSUED INVOICE")&&!docText.Contains("35.58"),"approved invoice review registered, exact totals, no cost data");
  check(s.ReadInvoiceReviewDocuments(project,next).Single().Id==doc.Id&&s.RetrieveInvoiceReviewDocument(project,next,doc.Id,doc.Sha256).Bytes.SequenceEqual(doc.Bytes),"approved draft document register and byte verification");
  Denied(()=>worker.ReadInvoiceReviewDocuments(project,next),"ordinary employee cannot list invoice documents");
  Invalid(()=>s.RetrieveInvoiceReviewDocument(project,next,doc.Id,"wrong"),"wrong invoice document hash denied");
  var csv=s.ExportInvoiceDraftCsv(project,next,nextDetail.Summary.PayloadHash);
  check(csv.Contains("NOT AN ISSUED INVOICE")&&csv.Contains("295.34")&&!csv.Contains("35.58"),"export labeled internal and omits cost rate");
  check(SafeCsv.Text(" =SUM(1,2)").StartsWith("\"'")&&SafeCsv.Text("quote\"").Contains("\"\""),"CSV protects formula text and quotes");
  var marker=Guid.NewGuid();var externalCustomer=Guid.NewGuid();var externalInvoice=Guid.NewGuid();var business=new{name="CRC ACCOUNTING TEST"};var reference="SYNTHETIC-INVOICE-REVIEW";
  string Reply(string request){
   if(request.StartsWith("GET /api2/text-custom-fields?"))return JsonSerializer.Serialize(new{business,skip=0,pageSize=2,totalRecords=1,textCustomFields=new[]{new{key=marker,name=BusinessMarker.TestName}}});
   if(request.StartsWith("GET /api2/customers?")||request.StartsWith("GET /api2/customers "))return JsonSerializer.Serialize(new{business,skip=0,pageSize=20,totalRecords=1,customers=new[]{new{key=externalCustomer,name="Synthetic Customer",status="Paid"}}});
   if(request.StartsWith("GET /api2/sales-invoices?"))return JsonSerializer.Serialize(new{business,skip=0,pageSize=20,totalRecords=1,salesInvoices=new[]{new{key=externalInvoice,reference,invoiceAmount=new{currency=(string?)null,value=295.34m},balanceDue=new{currency=(string?)null,value=195.34m}}}});
   if(request.StartsWith("GET /api2/sales-invoice-form/"))return JsonSerializer.Serialize(new{Key=externalInvoice,id=externalInvoice,Reference=reference,IssueDate=day.ToString("yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture)+"T00:00:00",Customer=externalCustomer});
   throw new InvalidOperationException("Unexpected fixture request");
  }
  var onlyGets=true;
  await AccountingProbeTests.WithServer(200,"{}",async(url,_)=>{
   check((await s.SaveAccountingProfileAsync(url,"synthetic-key")).Success,"invoice candidate profile configured");
   check((await s.EnrollOrVerifyMarkerAsync(true)).Success,"invoice candidate marker enrolled");
   check((await s.CompareSavedInvoiceCandidateAsync(project,next,nextDetail.Summary.PayloadHash,reference,true)).Outcome=="CurrentProvisionalCustomerLinkRequired","candidate requires scoped customer link");
   var directory=await s.SyncCustomerDirectoryAsync();check(directory.Outcome=="Synchronized","candidate directory synchronized");
   var revision=directory.Report!.Revision;
   check((await s.SaveProvisionalLinkAsync(revision,"CRC ACCOUNTING TEST",0,client,externalCustomer)).Outcome=="Saved","candidate provisional link saved");
   var beforeComparison=s.ReadInvoiceDraft(project,next);
   check(beforeComparison.Summary.State=="Approved"&&beforeComparison.Summary.PayloadHash==nextDetail.Summary.PayloadHash,"comparison baseline is approved immutable draft");
   var observed=await s.CompareSavedInvoiceCandidateAsync(project,next,nextDetail.Summary.PayloadHash,reference,true);
   check(observed.Outcome=="CandidateForManualReview"&&observed.Outstanding==195.34m&&!observed.MayMarkIssued,"saved approved invoice compared against read-only fixture");
   var afterComparison=s.ReadInvoiceDraft(project,next);
   check(afterComparison.Summary==beforeComparison.Summary&&afterComparison.Lines.SequenceEqual(beforeComparison.Lines),"comparison leaves approved draft and lines unchanged");
   s.RemoveProvisionalLink(revision,client,externalCustomer);
   check((await s.CompareSavedInvoiceCandidateAsync(project,next,nextDetail.Summary.PayloadHash,reference,true)).Outcome=="CurrentProvisionalCustomerLinkRequired","removed link cannot support candidate match");
   try{await worker.CompareSavedInvoiceCandidateAsync(project,next,nextDetail.Summary.PayloadHash,reference,true);throw new Exception("FAIL: employee candidate access");}catch(UnauthorizedAccessException){check(true,"employee cannot compare accounting invoice candidates");}
  },14,observe:req=>onlyGets&=req.StartsWith("GET /api2/"),responseFor:Reply);
  check(onlyGets,"candidate service uses GET only");
  Invalid(()=>s.InvalidateApprovedWork(time,"Previously allocated",true),"approved invoice source cannot be invalidated");
  var erroneousInput=new WorkSubmissionInput(Guid.NewGuid(),task,day,"Time",30,null,null,"Synthetic duplicate time");
  var erroneous=worker.SubmitWork(erroneousInput);
  check(s.ReadWorkInvalidation(erroneous)==null,"new fact has no invented invalidation");
  Invalid(()=>s.InvalidateApprovedWork(erroneous,"Unreviewed",true),"pending work cannot be invalidated as approved");
  s.ReviewWork(erroneous,true,"Reviewed in error");s.ClassifyWork(erroneous,true,"Billable in error");
  Denied(()=>worker.InvalidateApprovedWork(erroneous,"Self",true),"employee cannot invalidate approved fact");
  Invalid(()=>s.InvalidateApprovedWork(erroneous,"",true),"invalidation requires evidence");
  Invalid(()=>s.InvalidateApprovedWork(erroneous,"Duplicate",false),"invalidation requires explicit confirmation");
  check(s.ReadInvoiceCandidates(project,day).Single().SourceId==erroneous,"unallocated approved fact initially billable");
  void CorrectionSql(string sql){using var db=new SqliteConnection($"Data Source={path};Pooling=False");db.Open();using var q=db.CreateCommand();q.CommandText=sql;q.ExecuteNonQuery();}
  CorrectionSql("CREATE TRIGGER test_invalidate_audit_abort BEFORE INSERT ON audit_events WHEN NEW.action='InvalidateApprovedWork' BEGIN SELECT RAISE(ABORT,'Synthetic audit failure'); END");
  try{s.InvalidateApprovedWork(erroneous,"Synthetic rollback",true);throw new Exception("FAIL: invalidation rollback");}catch(SqliteException){check(s.ReadWorkInvalidation(erroneous)==null&&s.ReadInvoiceCandidates(project,day).Single().SourceId==erroneous,"failed audit rolls back invalidation and billing eligibility");}
  CorrectionSql("DROP TRIGGER test_invalidate_audit_abort");
  var correctionPeer=new LocalPractice(path);check(correctionPeer.Login("admin",pw),"correction concurrent session authenticated");
  WorkInvalidation? TryInvalidate(LocalPractice service){try{return service.InvalidateApprovedWork(erroneous,"Verified duplicate entry, not actual additional work",true);}catch(ArgumentException){return null;}}
  var results=await Task.WhenAll(Task.Run(()=>TryInvalidate(s)),Task.Run(()=>TryInvalidate(correctionPeer)));
  check(results.Count(x=>x!=null)==1,"concurrent invalidation records one decision");
  var invalidation=results.Single(x=>x!=null)!;
  foreach(var sql in new[]{$"UPDATE work_invalidations SET reason='Changed' WHERE submission_id={erroneous}",$"DELETE FROM work_invalidations WHERE submission_id={erroneous}",$"INSERT INTO work_invalidations(submission_id,reason,actor_id,recorded_utc) VALUES({time},'Invalid',1,'synthetic')"}){try{CorrectionSql(sql);throw new Exception("FAIL: invalidation SQL guard");}catch(SqliteException){check(true,"database protects invalidation evidence and allocated work");}}
  check(invalidation.SubmissionId==erroneous&&s.ReadWorkInvalidation(erroneous)==invalidation,"approved fact invalidation retains independent evidence");
  check(worker.ReadMySubmissions(day,day).Single(x=>x.Id==erroneous).ReviewState=="Invalidated","employee sees effective invalidation rather than original approval");
  check(s.ReadWorkReview(day,day,false).Single(x=>x.Id==erroneous).ReviewState=="Invalidated","review history exposes effective invalidation");
  check(s.ReadInvoiceCandidates(project,day).Count==0&&!s.ReadBillingWork(day,day,false).Any(x=>x.Id==erroneous),"invalidated work excluded from billing");
  check(s.ReadProjectFinancialReport(project,day,day).ApprovedMinutes==61&&s.ReadProjectFinancialReport(project,day,day).TotalOperationalCost==47.92m,"project financial totals exclude invalidated duplicate only");
  var correctedOps=s.ReadProjectOperations(project,day,day);
  check(correctedOps.SubmittedMinutes==91&&correctedOps.ApprovedMinutes==61&&correctedOps.InvalidatedMinutes==30&&correctedOps.RejectedMinutes==0&&correctedOps.PendingMinutes==0,"operations reconciles invalidated time separately");
  var correctedMix=s.ReadFirmTimeMix(day,day);
  check(correctedMix.ApprovedMinutes==61&&correctedMix.InvalidatedMinutes==30&&s.ReadFirmLaborCost(day,day).ApprovedMinutes==61,"firm time and labor cost use effective approval");
  Invalid(()=>s.InvalidateApprovedWork(erroneous,"Repeated",true),"invalidation cannot be repeated");
  check(worker.SubmitWork(erroneousInput)==erroneous,"original operation retry cannot recreate invalidated fact");
  Invalid(()=>worker.ReplaceRejectedWork(erroneous,erroneousInput with{OperationKey=Guid.NewGuid()}),"invalidated fact requires explicit correction path");
  var correction=erroneousInput with{OperationKey=Guid.NewGuid(),Minutes=10,Description="Correct actual time"};
  var replacement=s.ReadProjectFinancialReport(project,day,day);
  var correctedId=worker.ReplaceInvalidatedWork(erroneous,correction);
  check(worker.ReadMyWorkReplacement(erroneous)?.ReplacementId==correctedId&&worker.ReplaceInvalidatedWork(erroneous,correction)==correctedId,"invalidated fact receives one linked idempotent correction");
  check(s.ReadProjectFinancialReport(project,day,day)==replacement&&s.ReadInvoiceCandidates(project,day).Count==0,"correction does not recognize revenue or cost before review");
  s.ReviewWork(correctedId,true,"Verified actual correction");s.ClassifyWork(correctedId,true,"Reviewed billing");
  check(s.ReadInvoiceCandidates(project,day).Single().SourceId==correctedId&&s.ReadProjectFinancialReport(project,day,day).ApprovedMinutes==71,"only newly approved correction becomes billable");
  var erroneousExpense=worker.SubmitWork(new(Guid.NewGuid(),task,day,"Expense",null,5m,"Supplies","Synthetic duplicate expense"));s.ReviewWork(erroneousExpense,true,"Reviewed in error");
  s.InvalidateApprovedWork(erroneousExpense,"Verified duplicate expense",true);
  check(s.ClassifyWork(erroneousExpense,true,"Synthetic")== "Invalidated","invalidated unclassified expense cannot enter billing");
  check(s.ReadProjectFinancialReport(project,day,day).ApprovedExpenseCost==12.34m&&s.ReadProjectOperations(project,day,day).InvalidatedExpenseSubmissions==1,"invalidated expense excluded from cost and reported separately");
  var allocated=s.CreateInvoiceDraft(new(Guid.NewGuid(),project,day,day.AddDays(30),Array.Empty<FixedFeeInput>(),new[]{correctedId}));
  var allocatedDetail=s.ReadInvoiceDraft(project,allocated);
  Invalid(()=>s.InvalidateApprovedWork(correctedId,"Allocated",true),"even unissued allocated fact requires reconciliation");
  s.DecideInvoiceDraft(project,allocated,allocatedDetail.Summary.PayloadHash,false,"Synthetic cancellation");
  Invalid(()=>s.InvalidateApprovedWork(correctedId,"Previously allocated",true),"prior cancelled allocation remains protected from invalidation");
  Denied(()=>worker.ReadWorkAllocationReview(project,correctedId),"employee cannot read allocation reconciliation");
  Denied(()=>worker.ReconcileCancelledWorkAllocations(project,correctedId,"wrong",null,"Unauthorized",true),"employee cannot reconcile allocated work");
  Invalid(()=>s.ReadWorkAllocationReview(long.MaxValue,correctedId),"allocation review requires exact project");
  var approvedAllocation=s.ReadWorkAllocationReview(project,time);
  check(!approvedAllocation.Eligible&&approvedAllocation.Allocations.Count==2&&approvedAllocation.Allocations.Any(x=>x.State=="Approved"),"approved or possibly exported allocation remains ineligible");
  Invalid(()=>s.ReconcileCancelledWorkAllocations(project,time,approvedAllocation.EvidenceHash,null,"Approved allocation",true),"approved allocation cannot be reconciled as cancelled");
  var cancelledAllocation=s.ReadWorkAllocationReview(project,correctedId);
  check(cancelledAllocation.Eligible&&cancelledAllocation.Allocations.Single().DraftId==allocated&&cancelledAllocation.Allocations.Single().PayloadHash==allocatedDetail.Summary.PayloadHash,"cancelled allocation review binds exact immutable draft evidence");
  Invalid(()=>s.ReconcileCancelledWorkAllocations(project,correctedId,"wrong",null,"Stale evidence",true),"allocation reconciliation rejects wrong evidence hash");
  Invalid(()=>s.ReconcileCancelledWorkAllocations(project,correctedId,cancelledAllocation.EvidenceHash,null,"",true),"allocation reconciliation requires reason");
  Invalid(()=>s.ReconcileCancelledWorkAllocations(project,correctedId,cancelledAllocation.EvidenceHash,null,"Unconfirmed",false),"allocation reconciliation requires confirmation");
  var laterDraft=s.CreateInvoiceDraft(new(Guid.NewGuid(),project,day,day.AddDays(30),Array.Empty<FixedFeeInput>(),new[]{correctedId}));
  Invalid(()=>s.ReconcileCancelledWorkAllocations(project,correctedId,cancelledAllocation.EvidenceHash,null,"Stale list",true),"new allocation prevents stale reconciliation");
  var laterDetail=s.ReadInvoiceDraft(project,laterDraft);
  s.DecideInvoiceDraft(project,laterDraft,laterDetail.Summary.PayloadHash,false,"Synthetic cancellation");
  var currentAllocation=s.ReadWorkAllocationReview(project,correctedId);
  check(currentAllocation.Eligible&&currentAllocation.Allocations.Count==2&&currentAllocation.EvidenceHash!=cancelledAllocation.EvidenceHash,"all cancelled allocations bound to reviewed evidence");
  Invalid(()=>s.ReconcileCancelledWorkAllocations(project,correctedId,cancelledAllocation.EvidenceHash,null,"Stale hash",true),"even new cancelled allocation changes reviewed identity");
  CorrectionSql("CREATE TRIGGER test_reconcile_audit_abort BEFORE INSERT ON audit_events WHEN NEW.action='ReconcileCancelledWorkAllocations' BEGIN SELECT RAISE(ABORT,'Synthetic audit failure'); END");
  try{s.ReconcileCancelledWorkAllocations(project,correctedId,currentAllocation.EvidenceHash,null,"Synthetic rollback",true);throw new Exception("FAIL: reconciliation rollback");}catch(SqliteException){check(s.ReadWorkInvalidation(correctedId)==null&&s.ReadWorkAllocationReview(project,correctedId).Eligible,"failed reconciliation audit preserves original work");}
  CorrectionSql("DROP TRIGGER test_reconcile_audit_abort");
  WorkInvalidation? TryReconcile(LocalPractice service){try{return service.ReconcileCancelledWorkAllocations(project,correctedId,currentAllocation.EvidenceHash,null,"Verified erroneous work; all allocations cancelled before approval or export",true);}catch(ArgumentException){return null;}}
  var reconciliations=await Task.WhenAll(Task.Run(()=>TryReconcile(s)),Task.Run(()=>TryReconcile(correctionPeer)));
  check(reconciliations.Count(x=>x!=null)==1,"concurrent reconciliation records one decision");
  var reconciliation=reconciliations.Single(x=>x!=null)!;
  check(reconciliation.AllocationReviewHash==currentAllocation.EvidenceHash,"invalidation retains exact allocation evidence fingerprint");
  foreach(var sql in new[]{$"UPDATE work_invalidations SET allocation_review_hash=NULL WHERE submission_id={correctedId}",$"INSERT INTO work_invalidations(submission_id,reason,actor_id,recorded_utc,allocation_review_hash) VALUES({time},'Approved draft',1,'synthetic','{currentAllocation.EvidenceHash}')",$"INSERT INTO invoice_draft_lines(draft_id,source_id,line_type,description,minutes,rate_cents,amount_cents) SELECT {laterDraft},id,kind,description,minutes,18000,3000 FROM work_submissions WHERE id={correctedId}"}){try{CorrectionSql(sql);throw new Exception("FAIL: cancelled reconciliation SQL guard");}catch(SqliteException){check(true,"database protects reviewed allocation evidence and blocks approved source invalidation or new allocation");}}
  check(s.ReadWorkInvalidation(correctedId)==reconciliation&&s.ReadInvoiceCandidates(project,day).Count==0,"reconciled source invalidated without duplicate billing");
  check(s.ReadInvoiceDraft(project,allocated).Summary.State=="Cancelled"&&s.ReadInvoiceDraft(project,laterDraft).Summary.State=="Cancelled"&&s.ReadInvoiceDraft(project,laterDraft).Summary.PayloadHash==laterDetail.Summary.PayloadHash,"cancelled drafts and immutable hashes retained");
  check(s.ReadProjectFinancialReport(project,day,day).ApprovedMinutes==61&&s.ReadProjectFinancialReport(project,day,day).ApprovedInternalDraftValue==295.34m,"reconciliation removes only erroneous cost, not approved draft history");
  Invalid(()=>s.ReconcileCancelledWorkAllocations(project,correctedId,currentAllocation.EvidenceHash,null,"Duplicate",true),"reconciliation cannot repeat");
  var reconciledInput=correction with{OperationKey=Guid.NewGuid(),Minutes=8,Description="Verified actual corrected duration"};
  var reconciledId=worker.ReplaceInvalidatedWork(correctedId,reconciledInput);
  check(worker.ReadMyWorkReplacement(correctedId)?.ReplacementId==reconciledId&&worker.ReplaceInvalidatedWork(correctedId,reconciledInput)==reconciledId,"reconciled source permits one linked replacement");
  check(s.ReadInvoiceCandidates(project,day).Count==0,"replacement remains unbillable before review");
  s.ReviewWork(reconciledId,true,"Independent fact review");s.ClassifyWork(reconciledId,true,"Independent classification");
  check(s.ReadInvoiceCandidates(project,day).Single().SourceId==reconciledId,"only corrected approved fact becomes billing candidate");
 }
}
