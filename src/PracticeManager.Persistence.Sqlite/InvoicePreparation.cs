using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PracticeManager.Core;
namespace PracticeManager.Persistence;
public sealed record WorkAllocationEvidence(long DraftId,string DraftNumber,string PayloadHash,string State,decimal Amount);
public sealed record WorkAllocationReview(long ProjectId,long SubmissionId,string EvidenceHash,IReadOnlyList<WorkAllocationEvidence> Allocations,bool Eligible);
public sealed record ApprovedInvoiceHold(long DraftId,string PayloadHash,string Reason,long ActorId,string RecordedUtc,long EventId=0);
public sealed record InvoiceHoldEvent(long EventId,long DraftId,string PayloadHash,string Kind,long? PriorEventId,string Reason,string? EvidenceReference,long ActorId,string RecordedUtc);
public sealed partial class LocalPractice
{
 public IReadOnlyList<InvoiceHoldEvent> ReadInvoiceHoldHistory(long project,long draft)=>throw new NotImplementedException("Invoice hold history not implemented.");
 public InvoiceHoldEvent ReleaseApprovedInvoiceHold(long project,long draft,long expectedHoldId,string expectedHash,string evidenceReference,string reason,bool originalCorrect,bool noExternalIssuance)=>throw new NotImplementedException("Invoice hold release not implemented.");
 public ApprovedInvoiceHold? ReadApprovedInvoiceHold(long project,long draft)
 {
  using var c=Open();using var t=c.BeginTransaction();RequireInvoices(c,t,"Billing.Invoices.View");ProjectRow(c,t,project);InvoiceDetail(c,t,project,draft);
  using var q=Command(c,t,"SELECT draft_id,payload_hash,reason,actor_id,recorded_utc FROM approved_invoice_holds WHERE draft_id=$i",("$i",draft));using var r=q.ExecuteReader();
  return r.Read()?new(r.GetInt64(0),r.GetString(1),r.GetString(2),r.GetInt64(3),r.GetString(4)):null;
 }
 public ApprovedInvoiceHold HoldApprovedInvoice(long project,long draft,string expectedHash,string reason,bool confirmed)
 {
  using var c=Open();using var t=c.BeginTransaction(deferred:false);var actor=RequireInvoices(c,t,"Billing.Invoices.Edit");Require(c,t,"Billing.Invoices.Approve");ProjectRow(c,t,project);
  if(!confirmed)throw new ArgumentException("Explicit discrepancy hold confirmation required.");
  var note=Validation.Text(reason,1000,"Discrepancy and reconciliation evidence",true);
  var summary=InvoiceDetail(c,t,project,draft).Summary;
  if(summary.State!="Approved"||summary.PayloadHash!=expectedHash||summary.ReconciliationHold)throw new ArgumentException("Exact approved draft not already held required.");
  var utc=UtcNow();Exec(c,t,"INSERT INTO approved_invoice_holds(draft_id,payload_hash,reason,actor_id,recorded_utc) VALUES($i,$h,$n,$a,$u)",("$i",draft),("$h",expectedHash),("$n",note),("$a",actor),("$u",utc));Audit(c,t,actor,"HoldApprovedInvoice","InvoiceDraft",draft);t.Commit();return new(draft,expectedHash,note,actor,utc);
 }
 public WorkAllocationReview ReadWorkAllocationReview(long project,long submission)
 {
  using var c=Open();using var t=c.BeginTransaction();RequireWorkCorrection(c,t);RequireInvoices(c,t,"Billing.Invoices.View");return AllocationReview(c,t,project,submission);
 }
 private static WorkAllocationReview AllocationReview(SqliteConnection c,SqliteTransaction t,long project,long submission)
 {
  ProjectRow(c,t,project);
  if(Convert.ToInt64(Scalar(c,t,"SELECT count(*) FROM work_submissions s JOIN project_tasks w ON w.id=s.task_id JOIN project_phases h ON h.id=w.phase_id WHERE h.project_id=$p AND s.id=$i",("$p",project),("$i",submission)))!=1)throw new ArgumentException("Work source unavailable in selected project.");
  var ids=new List<long>();using(var q=Command(c,t,"SELECT draft_id FROM invoice_draft_lines WHERE source_id=$i ORDER BY draft_id LIMIT 501",("$i",submission)))using(var r=q.ExecuteReader()){while(r.Read()){if(ids.Count==500)throw new ArgumentException("Allocation review exceeds development limit.");ids.Add(r.GetInt64(0));}}
  var evidence=new List<WorkAllocationEvidence>();foreach(var id in ids){var detail=InvoiceDetail(c,t,project,id);var line=detail.Lines.Single(x=>x.SourceId==submission);evidence.Add(new(id,detail.Summary.Number,detail.Summary.PayloadHash,detail.Summary.State,line.Amount));}
  var hash=HashProposal(JsonSerializer.Serialize(new{Format="CRC-CANCELLED-ALLOCATION-1",Project=project,Submission=submission,Allocations=evidence.Select(x=>new{x.DraftId,x.DraftNumber,x.PayloadHash,x.State,AmountCents=BillingCents(x.Amount,true)}).ToArray()})).ToLowerInvariant();
  var eligible=evidence.Count>0&&evidence.All(x=>x.State=="Cancelled")&&Convert.ToInt64(Scalar(c,t,@"SELECT count(*) FROM work_submissions s JOIN work_reviews r ON r.submission_id=s.id WHERE s.id=$i AND r.decision='Approved' AND NOT EXISTS(SELECT 1 FROM work_invalidations z WHERE z.submission_id=s.id) AND NOT EXISTS(SELECT 1 FROM invoice_draft_lines l LEFT JOIN invoice_draft_decisions d ON d.draft_id=l.draft_id WHERE l.source_id=s.id AND (coalesce(d.decision,'Draft')!='Cancelled' OR EXISTS(SELECT 1 FROM generated_review_documents g WHERE g.invoice_draft_id=l.draft_id) OR EXISTS(SELECT 1 FROM audit_events u WHERE u.entity='InvoiceDraft' AND u.entity_id=l.draft_id AND u.action='PrepareManualExport')))",("$i",submission)))==1;
  return new(project,submission,hash,evidence.AsReadOnly(),eligible);
 }
 public WorkInvalidation ReconcileCancelledWorkAllocations(long project,long submission,string expectedHash,long? closureId,string reason,bool confirmed)
 =>InvalidateWorkCore(submission,reason,confirmed,closureId,project,expectedHash);
 private const long MaxBillingCents=999999999999;
 private static long BillingCents(decimal value,bool positive=false)
 {
  if(value<0||positive&&value==0||value>MaxBillingCents/100m||decimal.Round(value,2)!=value)throw new ArgumentException("Enter a valid USD amount to the cent.");
  return decimal.ToInt64(value*100);
 }
 private long RequireInvoices(SqliteConnection c,SqliteTransaction t,string grant)
 {
  RequireProjectView(c,t);Require(c,t,"Billing.Invoices.View");return Require(c,t,grant);
 }
 private long RequireRates(SqliteConnection c,SqliteTransaction t)
 {Require(c,t,"Employees.View");return Require(c,t,"Billing.Rates");}
 public IReadOnlyList<EmployeeRate> ReadEmployeeRates(long employee)
 {
  using var c=Open();using var t=c.BeginTransaction();RequireRates(c,t);
  using var q=Command(c,t,"SELECT effective_on,bill_cents,cost_cents FROM employee_rates WHERE employee_id=$e ORDER BY effective_on DESC",("$e",employee));using var r=q.ExecuteReader();var rows=new List<EmployeeRate>();while(r.Read())rows.Add(new(employee,ReadPlanDate(r.GetString(0)),r.GetInt64(1)/100m,r.GetInt64(2)/100m));return rows.AsReadOnly();
 }
 public void RecordEmployeeRate(EmployeeRate rate)
 {
  using var c=Open();using var t=c.BeginTransaction(deferred:false);var actor=RequireRates(c,t);var b=BillingCents(rate.BillingRate);var k=BillingCents(rate.CostRate);
  if(rate.EffectiveOn==DateOnly.MinValue||b>100000000||k>100000000||Convert.ToInt64(Scalar(c,t,"SELECT count(*) FROM employees WHERE id=$e AND active=1",("$e",rate.EmployeeId)))!=1)throw new ArgumentException("Active employee, effective date and valid rates required.");
  if(Convert.ToInt64(Scalar(c,t,"SELECT count(*) FROM employee_rates WHERE employee_id=$e AND effective_on>=$d",("$e",rate.EmployeeId),("$d",PlanDate(rate.EffectiveOn))))!=0)throw new ArgumentException("A new rate must have a later effective date than existing rates.");
  Exec(c,t,"INSERT INTO employee_rates(employee_id,effective_on,bill_cents,cost_cents,recorded_by,recorded_utc) VALUES($e,$d,$b,$k,$a,$u)",("$e",rate.EmployeeId),("$d",PlanDate(rate.EffectiveOn)),("$b",b),("$k",k),("$a",actor),("$u",UtcNow()));Audit(c,t,actor,"RecordRate","Employee",rate.EmployeeId);t.Commit();
 }
 private static BillingPolicy? BillingPolicyRow(SqliteConnection c,SqliteTransaction t,long project)
 {
  using var q=Command(c,t,"SELECT mode,fixed_limit,total_limit,expenses,evidence FROM project_billing_policies WHERE project_id=$p",("$p",project));using var r=q.ExecuteReader();return r.Read()?new(project,r.GetString(0),r.GetInt64(1)/100m,r.IsDBNull(2)?null:r.GetInt64(2)/100m,r.GetInt64(3)==1,r.GetString(4)):null;
 }
 public BillingPolicy? ReadBillingPolicy(long project)
 {
  using var c=Open();using var t=c.BeginTransaction();RequireInvoices(c,t,"Billing.Invoices.View");ProjectRow(c,t,project);return BillingPolicyRow(c,t,project);
 }
 public void RecordBillingPolicy(BillingPolicy policy)
 {
  using var c=Open();using var t=c.BeginTransaction(deferred:false);var actor=RequireInvoices(c,t,"Billing.Invoices.Edit");var project=ProjectRow(c,t,policy.ProjectId);
  if(project.State!="Active")throw new ArgumentException("Authorized active project required.");
  var limit=BillingCents(policy.FixedLimit);long? total=policy.TotalLimit.HasValue?BillingCents(policy.TotalLimit.Value):null;
  var accepted=Convert.ToInt64(Scalar(c,t,"SELECT s.fee_cents FROM projects p JOIN engagements e ON e.id=p.engagement_id JOIN proposals s ON s.id=e.proposal_id WHERE p.id=$p",("$p",policy.ProjectId)));
  if(policy.Mode is not ("FixedFee" or "Hourly" or "Hybrid")||policy.Mode=="Hourly"&&limit!=0||policy.Mode=="FixedFee"&&limit!=accepted||policy.Mode=="Hybrid"&&limit>accepted||total.HasValue&&total.Value<limit)throw new ArgumentException("Billing mode or cap does not match the accepted proposal evidence.");
  if(BillingPolicyRow(c,t,policy.ProjectId)!=null)throw new ArgumentException("Billing policy already recorded; formal amendment workflow required.");
  var note=Validation.Text(policy.Evidence,1000,"Reviewed contract billing evidence",true);
  Exec(c,t,"INSERT INTO project_billing_policies(project_id,mode,fixed_limit,total_limit,expenses,evidence,recorded_by,recorded_utc) VALUES($p,$m,$f,$t,$e,$n,$a,$u)",("$p",policy.ProjectId),("$m",policy.Mode),("$f",limit),("$t",total),("$e",policy.Expenses?1:0),("$n",note),("$a",actor),("$u",UtcNow()));Audit(c,t,actor,"RecordBillingPolicy","Project",policy.ProjectId);t.Commit();
 }
 private static IReadOnlyList<InvoiceCandidate> BillingCandidates(SqliteConnection c,SqliteTransaction t,long project,DateOnly through,BillingPolicy policy)
 {
  using var q=Command(c,t,@"SELECT s.id,trim(e.first_name||' '||e.last_name),s.kind,s.work_date,s.description,s.minutes,s.amount_cents,k.scope_change_id,(SELECT bill_cents FROM employee_rates z WHERE z.employee_id=e.id AND z.effective_on<=s.work_date ORDER BY z.effective_on DESC LIMIT 1) FROM work_submissions s JOIN work_reviews r ON r.submission_id=s.id JOIN work_billing_classifications b ON b.submission_id=s.id JOIN employees e ON e.id=s.employee_id JOIN project_tasks k ON k.id=s.task_id JOIN project_phases h ON h.id=k.phase_id WHERE h.project_id=$p AND s.work_date<=$d AND r.decision='Approved' AND b.classification='Billable' AND NOT EXISTS(SELECT 1 FROM work_invalidations z WHERE z.submission_id=s.id) AND NOT EXISTS(SELECT 1 FROM invoice_draft_lines l LEFT JOIN invoice_draft_decisions x ON x.draft_id=l.draft_id WHERE l.source_id=s.id AND coalesce(x.decision,'Draft')!='Cancelled') ORDER BY s.work_date,s.id LIMIT 501",("$p",project),("$d",PlanDate(through)));
  using var r=q.ExecuteReader();var rows=new List<InvoiceCandidate>();while(r.Read()){
   if(rows.Count==500)throw new ArgumentException("More than 500 unallocated billing sources; narrow the cutoff.");
   var kind=r.GetString(2);int? minutes=r.IsDBNull(5)?null:r.GetInt32(5);long? rate=r.IsDBNull(8)?null:r.GetInt64(8);long? amount=kind=="Time"&&rate.HasValue?decimal.ToInt64(decimal.Round(minutes!.Value*rate.Value/60m,0,MidpointRounding.AwayFromZero)):kind=="Expense"?r.GetInt64(6):null;
   var status=!r.IsDBNull(7)?"Additional scope terms required":kind=="Time"&&policy.Mode=="FixedFee"?"Included in fixed fee":kind=="Expense"&&!policy.Expenses?"Expense pass-through not authorized":!amount.HasValue||amount.Value<=0?"Positive billing rate required":"Eligible";
   rows.Add(new(r.GetInt64(0),r.GetString(1),kind,ReadPlanDate(r.GetString(3)),r.GetString(4),minutes,kind=="Time"&&rate.HasValue?rate.Value/100m:null,amount.HasValue?amount.Value/100m:null,status));
  }return rows.AsReadOnly();
 }
 public IReadOnlyList<InvoiceCandidate> ReadInvoiceCandidates(long project,DateOnly through)
 {
  using var c=Open();using var t=c.BeginTransaction();RequireInvoices(c,t,"Billing.Invoices.View");ProjectRow(c,t,project);var policy=BillingPolicyRow(c,t,project)??throw new ArgumentException("Record reviewed billing policy first.");return BillingCandidates(c,t,project,through,policy);
 }
 private static string DraftPayloadHash(long project,string number,DateOnly date,DateOnly due,IReadOnlyList<InvoiceDraftLine> lines)
 =>HashProposal(JsonSerializer.Serialize(new{Format="CRC-INV-DRAFT-1",Project=project,Number=number,Date=date,Due=due,Currency="USD",Lines=lines.Select(l=>new{l.Kind,l.SourceId,l.Description,l.Minutes,RateCents=l.BillingRate.HasValue?(long?)BillingCents(l.BillingRate.Value):null,AmountCents=BillingCents(l.Amount,true)}).ToArray()}));
 private const string InvoiceProjection="SELECT d.id,d.project_id,d.draft_no,d.invoice_date,d.due_date,d.total_cents,coalesce(x.decision,'Draft'),d.payload_hash,EXISTS(SELECT 1 FROM approved_invoice_holds h WHERE h.draft_id=d.id) FROM invoice_drafts d LEFT JOIN invoice_draft_decisions x ON x.draft_id=d.id";
 private static InvoiceDraftSummary InvoiceRow(SqliteDataReader r)=>new(r.GetInt64(0),r.GetInt64(1),r.GetString(2),ReadPlanDate(r.GetString(3)),ReadPlanDate(r.GetString(4)),r.GetInt64(5)/100m,r.GetString(6),r.GetString(7),r.GetBoolean(8));
 private static InvoiceDraftDetail InvoiceDetail(SqliteConnection c,SqliteTransaction t,long project,long id)
 {
  InvoiceDraftSummary summary;
  using(var q=Command(c,t,InvoiceProjection+" WHERE d.project_id=$p AND d.id=$i",("$p",project),("$i",id)))using(var r=q.ExecuteReader()){if(!r.Read())throw new ArgumentException("Draft unavailable.");summary=InvoiceRow(r);}
  using var q2=Command(c,t,"SELECT line_type,source_id,description,minutes,rate_cents,amount_cents FROM invoice_draft_lines WHERE draft_id=$i ORDER BY id",("$i",id));using var r2=q2.ExecuteReader();var lines=new List<InvoiceDraftLine>();while(r2.Read())lines.Add(new(r2.GetString(0),r2.IsDBNull(1)?null:r2.GetInt64(1),r2.GetString(2),r2.IsDBNull(3)?null:r2.GetInt32(3),r2.IsDBNull(4)?null:r2.GetInt64(4)/100m,r2.GetInt64(5)/100m));
  if(lines.Count==0||lines.Sum(x=>x.Amount)!=summary.Total||DraftPayloadHash(project,summary.Number,summary.InvoiceDate,summary.DueDate,lines)!=summary.PayloadHash)throw new InvalidOperationException("Draft integrity review failed.");
  return new(summary,lines.AsReadOnly());
 }
 public IReadOnlyList<InvoiceDraftSummary> ReadInvoiceDrafts(long project)
 {
  using var c=Open();using var t=c.BeginTransaction();RequireInvoices(c,t,"Billing.Invoices.View");ProjectRow(c,t,project);using var q=Command(c,t,InvoiceProjection+" WHERE d.project_id=$p ORDER BY d.id DESC LIMIT 501",("$p",project));using var r=q.ExecuteReader();var rows=new List<InvoiceDraftSummary>();while(r.Read()){if(rows.Count==500)throw new ArgumentException("Invoice register exceeds development limit.");rows.Add(InvoiceRow(r));}return rows.AsReadOnly();
 }
 public InvoiceDraftDetail ReadInvoiceDraft(long project,long id)
 {
  using var c=Open();using var t=c.BeginTransaction();RequireInvoices(c,t,"Billing.Invoices.View");ProjectRow(c,t,project);return InvoiceDetail(c,t,project,id);
 }
 public long CreateInvoiceDraft(InvoiceDraftInput input)
 {
  using var c=Open();using var t=c.BeginTransaction(deferred:false);var actor=RequireInvoices(c,t,"Billing.Invoices.Edit");var project=ProjectRow(c,t,input.ProjectId);
  if(project.State!="Active"||input.OperationKey==Guid.Empty||input.FixedFees==null||input.Sources==null||input.FixedFees.Count>20||input.Sources.Count>200||input.Sources.Distinct().Count()!=input.Sources.Count)throw new ArgumentException("Valid active project and bounded distinct draft sources required.");
  DateRange(input.InvoiceDate,input.DueDate,366);
  var fixedFees=input.FixedFees.Select(x=>new FixedFeeInput(Validation.Text(x.Description,1000,"Fee description",true),BillingCents(x.Amount,true)/100m)).ToArray();var sources=input.Sources.OrderBy(x=>x).ToArray();
  var inputHash=HashProposal(JsonSerializer.Serialize(new{input.ProjectId,input.InvoiceDate,input.DueDate,FixedFees=fixedFees,Sources=sources}));
  var key=input.OperationKey.ToString("D");
  using(var q=Command(c,t,"SELECT id,project_id,input_hash FROM invoice_drafts WHERE operation_key=$k",("$k",key)))using(var r=q.ExecuteReader()){if(r.Read()){if(r.GetInt64(1)!=input.ProjectId||r.GetString(2)!=inputHash)throw new ArgumentException("Draft operation conflict.");return r.GetInt64(0);}}
  var policy=BillingPolicyRow(c,t,input.ProjectId)??throw new ArgumentException("Record billing policy first.");
  if(policy.Mode=="Hourly"&&fixedFees.Length>0)throw new ArgumentException("Hourly policy does not allow fixed fee lines.");
  var lines=fixedFees.Select(x=>new InvoiceDraftLine("FixedFee",null,x.Description,null,null,x.Amount)).ToList();
  var available=BillingCandidates(c,t,input.ProjectId,input.InvoiceDate,policy).ToDictionary(x=>x.SourceId);
  foreach(var source in sources){if(!available.TryGetValue(source,out var candidate)||candidate.Status!="Eligible"||!candidate.Amount.HasValue)throw new ArgumentException("Billing source unavailable or ineligible.");lines.Add(new(candidate.Kind,source,candidate.Description,candidate.Minutes,candidate.BillingRate,candidate.Amount.Value));}
  if(lines.Count==0)throw new ArgumentException("Select at least one eligible line.");
  var total=BillingCents(lines.Sum(x=>x.Amount),true);var fixedAmount=BillingCents(fixedFees.Sum(x=>x.Amount));
  var oldFixed=Convert.ToInt64(Scalar(c,t,"SELECT coalesce(sum(l.amount_cents),0) FROM invoice_draft_lines l JOIN invoice_drafts d ON d.id=l.draft_id LEFT JOIN invoice_draft_decisions x ON x.draft_id=d.id WHERE d.project_id=$p AND l.line_type='FixedFee' AND coalesce(x.decision,'Draft')!='Cancelled'",("$p",input.ProjectId)));
  var oldTotal=Convert.ToInt64(Scalar(c,t,"SELECT coalesce(sum(d.total_cents),0) FROM invoice_drafts d LEFT JOIN invoice_draft_decisions x ON x.draft_id=d.id WHERE d.project_id=$p AND coalesce(x.decision,'Draft')!='Cancelled'",("$p",input.ProjectId)));
  if(oldFixed+fixedAmount>BillingCents(policy.FixedLimit)||policy.TotalLimit.HasValue&&oldTotal+total>BillingCents(policy.TotalLimit.Value))throw new ArgumentException("Contractual billing limit exceeded.");
  var number=NextNumber(c,t,"InvoiceDraft","DRAFT");var hash=DraftPayloadHash(input.ProjectId,number,input.InvoiceDate,input.DueDate,lines);
  Exec(c,t,"INSERT INTO invoice_drafts(project_id,operation_key,draft_no,invoice_date,due_date,input_hash,payload_hash,total_cents,created_by,created_utc) VALUES($p,$k,$n,$d,$u,$i,$h,$s,$a,$t)",("$p",input.ProjectId),("$k",key),("$n",number),("$d",PlanDate(input.InvoiceDate)),("$u",PlanDate(input.DueDate)),("$i",inputHash),("$h",hash),("$s",total),("$a",actor),("$t",UtcNow()));var id=Last(c,t);
  foreach(var l in lines)Exec(c,t,"INSERT INTO invoice_draft_lines(draft_id,source_id,line_type,description,minutes,rate_cents,amount_cents) VALUES($d,$s,$k,$n,$m,$r,$a)",("$d",id),("$s",l.SourceId),("$k",l.Kind),("$n",l.Description),("$m",l.Minutes),("$r",l.BillingRate.HasValue?BillingCents(l.BillingRate.Value):null),("$a",BillingCents(l.Amount,true)));
  Audit(c,t,actor,"Create","InvoiceDraft",id);t.Commit();return id;
 }
 public string DecideInvoiceDraft(long project,long id,string expectedHash,bool approve,string note,bool confirmOwnerException=false)
 {
  using var c=Open();using var t=c.BeginTransaction(deferred:false);var actor=RequireInvoices(c,t,approve?"Billing.Invoices.Approve":"Billing.Invoices.Edit");ProjectRow(c,t,project);
  var draft=InvoiceDetail(c,t,project,id);if(draft.Summary.PayloadHash!=expectedHash)throw new ArgumentException("Draft changed or wrong selection.");
  if(draft.Summary.State!="Draft")return "Already"+draft.Summary.State;
  long? exception=null;if(approve&&Equals(Scalar(c,t,"SELECT created_by FROM invoice_drafts WHERE id=$i",("$i",id)),actor))exception=RequireOwnerException(c,t,actor,confirmOwnerException);
  var n=Validation.Text(note,1000,"Decision evidence",true);var state=approve?"Approved":"Cancelled";
  Exec(c,t,"INSERT INTO invoice_draft_decisions(draft_id,decision,note,actor_id,decided_utc,owner_exception_event_id) VALUES($i,$s,$n,$a,$u,$x)",("$i",id),("$s",state),("$n",n),("$a",actor),("$u",UtcNow()),("$x",exception));Audit(c,t,actor,exception.HasValue?"OwnerSelfApprove":state,"InvoiceDraft",id);t.Commit();return state;
 }
}
