namespace PracticeManager.Core;
public sealed record TimeMixRow(long EmployeeId,string EmployeeNumber,string Employee,bool Active,long BillableMinutes,long ProjectNonbillableMinutes,long UnclassifiedMinutes,long OverheadMinutes,long PendingProjectMinutes,long PendingOverheadMinutes,long RejectedProjectMinutes,long RejectedOverheadMinutes,long InvalidatedProjectMinutes=0,long InvalidatedOverheadMinutes=0)
{
 public long ApprovedMinutes=>BillableMinutes+ProjectNonbillableMinutes+UnclassifiedMinutes+OverheadMinutes;
 public decimal? ApprovedBillableShare=>ApprovedMinutes>0&&UnclassifiedMinutes==0?BillableMinutes/(decimal)ApprovedMinutes:null;
}
public sealed record FirmTimeMix(DateOnly Start,DateOnly End,IReadOnlyList<TimeMixRow> Rows)
{
 public long ApprovedBillableMinutes=>Rows.Sum(x=>x.BillableMinutes);
 public long ApprovedProjectNonbillableMinutes=>Rows.Sum(x=>x.ProjectNonbillableMinutes);
 public long ApprovedUnclassifiedMinutes=>Rows.Sum(x=>x.UnclassifiedMinutes);
 public long ApprovedOverheadMinutes=>Rows.Sum(x=>x.OverheadMinutes);
 public long ApprovedMinutes=>Rows.Sum(x=>x.ApprovedMinutes);
 public long PendingMinutes=>Rows.Sum(x=>x.PendingProjectMinutes+x.PendingOverheadMinutes);
 public long RejectedMinutes=>Rows.Sum(x=>x.RejectedProjectMinutes+x.RejectedOverheadMinutes);
 public long InvalidatedMinutes=>Rows.Sum(x=>x.InvalidatedProjectMinutes+x.InvalidatedOverheadMinutes);
 public decimal? ApprovedBillableShare=>ApprovedMinutes>0&&ApprovedUnclassifiedMinutes==0?ApprovedBillableMinutes/(decimal)ApprovedMinutes:null;
}
