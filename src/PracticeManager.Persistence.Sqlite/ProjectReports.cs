using Microsoft.Data.Sqlite;
using PracticeManager.Core;
namespace PracticeManager.Persistence;
public sealed partial class LocalPractice
{
 private long RequireReport(SqliteConnection c,SqliteTransaction t,string grant)
 {RequireProjectView(c,t);return Require(c,t,grant);}
 public ProjectCloseoutReview ReadProjectCloseoutReview(long project)=>throw new NotImplementedException("Closeout review not implemented.");
 public ProjectOperationsReport ReadProjectOperations(long project,DateOnly start,DateOnly end)
 {
  using var c=Open();using var t=c.BeginTransaction();RequireReport(c,t,"Reports.Operations");var p=ProjectRow(c,t,project);DateRange(start,end,366);
  var range=new (string,object?)[]{("$p",project),("$s",PlanDate(start)),("$e",PlanDate(end))};
  int Count(string sql)=>Convert.ToInt32(Scalar(c,t,sql,range));
  var phaseCount=Count("SELECT count(*) FROM project_phases WHERE project_id=$p AND start_on<=$e AND end_on>=$s");
  int planned=0,started=0,complete=0;long estimate=0;
  using(var q=Command(c,t,"SELECT w.state,count(*),coalesce(sum(w.estimate_minutes),0) FROM project_tasks w JOIN project_phases h ON h.id=w.phase_id WHERE h.project_id=$p AND w.start_on<=$e AND w.end_on>=$s GROUP BY w.state",range))using(var r=q.ExecuteReader()){while(r.Read()){var n=r.GetInt32(1);switch(r.GetString(0)){case "Planned":planned=n;break;case "InProgress":started=n;break;case "Completed":complete=n;break;}estimate+=r.GetInt64(2);}}
  long submitted=0,approved=0,rejected=0,pending=0;
  using(var q=Command(c,t,"SELECT coalesce(r.decision,'Submitted'),coalesce(sum(s.minutes),0) FROM work_submissions s JOIN project_tasks w ON w.id=s.task_id JOIN project_phases h ON h.id=w.phase_id LEFT JOIN work_reviews r ON r.submission_id=s.id WHERE h.project_id=$p AND s.kind='Time' AND s.work_date BETWEEN $s AND $e GROUP BY coalesce(r.decision,'Submitted')",range))using(var r=q.ExecuteReader()){while(r.Read()){var n=r.GetInt64(1);submitted+=n;switch(r.GetString(0)){case "Approved":approved=n;break;case "Rejected":rejected=n;break;default:pending=n;break;}}}
  var expenses=Count("SELECT count(*) FROM work_submissions s JOIN project_tasks w ON w.id=s.task_id JOIN project_phases h ON h.id=w.phase_id WHERE h.project_id=$p AND s.kind='Expense' AND s.work_date BETWEEN $s AND $e");
  var quality=Convert.ToInt32(Scalar(c,t,"SELECT count(*) FROM project_quality_items i WHERE i.project_id=$p AND NOT EXISTS(SELECT 1 FROM project_quality_closures x WHERE x.item_id=i.id)",("$p",project)));
  return new(p.Number,start,end,phaseCount,planned,started,complete,estimate,submitted,approved,rejected,pending,expenses,quality);
 }
 public ProjectFinancialReport ReadProjectFinancialReport(long project,DateOnly start,DateOnly end)
 {
  using var c=Open();using var t=c.BeginTransaction();RequireReport(c,t,"Reports.Financial");Require(c,t,"Billing.Invoices.View");Require(c,t,"Billing.Rates");var p=ProjectRow(c,t,project);DateRange(start,end,366);
  var range=new (string,object?)[]{("$p",project),("$s",PlanDate(start)),("$e",PlanDate(end))};
  long minutes=0,billable=0,nonbillable=0,unclassified=0;decimal cost=0,expense=0;int missing=0;
  using(var q=Command(c,t,@"SELECT s.kind,s.minutes,s.amount_cents,coalesce(b.classification,'Unclassified'),(SELECT z.cost_cents FROM employee_rates z WHERE z.employee_id=s.employee_id AND z.effective_on<=s.work_date ORDER BY z.effective_on DESC LIMIT 1) FROM work_submissions s JOIN work_reviews r ON r.submission_id=s.id JOIN project_tasks w ON w.id=s.task_id JOIN project_phases h ON h.id=w.phase_id LEFT JOIN work_billing_classifications b ON b.submission_id=s.id WHERE h.project_id=$p AND r.decision='Approved' AND s.work_date BETWEEN $s AND $e",range))using(var r=q.ExecuteReader()){
   while(r.Read()){
    if(r.GetString(0)=="Expense"){expense+=r.GetInt64(2)/100m;continue;}
    var m=r.GetInt64(1);minutes+=m;switch(r.GetString(3)){case "Billable":billable+=m;break;case "Nonbillable":nonbillable+=m;break;default:unclassified+=m;break;}
    if(r.IsDBNull(4))missing++;else cost+=decimal.Round(m*r.GetInt64(4)/60m,0,MidpointRounding.AwayFromZero)/100m;
   }
  }
  decimal draft=0,ready=0,cancelled=0;
  using(var q=Command(c,t,"SELECT coalesce(x.decision,'Draft'),coalesce(sum(d.total_cents),0) FROM invoice_drafts d LEFT JOIN invoice_draft_decisions x ON x.draft_id=d.id WHERE d.project_id=$p AND d.invoice_date BETWEEN $s AND $e GROUP BY coalesce(x.decision,'Draft')",range))using(var r=q.ExecuteReader()){while(r.Read()){var n=r.GetInt64(1)/100m;switch(r.GetString(0)){case "Draft":draft=n;break;case "Approved":ready=n;break;case "Cancelled":cancelled=n;break;}}}
  return new(p.Number,start,end,minutes,billable,nonbillable,unclassified,missing==0?cost:null,missing,expense,draft,ready,cancelled);
 }
 public string ExportInvoiceDraftCsv(long project,long id,string expectedHash)
 {
  using var c=Open();using var t=c.BeginTransaction(deferred:false);var actor=RequireInvoices(c,t,"Billing.Invoices.Export");var p=ProjectRow(c,t,project);var d=InvoiceDetail(c,t,project,id);
  if(d.Summary.PayloadHash!=expectedHash)throw new ArgumentException("Draft identity changed.");
  if(d.Summary.State!="Approved")throw new ArgumentException("Only approved internal drafts may be exported for manual accounting preparation.");
  var rows=new List<IReadOnlyList<string?>>{new[]{"INTERNAL DRAFT ONLY — NOT AN ISSUED INVOICE"},new[]{"Project","Draft reference","Draft date","Due date","State","Currency","Total","Payload SHA256"},new[]{p.Number,d.Summary.Number,SafeCsv.Date(d.Summary.InvoiceDate),SafeCsv.Date(d.Summary.DueDate),d.Summary.State,"USD",SafeCsv.Amount(d.Summary.Total),d.Summary.PayloadHash},new[]{"Kind","Source","Description","Minutes","Billing USD/hour","Amount USD"}};
  rows.AddRange(d.Lines.Select(l=>(IReadOnlyList<string?>)new[]{l.Kind,SafeCsv.Number(l.SourceId),l.Description,SafeCsv.Number(l.Minutes),SafeCsv.Amount(l.BillingRate),SafeCsv.Amount(l.Amount)}));
  var csv=SafeCsv.Build(rows);Audit(c,t,actor,"PrepareManualExport","InvoiceDraft",id);t.Commit();return csv;
 }
}
