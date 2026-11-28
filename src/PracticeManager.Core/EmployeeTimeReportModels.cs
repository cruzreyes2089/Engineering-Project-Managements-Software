namespace PracticeManager.Core;

public sealed record EmployeeTimeFilter(long? EmployeeId,long? ProjectId,DateOnly Start,DateOnly End,string Status);
public sealed record EmployeeTimeDetail(string Source,long SubmissionId,long EmployeeId,string EmployeeNumber,string Employee,
 long? ProjectId,string ProjectNumber,string TaskOrCategory,DateOnly WorkDate,int Minutes,string Status)
{
 public decimal Hours=>Minutes/60m;
}
public sealed record EmployeeTimeReport(EmployeeTimeFilter Filter,IReadOnlyList<EmployeeTimeDetail> Rows,
 long ApprovedMinutes,long PendingMinutes,long RejectedMinutes,long InvalidatedMinutes)
{
 public long RecordedMinutes=>ApprovedMinutes+PendingMinutes+RejectedMinutes+InvalidatedMinutes;
 public long ApprovedProjectMinutes=>Rows.Where(x=>x.Status=="Approved"&&x.Source=="Project").Sum(x=>(long)x.Minutes);
 public long ApprovedOverheadMinutes=>Rows.Where(x=>x.Status=="Approved"&&x.Source=="Overhead").Sum(x=>(long)x.Minutes);
}
