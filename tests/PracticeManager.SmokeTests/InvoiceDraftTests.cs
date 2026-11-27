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
   check(s.ReadInvoiceCandidateObservations(project,next).Count==0,"ordinary read-only comparison does not silently persist evidence");
   Denied(()=>worker.ReadInvoiceCandidateObservations(project,next),"ordinary employee cannot view accounting observation history");
   check((await s.CaptureSavedInvoiceCandidateAsync(project,next,nextDetail.Summary.PayloadHash,reference,true,false)).Outcome=="OperatorCaptureConfirmationRequired","capture requires explicit consent");
   CorrectionSql("CREATE TRIGGER test_observation_audit_abort BEFORE INSERT ON audit_events WHEN NEW.action='CaptureInvoiceCandidateObservation' BEGIN SELECT RAISE(ABORT,'Synthetic audit failure'); END");
   try{await s.CaptureSavedInvoiceCandidateAsync(project,next,nextDetail.Summary.PayloadHash,reference,true,true);throw new Exception("FAIL: observation rollback");}catch(SqliteException){check(s.ReadInvoiceCandidateObservations(project,next).Count==0,"failed audit rolls back synthetic observation");}
   CorrectionSql("DROP TRIGGER test_observation_audit_abort");
   var captured=await s.CaptureSavedInvoiceCandidateAsync(project,next,nextDetail.Summary.PayloadHash,reference,true,true);
   var observation=s.ReadInvoiceCandidateObservations(project,next).Single();
   check(captured.Outcome=="CandidateForManualReview"&&captured.ObservationId==observation.Id&&captured.InvoiceKey==externalInvoice&&!captured.MayMarkIssued&&!captured.MayPost&&!captured.MaySendReminder,"explicit synthetic candidate capture is evidence only");
   check(observation.DraftId==next&&observation.PayloadHash==nextDetail.Summary.PayloadHash&&observation.BusinessName=="CRC ACCOUNTING TEST"&&observation.MarkerKey==marker&&observation.ProfileRevision==revision&&observation.CustomerKey==externalCustomer&&observation.InvoiceKey==externalInvoice&&observation.Reference==reference&&observation.IssueDate==day&&observation.ObservedTotal==295.34m&&observation.ObservedOutstanding==195.34m&&observation.ObservedUtc==captured.ObservedUtc,"capture binds exact approved draft and freshly observed fixture identity");
   foreach(var sql in new[]{$"UPDATE invoice_candidate_observations SET outstanding_cents=0 WHERE id={observation.Id}",$"DELETE FROM invoice_candidate_observations WHERE id={observation.Id}",$"INSERT INTO invoice_candidate_observations(draft_id,payload_hash,business_name,marker_key,profile_revision,customer_key,invoice_key,reference,issue_date,total_cents,outstanding_cents,observed_utc,actor_id) SELECT {draft},payload_hash,business_name,marker_key,profile_revision,customer_key,invoice_key,reference,issue_date,total_cents,outstanding_cents,observed_utc,actor_id FROM invoice_candidate_observations WHERE id={observation.Id}"}){try{CorrectionSql(sql);throw new Exception("FAIL: observation SQL guard");}catch(SqliteException){check(true,"database preserves observation evidence and rejects mismatched draft");}}
   var absent=await s.CaptureSavedInvoiceCandidateAsync(project,next,nextDetail.Summary.PayloadHash,"UNOBSERVED-SYNTHETIC",true,true);
   check(absent.Outcome=="ReferenceNotObserved"&&absent.ObservationId==null&&s.ReadInvoiceCandidateObservations(project,next).Count==1,"missing reference does not create durable match or infer nonissuance");
   var afterComparison=s.ReadInvoiceDraft(project,next);
   check(afterComparison.Summary==beforeComparison.Summary&&afterComparison.Lines.SequenceEqual(beforeComparison.Lines),"comparison leaves approved draft and lines unchanged");
   s.RemoveProvisionalLink(revision,client,externalCustomer);
   check((await s.CaptureSavedInvoiceCandidateAsync(project,next,nextDetail.Summary.PayloadHash,reference,true,true)).Outcome=="CurrentProvisionalCustomerLinkRequired"&&s.ReadInvoiceCandidateObservations(project,next).Single().Id==observation.Id,"removed link blocks new capture while historical evidence remains readable");
   check((await s.CompareSavedInvoiceCandidateAsync(project,next,nextDetail.Summary.PayloadHash,reference,true)).Outcome=="CurrentProvisionalCustomerLinkRequired","removed link cannot support candidate match");
   try{await worker.CompareSavedInvoiceCandidateAsync(project,next,nextDetail.Summary.PayloadHash,reference,true);throw new Exception("FAIL: employee candidate access");}catch(UnauthorizedAccessException){check(true,"employee cannot compare accounting invoice candidates");}
  },40,observe:req=>onlyGets&=req.StartsWith("GET /api2/"),responseFor:Reply);
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
  check(s.ReadApprovedInvoiceHold(project,next)==null,"approved draft has no invented hold");
  Denied(()=>worker.ReadApprovedInvoiceHold(project,next),"employee cannot read invoice hold");
  Denied(()=>worker.HoldApprovedInvoice(project,next,nextDetail.Summary.PayloadHash,"Unauthorized",true),"employee cannot place invoice hold");
  Invalid(()=>s.HoldApprovedInvoice(project,next,"wrong","Wrong hash",true),"hold requires exact approved payload");
  Invalid(()=>s.HoldApprovedInvoice(project,draft,detail.Summary.PayloadHash,"Cancelled",true),"cancelled draft cannot enter approved hold");
  Invalid(()=>s.HoldApprovedInvoice(project,next,nextDetail.Summary.PayloadHash,"",true),"hold requires discrepancy evidence");
  Invalid(()=>s.HoldApprovedInvoice(project,next,nextDetail.Summary.PayloadHash,"Unconfirmed",false),"hold requires explicit confirmation");
  var beforeHold=s.ReadProjectFinancialReport(project,day,day);
  CorrectionSql("CREATE TRIGGER test_hold_audit_abort BEFORE INSERT ON audit_events WHEN NEW.action='HoldApprovedInvoice' BEGIN SELECT RAISE(ABORT,'Synthetic audit failure'); END");
  try{s.HoldApprovedInvoice(project,next,nextDetail.Summary.PayloadHash,"Synthetic rollback",true);throw new Exception("FAIL: hold rollback");}catch(SqliteException){check(s.ReadApprovedInvoiceHold(project,next)==null&&s.ReadProjectFinancialReport(project,day,day)==beforeHold,"failed hold audit rolls back protective evidence");}
  CorrectionSql("DROP TRIGGER test_hold_audit_abort");
  ApprovedInvoiceHold? TryHold(LocalPractice service){try{return service.HoldApprovedInvoice(project,next,nextDetail.Summary.PayloadHash,"Potential duplicate charge; external accounting review required",true);}catch(ArgumentException){return null;}}
  var holds=await Task.WhenAll(Task.Run(()=>TryHold(s)),Task.Run(()=>TryHold(correctionPeer)));
  check(holds.Count(x=>x!=null)==1,"concurrent invoice hold records one decision");
  var hold=holds.Single(x=>x!=null)!;
  foreach(var sql in new[]{$"UPDATE invoice_hold_events SET reason='Changed' WHERE draft_id={next}",$"DELETE FROM invoice_hold_events WHERE draft_id={next}",$"INSERT INTO invoice_hold_events(kind,draft_id,payload_hash,reason,actor_id,recorded_utc) VALUES('Hold',{draft},'{detail.Summary.PayloadHash}','Cancelled',1,'synthetic')",$"INSERT INTO audit_events(actor_id,action,entity,entity_id) VALUES(1,'PrepareManualExport','InvoiceDraft',{next})",$"INSERT INTO audit_events(actor_id,action,entity,entity_id) VALUES(1,'RetrieveReviewDocument','GeneratedReviewDocument',{doc.Id})"}){try{CorrectionSql(sql);throw new Exception("FAIL: hold SQL guard");}catch(SqliteException){check(true,"database preserves hold evidence and rejects forbidden export audit or cancelled source");}}
  check(s.ReadApprovedInvoiceHold(project,next)==hold&&hold.PayloadHash==nextDetail.Summary.PayloadHash,"exact immutable invoice hold evidence recorded");
  var held=s.ReadInvoiceDraft(project,next);
  check(held.Summary.State=="Approved"&&held.Summary.ReconciliationHold&&held.Summary.PayloadHash==nextDetail.Summary.PayloadHash&&held.Lines.SequenceEqual(nextDetail.Lines),"hold preserves original approval and immutable lines");
  check(s.ReadInvoiceDrafts(project).Single(x=>x.Id==next).ReconciliationHold,"draft register exposes reconciliation hold");
  Invalid(()=>s.HoldApprovedInvoice(project,next,nextDetail.Summary.PayloadHash,"Repeat",true),"duplicate hold denied");
  Invalid(()=>s.ExportInvoiceDraftCsv(project,next,nextDetail.Summary.PayloadHash),"held draft cannot be re-exported");
  Invalid(()=>s.PrepareInvoiceReviewDocx(project,next,nextDetail.Summary.PayloadHash),"held draft cannot generate or reissue review document");
  Invalid(()=>s.RetrieveInvoiceReviewDocument(project,next,doc.Id,doc.Sha256),"held draft cannot retrieve potentially reusable export bytes");
  check(s.ReadInvoiceReviewDocuments(project,next).Single().Id==doc.Id,"historical document metadata remains available for reconciliation");
  check(s.DecideInvoiceDraft(project,next,nextDetail.Summary.PayloadHash,false,"Cancel")== "AlreadyApproved","hold is not cancellation");
  check(!s.ReadWorkAllocationReview(project,time).Eligible&&!s.ReadInvoiceCandidates(project,day).Any(x=>x.SourceId==time),"hold never releases approved source allocation");
  var afterHold=s.ReadProjectFinancialReport(project,day,day);
  check(afterHold.ApprovedInternalDraftValue==beforeHold.ApprovedInternalDraftValue&&afterHold.HeldApprovedDraftValue==295.34m&&afterHold.HeldApprovedDraftCount==1&&afterHold.TotalOperationalCost==beforeHold.TotalOperationalCost,"held amount separately identified as subset of approved internal drafts, no cost or revenue reversal");
  Invalid(()=>s.CreateInvoiceDraft(new(Guid.NewGuid(),project,day,day.AddDays(30),new[]{new FixedFeeInput("Over cap",1m)},Array.Empty<long>())),"held draft retains contractual cap allocation");
  var holdHistory=s.ReadInvoiceHoldHistory(project,next);
  check(hold.EventId>0&&holdHistory.Count==1&&holdHistory[0].EventId==hold.EventId&&holdHistory[0].Kind=="Hold","hold has exact immutable event identity");
  Denied(()=>worker.ReadInvoiceHoldHistory(project,next),"employee cannot view hold history");
  Denied(()=>worker.ReleaseApprovedInvoiceHold(project,next,hold.EventId,"wrong","","Unauthorized",false,false),"release authorization precedes input validation");
  Denied(()=>s.ReleaseApprovedInvoiceHold(project,next,hold.EventId,nextDetail.Summary.PayloadHash,"Synthetic evidence","Self release",true,true),"owner cannot release own hold despite approval exception");
  var reviewRole=s.ReadStaffRoles().Single(x=>x.Name=="Financial / Accounting");
  foreach(var grant in new[]{"Clients.View","Projects.View","Billing.Invoices.View","Billing.Invoices.Edit","Billing.Invoices.Approve"})
  {var existing=s.ReadStaffGrants(reviewRole.Id).Single(x=>x.Permission==grant);s.SetStaffGrant(reviewRole.Id,grant,true,existing.Allowed);}
  var reviewActor=s.CreateStaffAccount("hold-reviewer",reviewRole.Id,"Synthetic-hold-review-temp-0001");
  var reviewer=new LocalPractice(path);check(reviewer.ChangeRequiredPassword("hold-reviewer","Synthetic-hold-review-temp-0001","Synthetic-hold-review-password-0001")&&reviewer.Login("hold-reviewer","Synthetic-hold-review-password-0001"),"independent hold reviewer authenticated");
  Invalid(()=>reviewer.ReleaseApprovedInvoiceHold(project,next,hold.EventId+100,nextDetail.Summary.PayloadHash,"Evidence","Stale hold",true,true),"release requires exact current hold event");
  Invalid(()=>reviewer.ReleaseApprovedInvoiceHold(project,next,hold.EventId,"wrong","Evidence","Wrong hash",true,true),"release requires exact approved payload");
  Invalid(()=>reviewer.ReleaseApprovedInvoiceHold(project,next,hold.EventId,nextDetail.Summary.PayloadHash,"","Missing evidence",true,true),"release requires independent evidence reference");
  Invalid(()=>reviewer.ReleaseApprovedInvoiceHold(project,next,hold.EventId,nextDetail.Summary.PayloadHash,"Evidence","",true,true),"release requires reason");
  Invalid(()=>reviewer.ReleaseApprovedInvoiceHold(project,next,hold.EventId,nextDetail.Summary.PayloadHash,"Evidence","Erroneous draft",false,true),"erroneous original cannot be released");
  Invalid(()=>reviewer.ReleaseApprovedInvoiceHold(project,next,hold.EventId,nextDetail.Summary.PayloadHash,"Evidence","Possibly issued",true,false),"unknown or issued external invoice cannot be released");
  CorrectionSql("CREATE TRIGGER test_release_audit_abort BEFORE INSERT ON audit_events WHEN NEW.action='ReleaseApprovedInvoiceHold' BEGIN SELECT RAISE(ABORT,'Synthetic audit failure'); END");
  try{reviewer.ReleaseApprovedInvoiceHold(project,next,hold.EventId,nextDetail.Summary.PayloadHash,"Evidence","Synthetic rollback",true,true);throw new Exception("FAIL: release rollback");}catch(SqliteException){check(s.ReadInvoiceHoldHistory(project,next).Count==1&&s.ReadInvoiceDraft(project,next).Summary.ReconciliationHold,"failed release audit preserves current hold");}
  CorrectionSql("DROP TRIGGER test_release_audit_abort");
  var reviewerPeer=new LocalPractice(path);check(reviewerPeer.Login("hold-reviewer","Synthetic-hold-review-password-0001"),"concurrent independent reviewer authenticated");
  InvoiceHoldEvent? TryRelease(LocalPractice service){try{return service.ReleaseApprovedInvoiceHold(project,next,hold.EventId,nextDetail.Summary.PayloadHash,"Synthetic accounting review report 001","Verified original draft correct and no external issuance after complete independent review",true,true);}catch(ArgumentException){return null;}}
  var releases=await Task.WhenAll(Task.Run(()=>TryRelease(reviewer)),Task.Run(()=>TryRelease(reviewerPeer)));
  check(releases.Count(x=>x!=null)==1,"concurrent release records one decision");var released=releases.Single(x=>x!=null)!;
  check(released.Kind=="Release"&&released.PriorEventId==hold.EventId&&released.ActorId==reviewActor&&s.ReadInvoiceHoldHistory(project,next).Count==2,"independent release retains immutable hold lineage and evidence");
  check(!s.ReadInvoiceDraft(project,next).Summary.ReconciliationHold&&s.ReadInvoiceDraft(project,next).Summary.State=="Approved","released draft returns to approved internal preparation only");
  check(s.ExportInvoiceDraftCsv(project,next,nextDetail.Summary.PayloadHash).Contains("NOT AN ISSUED INVOICE")&&s.RetrieveInvoiceReviewDocument(project,next,doc.Id,doc.Sha256).Bytes.SequenceEqual(doc.Bytes),"verified release permits original internal exports");
  check(s.ReadProjectFinancialReport(project,day,day).HeldApprovedDraftCount==0&&s.ReadProjectFinancialReport(project,day,day).ApprovedInternalDraftValue==295.34m&&!s.ReadWorkAllocationReview(project,time).Eligible,"release leaves allocations and approved value unchanged");
  Invalid(()=>reviewer.ReleaseApprovedInvoiceHold(project,next,hold.EventId,nextDetail.Summary.PayloadHash,"Evidence","Duplicate release",true,true),"repeat release denied");
  var renewed=s.HoldApprovedInvoice(project,next,nextDetail.Summary.PayloadHash,"New discrepancy requires investigation",true);
  check(renewed.EventId>released.EventId&&s.ReadInvoiceHoldHistory(project,next).Count==3&&s.ReadInvoiceDraft(project,next).Summary.ReconciliationHold,"renewed hold immediately protects previously released draft");
  Invalid(()=>reviewer.ReleaseApprovedInvoiceHold(project,next,hold.EventId,nextDetail.Summary.PayloadHash,"Evidence","Old hold",true,true),"old event cannot release renewed hold");
  Invalid(()=>s.ExportInvoiceDraftCsv(project,next,nextDetail.Summary.PayloadHash),"renewed hold blocks exports");
  foreach(var sql in new[]{$"UPDATE invoice_hold_events SET reason='Changed' WHERE event_id={released.EventId}",$"DELETE FROM invoice_hold_events WHERE event_id={renewed.EventId}",$"INSERT INTO invoice_hold_events(draft_id,payload_hash,kind,prior_event_id,reason,evidence_reference,actor_id,recorded_utc) VALUES({next},'{nextDetail.Summary.PayloadHash}','Release',{hold.EventId},'Stale','Evidence',{reviewActor},'synthetic')",$"INSERT INTO invoice_hold_events(draft_id,payload_hash,kind,prior_event_id,reason,evidence_reference,actor_id,recorded_utc) VALUES({next},'{nextDetail.Summary.PayloadHash}','Release',{renewed.EventId},'Self','Evidence',1,'synthetic')",$"INSERT INTO approved_invoice_holds(draft_id,payload_hash,reason,actor_id,recorded_utc) VALUES({next},'{nextDetail.Summary.PayloadHash}','Legacy bypass',1,'synthetic')"}){try{CorrectionSql(sql);throw new Exception("FAIL: hold history SQL guard");}catch(SqliteException){check(true,"database preserves event lineage and rejects stale, self or legacy bypass");}}
 }
}
