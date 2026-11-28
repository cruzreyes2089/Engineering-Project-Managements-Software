using System.Globalization;
using System.Text;
namespace PracticeManager.Core;
public sealed record ProjectOperationsReport(string ProjectNumber,DateOnly Start,DateOnly End,int Phases,int PlannedTasks,int StartedTasks,int CompletedTasks,long EstimatedMinutes,long SubmittedMinutes,long ApprovedMinutes,long RejectedMinutes,long PendingMinutes,int ExpenseSubmissions,int OpenQualityItems,int CancelledTasks=0,int DiscontinuedTasks=0,long InvalidatedMinutes=0,int InvalidatedExpenseSubmissions=0);
public sealed record ProjectCloseoutReview(string ProjectNumber,string ProjectState,string SetupState,string ConflictStatus,int Phases,int OpenTasks,int OpenQualityItems,int PendingScopeDecisions,int PendingTimeSubmissions,int PendingExpenseSubmissions)
{
 public bool HasUnresolvedOperationalItems=>SetupState!="Complete"||OpenTasks!=0||OpenQualityItems!=0||PendingScopeDecisions!=0||PendingTimeSubmissions!=0||PendingExpenseSubmissions!=0;
 public bool CanRequestClose=>ProjectState=="Active"&&ConflictStatus=="Clear"&&!HasUnresolvedOperationalItems;
 public bool CanRequestReopen=>ProjectState=="OperationalClosed"&&ConflictStatus=="Clear";
}
public sealed record ProjectFinancialReport(string ProjectNumber,DateOnly Start,DateOnly End,long ApprovedMinutes,long BillableMinutes,long NonbillableMinutes,long UnclassifiedMinutes,decimal? EstimatedLaborCost,int MissingCostRates,decimal ApprovedExpenseCost,decimal DraftValue,decimal ApprovedInternalDraftValue,decimal CancelledDraftValue,decimal HeldApprovedDraftValue=0,int HeldApprovedDraftCount=0)
{
 public decimal? TotalOperationalCost=>EstimatedLaborCost.HasValue?EstimatedLaborCost.Value+ApprovedExpenseCost:null;
}
public static class SafeCsv
{
 public static string Text(string? value)
 {
  var s=value??"";
  // Protect spreadsheet readers from interpreting imported user strings as formulas.
  var first=s.TrimStart();
  if(s.Length>0&&(char.IsControl(s[0])||first.Length>0&&first[0] is '=' or '+' or '-' or '@'))s="'"+s;
  return "\""+s.Replace("\"","\"\"")+"\"";
 }
 public static string Build(IEnumerable<IReadOnlyList<string?>> rows)
 {
  var result=new StringBuilder();foreach(var row in rows)result.Append(string.Join(",",row.Select(Text))).Append("\r\n");return result.ToString();
 }
 public static string Amount(decimal? value)=>value?.ToString("0.00",CultureInfo.InvariantCulture)??"";
 public static string Number(long? value)=>value?.ToString(CultureInfo.InvariantCulture)??"";
 public static string Date(DateOnly day)=>day.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
}
