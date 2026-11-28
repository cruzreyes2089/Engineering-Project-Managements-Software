using System.Globalization;
namespace PracticeManager.Core;
// Task estimates may exceed a single workday. Preserve exact whole minutes.
public static class TaskEstimate
{
 public static string Format(int minutes)
 {
  if(minutes is <1 or >600000)throw new ArgumentException("Invalid estimate.");
  return (minutes/60).ToString(CultureInfo.InvariantCulture)+":"+(minutes%60).ToString("00",CultureInfo.InvariantCulture);
 }
 public static int Parse(string text)
 {
  if(string.IsNullOrEmpty(text)||text.Length>20||text!=text.Trim())throw new ArgumentException("Enter total hours or hours:minutes.");
  decimal minutes;
  if(text.Contains(':'))
  {
   var p=text.Split(':');
   if(p.Length!=2||p[1].Length!=2||p.Any(x=>x.Length==0||x.Any(c=>c<'0'||c>'9'))||
      !int.TryParse(p[0],NumberStyles.None,CultureInfo.InvariantCulture,out var h)||!int.TryParse(p[1],NumberStyles.None,CultureInfo.InvariantCulture,out var m)||m>59)
    throw new ArgumentException("Enter hours:minutes (for example 2:30).");
   minutes=h*60m+m;
  }
  else
  {
   if(text.Any(c=>(c<'0'||c>'9')&&c!='.')||!decimal.TryParse(text,NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out var h)||h>10000m)
    throw new ArgumentException("Enter total hours or hours:minutes.");
   minutes=h*60m;
  }
  if(minutes<1||minutes>600000||decimal.Truncate(minutes)!=minutes)throw new ArgumentException("Estimate must be 1–600000 whole minutes. Use hours:minutes for exact durations.");
  return (int)minutes;
 }
}
