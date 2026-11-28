using System.Globalization;
using System.Text.Json;
namespace PracticeManager.Core;
public sealed record ManagerInvoiceTerm(string Mode,DateOnly DueDate,int? NetDays=null);
public static class ManagerInvoiceTerms
{
 // Exact observed three shapes from the isolated November 2026 fixture.
 // Unsupported/ambiguous fields return null, not assumed immediate terms.
 public static ManagerInvoiceTerm? Parse(JsonElement form,DateOnly issued)
 {
  if(form.ValueKind!=JsonValueKind.Object||issued.Year<1900)return null;
  var fields=form.EnumerateObject().ToArray();
  if(fields.Select(x=>x.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=fields.Length)return null;
  var known=new HashSet<string>(StringComparer.Ordinal){"DueDate","DueDateDays","DueDateDate"};
  if(fields.Any(x=>x.Name.Contains("due",StringComparison.OrdinalIgnoreCase)&&!known.Contains(x.Name)))return null;
  var hasMode=form.TryGetProperty("DueDate",out var mode);
  var hasDays=form.TryGetProperty("DueDateDays",out var days);
  var hasDate=form.TryGetProperty("DueDateDate",out var date);
  if(!hasMode&&!hasDays&&!hasDate)return new("Immediate",issued);
  if(!hasMode&&hasDays&&!hasDate&&days.ValueKind==JsonValueKind.Number&&days.TryGetInt32(out var n)&&n>=0&&n<=3660&&issued.DayNumber<=DateOnly.MaxValue.DayNumber-n)return new("NetDays",issued.AddDays(n),n);
  if(hasMode&&!hasDays&&hasDate&&mode.ValueKind==JsonValueKind.Number&&mode.TryGetInt32(out var m)&&m==1&&date.ValueKind==JsonValueKind.String&&DateTime.TryParseExact(date.GetString(),"yyyy-MM-dd'T'HH:mm:ss",CultureInfo.InvariantCulture,DateTimeStyles.None,out var parsed)&&parsed.TimeOfDay==TimeSpan.Zero){
   var due=DateOnly.FromDateTime(parsed);if(due>=issued)return new("FixedDate",due);
  }
  return null;
 }
}
