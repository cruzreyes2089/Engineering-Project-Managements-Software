using System.Globalization;
namespace PracticeManager.Core;

// Explicit, temporary planning inputs; no HR record, leave approval or persistent policy.
public sealed record EmployeeCapacityWeek(string EmployeeNumber,int Monday,int Tuesday,int Wednesday,int Thursday,int Friday,int Saturday,int Sunday)
{
 internal int Minutes(DayOfWeek day)=>day switch
 {
  DayOfWeek.Monday=>Monday,DayOfWeek.Tuesday=>Tuesday,DayOfWeek.Wednesday=>Wednesday,
  DayOfWeek.Thursday=>Thursday,DayOfWeek.Friday=>Friday,DayOfWeek.Saturday=>Saturday,DayOfWeek.Sunday=>Sunday,
  _=>throw new ArgumentException("Invalid weekday.")
 };
}
public sealed record EmployeeCapacityException(string EmployeeNumber,DateOnly Date,int Minutes);
public sealed record StaffCapacityCalendar(IReadOnlyList<EmployeeCapacityWeek> Weeks,IReadOnlyList<DateOnly> Closures,
 IReadOnlyList<EmployeeCapacityException> Exceptions)
{
 // Exact small line format for the scenario editor. No file paths or external imports.
 public static StaffCapacityCalendar Parse(string weeks,string closures,string exceptions)
 {
  static string[] Lines(string value)
  {
   if(value.Length>100000)throw new ArgumentException("Capacity scenario input exceeds limit.");
   return value.Replace("\r\n","\n").Split('\n',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries);
  }
  static string[] Fields(string line,int count)
  {
   var parts=line.Split(',',StringSplitOptions.TrimEntries);
   if(parts.Length!=count||parts.Any(string.IsNullOrEmpty))throw new ArgumentException("Invalid capacity scenario row.");
   return parts;
  }
  static int Minutes(string text)=>int.TryParse(text,NumberStyles.None,CultureInfo.InvariantCulture,out var n)&&n is >=0 and <=1440
   ?n:throw new ArgumentException("Enter whole capacity minutes from 0 to 1440.");
  static DateOnly Date(string text)=>DateOnly.TryParseExact(text,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var date)
   ?date:throw new ArgumentException("Enter calendar dates as yyyy-MM-dd.");
  var w=Lines(weeks);var h=Lines(closures);var e=Lines(exceptions);
  if(w.Length>500||h.Length>366||e.Length>2000)throw new ArgumentException("Calendar scenario row limit exceeded.");
  return new(Array.AsReadOnly(w.Select(line=>{var p=Fields(line,8);return new EmployeeCapacityWeek(p[0],Minutes(p[1]),Minutes(p[2]),Minutes(p[3]),Minutes(p[4]),Minutes(p[5]),Minutes(p[6]),Minutes(p[7]));}).ToArray()),
   Array.AsReadOnly(h.Select(Date).ToArray()),
   Array.AsReadOnly(e.Select(line=>{var p=Fields(line,3);return new EmployeeCapacityException(p[0],Date(p[1]),Minutes(p[2]));}).ToArray()));
 }
}
