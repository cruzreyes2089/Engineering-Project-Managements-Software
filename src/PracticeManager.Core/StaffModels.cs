namespace PracticeManager.Core;
public sealed record StaffAccount(long Id,string Username,string Role,bool Active,bool PasswordChangeRequired,long Revision);
public sealed record StaffRole(long Id,string Name);
public sealed record StaffGrant(string Permission,bool Allowed);
public sealed record EmployeeInput(string First,string Last,string Email,string Title,bool Active);
public sealed record EmployeeRecord(long Id,string Number,long? UserId,string First,string Last,string Email,string Title,bool Active,int Revision)
{
 public string Name=>(First+" "+Last).Trim();
}
public sealed record EmployeeLicense(long Id,long EmployeeId,string Jurisdiction,string Type,string Number,DateOnly Expires,int Revision)
{
 public string ExpiryStatus(DateOnly asOf)=>Expires<asOf?"Expired":Expires.DayNumber-asOf.DayNumber<=30?"Due within 30 days":"Current";
}
