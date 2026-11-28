using Microsoft.Data.Sqlite;
using PracticeManager.Core;
namespace PracticeManager.Persistence;
public sealed partial class LocalPractice
{
 private static decimal ReportLaborCost(long minutes,long cents)
 {
  if(minutes is <1 or >1440)throw new InvalidOperationException("Invalid approved minutes.");
  if(cents<0||cents>100000000)throw new InvalidOperationException("Invalid recorded cost rate.");
  return decimal.Round(minutes*cents/60m,0,MidpointRounding.AwayFromZero)/100m;
 }
 public bool CanReadFirmLaborCost()=>CanReadFirmTimeMix()&&Can("Billing.Rates");
 public FirmLaborCost ReadFirmLaborCost(DateOnly start,DateOnly end)
 {
  using var c=Open();using var t=c.BeginTransaction();RequireWorkReview(c,t,"Billing.Classify");Require(c,t,"Reports.Financial");RequireRates(c,t);DateRange(start,end,366);
  using var q=Command(c,t,@"WITH facts AS (
   SELECT s.employee_id,s.work_date,s.minutes,CASE coalesce(b.classification,'Unclassified') WHEN 'Billable' THEN 'Project billable' WHEN 'Nonbillable' THEN 'Project nonbillable' ELSE 'Project unclassified' END category
   FROM work_submissions s JOIN work_reviews r ON r.submission_id=s.id LEFT JOIN work_billing_classifications b ON b.submission_id=s.id WHERE s.kind='Time' AND r.decision='Approved' AND NOT EXISTS(SELECT 1 FROM work_invalidations z WHERE z.submission_id=s.id) AND s.work_date BETWEEN $s AND $e
   UNION ALL SELECT s.employee_id,s.work_date,s.minutes,'Overhead: '||s.category FROM overhead_time s JOIN overhead_reviews r ON r.submission_id=s.id WHERE r.decision='Approved' AND NOT EXISTS(SELECT 1 FROM overhead_invalidations z WHERE z.submission_id=s.id) AND s.work_date BETWEEN $s AND $e
  ) SELECT f.category,f.minutes,(SELECT z.cost_cents FROM employee_rates z WHERE z.employee_id=f.employee_id AND z.effective_on<=f.work_date ORDER BY z.effective_on DESC LIMIT 1) FROM facts f LIMIT 100001",("$s",PlanDate(start)),("$e",PlanDate(end)));
  using var r=q.ExecuteReader();var rows=new Dictionary<string,FirmLaborCostRow>(StringComparer.Ordinal);int count=0;
  while(r.Read()){
   if(++count>100000)throw new ArgumentException("Narrow labor cost report date range.");
   var cat=r.GetString(0);var minutes=r.GetInt64(1);
   if(minutes is <1 or >1440)throw new InvalidOperationException("Invalid approved minutes.");
   var row=rows.TryGetValue(cat,out var found)?found:new FirmLaborCostRow(cat,0,0,0,0);
   if(r.IsDBNull(2))row=row with{ApprovedMinutes=row.ApprovedMinutes+minutes,MissingRateMinutes=row.MissingRateMinutes+minutes,MissingRateRecords=row.MissingRateRecords+1};
   else{
    var cost=ReportLaborCost(minutes,r.GetInt64(2));
    row=row with{ApprovedMinutes=row.ApprovedMinutes+minutes,KnownCost=row.KnownCost+cost};
   }
   rows[cat]=row;
  }
  return new(start,end,Array.AsReadOnly(rows.Values.OrderBy(x=>x.Category,StringComparer.Ordinal).ToArray()));
 }
}
