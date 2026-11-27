using System.Globalization;
using PracticeManager.Core;
namespace PracticeManager.Persistence;
public sealed partial class LocalPractice
{
 // Current recorded expiration dates for active employees only.
 // No assertion about regulatory standing, required licenses, or historic status.
 public LicenseExpiryReview ReadEmployeeLicenseExpiry(DateOnly asOf,int horizonDays=30)
 {
  using var c=Open();using var t=c.BeginTransaction();Require(c,t,"Employees.View");
  if(horizonDays is <1 or >90||asOf==DateOnly.MinValue||asOf.DayNumber>DateOnly.MaxValue.DayNumber-horizonDays)
   throw new ArgumentException("Select a valid reference date and horizon of 1–90 days.");
  var end=asOf.AddDays(horizonDays).ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
  // Validate all active license dates, not just the string-filtered result.
  // A malformed stored date must not silently disappear from the review.
  using var q=Command(c,t,@"SELECT e.id,e.employee_no,e.first_name,e.last_name,l.jurisdiction,l.license_type,l.expires_on
    FROM employee_licenses l JOIN employees e ON e.id=l.employee_id
    WHERE e.active=1
    ORDER BY l.expires_on COLLATE BINARY,e.employee_no COLLATE BINARY,l.id LIMIT 5001");
  using var r=q.ExecuteReader();var items=new List<LicenseExpiryItem>();int count=0;
  while(r.Read())
  {
   if(++count>5000)throw new ArgumentException("Active license register exceeds review limit.");
   var raw=r.GetString(6);
   if(!DateOnly.TryParseExact(raw,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var expires)||expires==DateOnly.MinValue)
    throw new ArgumentException("License register contains an invalid expiration date; review source records.");
   if(string.CompareOrdinal(raw,end)>0)continue;
   items.Add(new(r.GetInt64(0),r.GetString(1),(r.GetString(2)+" "+r.GetString(3)).Trim(),r.GetString(4),r.GetString(5),expires,expires.DayNumber-asOf.DayNumber));
  }
  return new(asOf,horizonDays,items.AsReadOnly());
 }
}
