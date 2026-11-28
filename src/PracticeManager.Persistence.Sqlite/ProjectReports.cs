using Microsoft.Data.Sqlite;
using PracticeManager.Core;
namespace PracticeManager.Persistence;
public sealed record ProjectLifecycleEvent(long Id,long ProjectId,string State,string Note,long ActorId,string RecordedUtc);
public sealed record ProjectLifecycleReview(ProjectCloseoutReview Snapshot,IReadOnlyList<ProjectLifecycleEvent> History);
public sealed partial class LocalPractice
{
 private static string OperationalState(SqliteConnection c,SqliteTransaction t,long project)=>Scalar(c,t,"SELECT state FROM project_operational_state WHERE project_id=$p",("$p",project)) as string??throw new ArgumentException("Project unavailable.");
 private static void RequireOpenOperations(SqliteConnection c,SqliteTransaction t,long project)
 {if(OperationalState(c,t,project)=="OperationalClosed")throw new ArgumentException("Operationally closed project requires explicit reopening.");}
 public IReadOnlyList<ProjectLifecycleEvent> ReadProjectLifecycle(long project)
 {
  using var c=Open();using var t=c.BeginTransaction();RequireReport(c,t,"Reports.Operations");Require(c,t,"Projects.Closeout");ProjectRow(c,t,project);
  return LifecycleHistory(c,t,project);
 }
 public ProjectLifecycleReview ReadProjectLifecycleReview(long project)
 {
  using var c=Open();using var t=c.BeginTransaction(deferred:true);
  RequireReport(c,t,"Reports.Operations");Require(c,t,"Projects.Closeout");
  return new(CloseoutSnapshot(c,t,project),LifecycleHistory(c,t,project));
 }
 private static IReadOnlyList<ProjectLifecycleEvent> LifecycleHistory(SqliteConnection c,SqliteTransaction t,long project)
 {
  using var q=Command(c,t,"SELECT id,project_id,state,note,actor_id,recorded_utc FROM project_lifecycle_events WHERE project_id=$p ORDER BY id DESC LIMIT 501",("$p",project));using var r=q.ExecuteReader();
  var rows=new List<ProjectLifecycleEvent>();while(r.Read()){if(rows.Count==500)throw new ArgumentException("Lifecycle history exceeds development limit.");rows.Add(new(r.GetInt64(0),r.GetInt64(1),r.GetString(2),r.GetString(3),r.GetInt64(4),r.GetString(5)));}return rows.AsReadOnly();
 }
 public ProjectLifecycleEvent RecordProjectLifecycle(long project,long? expectedEvent,bool close,string note,bool confirmed)
 {
  using var c=Open();using var t=c.BeginTransaction(deferred:false);RequireReport(c,t,"Reports.Operations");var actor=Require(c,t,"Projects.Closeout");var p=ProjectRow(c,t,project);
  if(!confirmed)throw new ArgumentException("Explicit operational lifecycle confirmation required.");
  var reason=Validation.Text(note,1000,"Lifecycle evidence",true);
  var latest=Scalar(c,t,"SELECT id FROM project_lifecycle_events WHERE project_id=$p ORDER BY id DESC LIMIT 1",("$p",project)) as long?;
  if(latest!=expectedEvent)throw new ArgumentException("Lifecycle changed. Refresh before retrying.");
  var snapshot=CloseoutSnapshot(c,t,project);
  if(p.State!="Active"||snapshot.ProjectState!=(close?"Active":"OperationalClosed"))throw new ArgumentException("Current authorized project and valid transition required.");
  var a=CurrentAgreement(c,t,project);
  if(a==null||a.IssuedHash!=p.ProposalHash||!AgreementIntact(c,t,a)||Convert.ToInt64(Scalar(c,t,"SELECT count(*) FROM project_start_authorizations WHERE project_id=$p AND agreement_id=$a",("$p",project),("$a",a.Id)))!=1)throw new ArgumentException("Intact start authorization evidence required.");
  if(snapshot.ConflictStatus!="Clear")throw new ArgumentException("Current conflict clearance required.");
  if(close&&snapshot.HasUnresolvedOperationalItems)throw new ArgumentException("Complete setup and resolve all outstanding operational items before closing.");
  if(!close)ActiveClient(c,t,p.Client);
  var state=close?"OperationalClosed":"Active";var utc=UtcNow();
  Exec(c,t,"INSERT INTO project_lifecycle_events(project_id,state,note,actor_id,recorded_utc) VALUES($p,$s,$n,$a,$u)",("$p",project),("$s",state),("$n",reason),("$a",actor),("$u",utc));
  var id=Last(c,t);Audit(c,t,actor,close?"OperationalClose":"OperationalReopen","Project",project);t.Commit();return new(id,project,state,reason,actor,utc);
 }
 public (bool Operations,bool Financial) ReadProjectReportAccess()
 {
  using var c=Open();using var t=c.BeginTransaction(deferred:true);
  bool Allowed(params string[] grants){try{RequireProjectView(c,t);foreach(var grant in grants)Require(c,t,grant);return true;}catch(UnauthorizedAccessException){return false;}}
  return (Allowed("Reports.Operations"),Allowed("Reports.Financial","Billing.Invoices.View","Billing.Rates"));
 }
 private long RequireReport(SqliteConnection c,SqliteTransaction t,string grant)
 {RequireProjectView(c,t);return Require(c,t,grant);}
 public ProjectCloseoutReview ReadProjectCloseoutReview(long project)
 {
  using var c=Open();using var t=c.BeginTransaction();RequireReport(c,t,"Reports.Operations");return CloseoutSnapshot(c,t,project);
 }
 private static ProjectCloseoutReview CloseoutSnapshot(SqliteConnection c,SqliteTransaction t,long project)
 {
  var p=ProjectRow(c,t,project);
  var setup=SetupReview(c,t,project).State;
  var conflict=ConflictCleared(c,t,project)?"Clear":CurrentConflict(c,t,project)==null?"NotReviewed":"Hold";
  int Count(string sql)=>Convert.ToInt32(Scalar(c,t,sql,("$p",project)));
  var phases=Count("SELECT count(*) FROM project_phases WHERE project_id=$p");
  var tasks=Count("SELECT count(*) FROM project_tasks w JOIN project_phases h ON h.id=w.phase_id WHERE h.project_id=$p AND w.state!='Completed' AND NOT EXISTS(SELECT 1 FROM task_cancellations z WHERE z.task_id=w.id) AND NOT EXISTS(SELECT 1 FROM task_discontinuations d WHERE d.task_id=w.id)");
  var quality=Count("SELECT count(*) FROM project_quality_items i WHERE i.project_id=$p AND NOT EXISTS(SELECT 1 FROM project_quality_closures x WHERE x.item_id=i.id)");
  var scope=Count("SELECT count(*) FROM project_scope_changes s WHERE s.project_id=$p AND NOT EXISTS(SELECT 1 FROM project_scope_decisions d WHERE d.change_id=s.id)");
  int Pending(string kind)=>Convert.ToInt32(Scalar(c,t,"SELECT count(*) FROM work_submissions s JOIN project_tasks w ON w.id=s.task_id JOIN project_phases h ON h.id=w.phase_id WHERE h.project_id=$p AND s.kind=$k AND NOT EXISTS(SELECT 1 FROM work_reviews r WHERE r.submission_id=s.id)",("$p",project),("$k",kind)));
  return new(p.Number,OperationalState(c,t,project),setup,conflict,phases,tasks,quality,scope,Pending("Time"),Pending("Expense"));
 }
 public ProjectOperationsReport ReadProjectOperations(long project,DateOnly start,DateOnly end)
 {
  using var c=Open();using var t=c.BeginTransaction(deferred:true);RequireReport(c,t,"Reports.Operations");return ReadProjectOperationsCore(c,t,project,start,end);
 }
 private static ProjectOperationsReport ReadProjectOperationsCore(SqliteConnection c,SqliteTransaction t,long project,DateOnly start,DateOnly end)
 {
  var p=ProjectRow(c,t,project);DateRange(start,end,366);
  var range=new (string,object?)[]{("$p",project),("$s",PlanDate(start)),("$e",PlanDate(end))};
  int Count(string sql)=>Convert.ToInt32(Scalar(c,t,sql,range));
  var phaseCount=Count("SELECT count(*) FROM project_phases WHERE project_id=$p AND start_on<=$e AND end_on>=$s");
  int planned=0,started=0,complete=0,cancelled=0,discontinued=0;long estimate=0;
  using(var q=Command(c,t,"SELECT CASE WHEN z.task_id IS NOT NULL THEN 'Cancelled' WHEN d.task_id IS NOT NULL THEN 'Discontinued' ELSE w.state END AS effective_state,count(*),coalesce(sum(CASE WHEN z.task_id IS NULL AND d.task_id IS NULL THEN w.estimate_minutes ELSE 0 END),0) FROM project_tasks w JOIN project_phases h ON h.id=w.phase_id LEFT JOIN task_cancellations z ON z.task_id=w.id LEFT JOIN task_discontinuations d ON d.task_id=w.id WHERE h.project_id=$p AND w.start_on<=$e AND w.end_on>=$s GROUP BY effective_state",range))using(var r=q.ExecuteReader()){while(r.Read()){var n=r.GetInt32(1);switch(r.GetString(0)){case "Planned":planned=n;break;case "InProgress":started=n;break;case "Completed":complete=n;break;case "Cancelled":cancelled=n;break;case "Discontinued":discontinued=n;break;}estimate+=r.GetInt64(2);}}
  long submitted=0,approved=0,rejected=0,pending=0,invalidated=0;
  using(var q=Command(c,t,"SELECT CASE WHEN EXISTS(SELECT 1 FROM work_invalidations z WHERE z.submission_id=s.id) THEN 'Invalidated' ELSE coalesce(r.decision,'Submitted') END,coalesce(sum(s.minutes),0) FROM work_submissions s JOIN project_tasks w ON w.id=s.task_id JOIN project_phases h ON h.id=w.phase_id LEFT JOIN work_reviews r ON r.submission_id=s.id WHERE h.project_id=$p AND s.kind='Time' AND s.work_date BETWEEN $s AND $e GROUP BY CASE WHEN EXISTS(SELECT 1 FROM work_invalidations z WHERE z.submission_id=s.id) THEN 'Invalidated' ELSE coalesce(r.decision,'Submitted') END",range))using(var r=q.ExecuteReader()){while(r.Read()){var n=r.GetInt64(1);submitted+=n;switch(r.GetString(0)){case "Approved":approved=n;break;case "Rejected":rejected=n;break;case "Invalidated":invalidated=n;break;case "Submitted":pending=n;break;default:throw new InvalidOperationException("Unknown time review status.");}}}
  var expenses=Count("SELECT count(*) FROM work_submissions s JOIN project_tasks w ON w.id=s.task_id JOIN project_phases h ON h.id=w.phase_id WHERE h.project_id=$p AND s.kind='Expense' AND s.work_date BETWEEN $s AND $e");
  var invalidatedExpenses=Count("SELECT count(*) FROM work_submissions s JOIN project_tasks w ON w.id=s.task_id JOIN project_phases h ON h.id=w.phase_id JOIN work_invalidations z ON z.submission_id=s.id WHERE h.project_id=$p AND s.kind='Expense' AND s.work_date BETWEEN $s AND $e");
  var quality=Convert.ToInt32(Scalar(c,t,"SELECT count(*) FROM project_quality_items i WHERE i.project_id=$p AND NOT EXISTS(SELECT 1 FROM project_quality_closures x WHERE x.item_id=i.id)",("$p",project)));
  return new(p.Number,start,end,phaseCount,planned,started,complete,estimate,submitted,approved,rejected,pending,expenses,quality,cancelled,discontinued,invalidated,invalidatedExpenses);
 }
 public ProjectFinancialReport ReadProjectFinancialReport(long project,DateOnly start,DateOnly end)
 {
  using var c=Open();using var t=c.BeginTransaction(deferred:true);RequireReport(c,t,"Reports.Financial");Require(c,t,"Billing.Invoices.View");Require(c,t,"Billing.Rates");return ReadProjectFinancialCore(c,t,project,start,end);
 }
 private static ProjectFinancialReport ReadProjectFinancialCore(SqliteConnection c,SqliteTransaction t,long project,DateOnly start,DateOnly end)
 {
  var p=ProjectRow(c,t,project);DateRange(start,end,366);
  var range=new (string,object?)[]{("$p",project),("$s",PlanDate(start)),("$e",PlanDate(end))};
  long minutes=0,billable=0,nonbillable=0,unclassified=0;decimal cost=0,expense=0;int missing=0;
  using(var q=Command(c,t,@"SELECT s.kind,s.minutes,s.amount_cents,coalesce(b.classification,'Unclassified'),(SELECT z.cost_cents FROM employee_rates z WHERE z.employee_id=s.employee_id AND z.effective_on<=s.work_date ORDER BY z.effective_on DESC LIMIT 1) FROM work_submissions s JOIN work_reviews r ON r.submission_id=s.id JOIN project_tasks w ON w.id=s.task_id JOIN project_phases h ON h.id=w.phase_id LEFT JOIN work_billing_classifications b ON b.submission_id=s.id WHERE h.project_id=$p AND r.decision='Approved' AND NOT EXISTS(SELECT 1 FROM work_invalidations z WHERE z.submission_id=s.id) AND s.work_date BETWEEN $s AND $e",range))using(var r=q.ExecuteReader()){
   while(r.Read()){
    if(r.GetString(0)=="Expense"){expense+=r.GetInt64(2)/100m;continue;}
    var m=r.GetInt64(1);if(m is <1 or >1440)throw new InvalidOperationException("Invalid approved minutes.");minutes+=m;switch(r.GetString(3)){case "Billable":billable+=m;break;case "Nonbillable":nonbillable+=m;break;case "Unclassified":unclassified+=m;break;default:throw new InvalidOperationException("Unknown billing classification.");}
    if(r.IsDBNull(4))missing++;else cost+=ReportLaborCost(m,r.GetInt64(4));
   }
  }
  decimal draft=0,ready=0,cancelled=0;
  using(var q=Command(c,t,"SELECT coalesce(x.decision,'Draft'),coalesce(sum(d.total_cents),0) FROM invoice_drafts d LEFT JOIN invoice_draft_decisions x ON x.draft_id=d.id WHERE d.project_id=$p AND d.invoice_date BETWEEN $s AND $e GROUP BY coalesce(x.decision,'Draft')",range))using(var r=q.ExecuteReader()){while(r.Read()){var n=r.GetInt64(1)/100m;switch(r.GetString(0)){case "Draft":draft=n;break;case "Approved":ready=n;break;case "Cancelled":cancelled=n;break;default:throw new InvalidOperationException("Unknown invoice draft decision.");}}}
  decimal held=0;int heldCount=0;
  using(var q=Command(c,t,"SELECT coalesce(sum(d.total_cents),0),count(*) FROM invoice_drafts d JOIN current_approved_invoice_holds h ON h.draft_id=d.id WHERE d.project_id=$p AND d.invoice_date BETWEEN $s AND $e",range))using(var r=q.ExecuteReader()){if(r.Read()){held=r.GetInt64(0)/100m;heldCount=r.GetInt32(1);}}
  if(minutes!=billable+nonbillable+unclassified||held<0||held>ready)throw new InvalidOperationException("Project financial report does not reconcile.");
  return new(p.Number,start,end,minutes,billable,nonbillable,unclassified,missing==0?cost:null,missing,expense,draft,ready,cancelled,held,heldCount);
 }
 public string PrepareProjectOperationsCsv(long project,DateOnly start,DateOnly end)
 {
  using var c=Open();using var t=c.BeginTransaction(deferred:false);var actor=RequireReport(c,t,"Reports.Operations");
  var r=ReadProjectOperationsCore(c,t,project,start,end);
  var csv=SafeCsv.Build(new IReadOnlyList<string?>[]{
   new[]{"PROJECT OPERATIONS — NOT AN ACCOUNTING STATEMENT"},
   new[]{"Project","Start (inclusive)","End (inclusive)"},new[]{r.ProjectNumber,SafeCsv.Date(start),SafeCsv.Date(end)},
   new[]{"Metric","Value"},
   new[]{"Overlapping phases",SafeCsv.Number(r.Phases)},new[]{"Planned tasks",SafeCsv.Number(r.PlannedTasks)},new[]{"In progress tasks",SafeCsv.Number(r.StartedTasks)},new[]{"Completed tasks",SafeCsv.Number(r.CompletedTasks)},
   new[]{"Cancelled tasks",SafeCsv.Number(r.CancelledTasks)},new[]{"Discontinued tasks",SafeCsv.Number(r.DiscontinuedTasks)},
   new[]{"Estimated minutes (overlapping tasks, not prorated, excludes cancelled/discontinued)",SafeCsv.Number(r.EstimatedMinutes)},
   new[]{"Submitted minutes (all states)",SafeCsv.Number(r.SubmittedMinutes)},new[]{"Approved minutes",SafeCsv.Number(r.ApprovedMinutes)},new[]{"Pending minutes",SafeCsv.Number(r.PendingMinutes)},new[]{"Rejected minutes",SafeCsv.Number(r.RejectedMinutes)},new[]{"Invalidated minutes",SafeCsv.Number(r.InvalidatedMinutes)},
   new[]{"Expense submissions (all)",SafeCsv.Number(r.ExpenseSubmissions)},new[]{"Invalidated expense submissions",SafeCsv.Number(r.InvalidatedExpenseSubmissions)},new[]{"Current open QA items (all dates)",SafeCsv.Number(r.OpenQualityItems)}
  });
  Audit(c,t,actor,"PrepareProjectOperationsExport","Project",project);t.Commit();return csv;
 }
 public string PrepareProjectFinancialCsv(long project,DateOnly start,DateOnly end)
 {
  using var c=Open();using var t=c.BeginTransaction(deferred:false);var actor=RequireReport(c,t,"Reports.Financial");Require(c,t,"Billing.Invoices.View");Require(c,t,"Billing.Rates");
  var r=ReadProjectFinancialCore(c,t,project,start,end);
  var csv=SafeCsv.Build(new IReadOnlyList<string?>[]{
   new[]{"RESTRICTED PROJECT FINANCIAL PREVIEW — NOT POSTED REVENUE, PROFIT OR RECEIVABLES"},
   new[]{"Project","Start (inclusive)","End (inclusive)","Currency"},new[]{r.ProjectNumber,SafeCsv.Date(start),SafeCsv.Date(end),"USD"},
   new[]{"Metric","Value"},
   new[]{"Approved minutes",SafeCsv.Number(r.ApprovedMinutes)},new[]{"Billable minutes",SafeCsv.Number(r.BillableMinutes)},new[]{"Nonbillable minutes",SafeCsv.Number(r.NonbillableMinutes)},new[]{"Unclassified minutes",SafeCsv.Number(r.UnclassifiedMinutes)},
   new[]{"Estimated labor cost (blank if incomplete)",SafeCsv.Amount(r.EstimatedLaborCost)},new[]{"Missing cost rate records",SafeCsv.Number(r.MissingCostRates)},new[]{"Approved expense cost",SafeCsv.Amount(r.ApprovedExpenseCost)},new[]{"Total operational cost (blank if incomplete)",SafeCsv.Amount(r.TotalOperationalCost)},
   new[]{"Unapproved internal draft value",SafeCsv.Amount(r.DraftValue)},new[]{"Approved internal draft value",SafeCsv.Amount(r.ApprovedInternalDraftValue)},new[]{"Held approved draft value (included in approved total)",SafeCsv.Amount(r.HeldApprovedDraftValue)},new[]{"Held approved draft count",SafeCsv.Number(r.HeldApprovedDraftCount)},new[]{"Cancelled draft value",SafeCsv.Amount(r.CancelledDraftValue)}
  });
  Audit(c,t,actor,"PrepareProjectFinancialExport","Project",project);t.Commit();return csv;
 }
 public string ExportInvoiceDraftCsv(long project,long id,string expectedHash)
 {
  using var c=Open();using var t=c.BeginTransaction(deferred:false);var actor=RequireInvoices(c,t,"Billing.Invoices.Export");var p=ProjectRow(c,t,project);var d=InvoiceDetail(c,t,project,id);
  if(d.Summary.PayloadHash!=expectedHash)throw new ArgumentException("Draft identity changed.");
  if(d.Summary.State!="Approved"||d.Summary.ReconciliationHold)throw new ArgumentException("Only approved internal drafts without reconciliation hold may be exported for manual accounting preparation.");
  var rows=new List<IReadOnlyList<string?>>{new[]{"INTERNAL DRAFT ONLY — NOT AN ISSUED INVOICE"},new[]{"Project","Draft reference","Draft date","Due date","State","Currency","Total","Payload SHA256"},new[]{p.Number,d.Summary.Number,SafeCsv.Date(d.Summary.InvoiceDate),SafeCsv.Date(d.Summary.DueDate),d.Summary.State,"USD",SafeCsv.Amount(d.Summary.Total),d.Summary.PayloadHash},new[]{"Kind","Source","Description","Minutes","Billing USD/hour","Amount USD"}};
  rows.AddRange(d.Lines.Select(l=>(IReadOnlyList<string?>)new[]{l.Kind,SafeCsv.Number(l.SourceId),l.Description,SafeCsv.Number(l.Minutes),SafeCsv.Amount(l.BillingRate),SafeCsv.Amount(l.Amount)}));
  var csv=SafeCsv.Build(rows);Audit(c,t,actor,"PrepareManualExport","InvoiceDraft",id);t.Commit();return csv;
 }
}
