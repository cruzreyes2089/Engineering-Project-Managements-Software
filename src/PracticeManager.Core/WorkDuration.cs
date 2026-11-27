using System.Globalization;
namespace PracticeManager.Core;

// User-facing hours/minutes entry; persisted time remains exact whole minutes.
public static class WorkDuration
{
 public static int Parse(string hours,string minutes)
 {
  static int Part(string value,int maximum)
  {
   if(value.Length==0)return 0;
   if(value.Length>2||value.Any(c=>c<'0'||c>'9')||!int.TryParse(value,NumberStyles.None,CultureInfo.InvariantCulture,out var n)||n>maximum)
    throw new ArgumentException("Enter whole hours (0–24) and minutes (0–59).");
   return n;
  }
  var total=Part(hours,24)*60+Part(minutes,59);
  if(total is <1 or >1440)throw new ArgumentException("Enter a duration from 1 minute to 24 hours.");
  return total;
 }
 public static (string Hours,string Minutes) Fields(int? total)
 {
  if(total==null)return ("","");
  if(total is <1 or >1440)throw new ArgumentException("Invalid stored duration.");
  return ((total.Value/60).ToString(CultureInfo.InvariantCulture),(total.Value%60).ToString(CultureInfo.InvariantCulture));
 }
}
