using PracticeManager.Core;
using PracticeManager.Persistence;
internal static class StaffAdministrationTests
{
 public static void Run(string path,string pw,Action<bool,string> check)
 {
  var s=new LocalPractice(path);s.Bootstrap("Synthetic Staff Firm","admin",pw,"CL");check(s.Login("admin",pw),"staff admin login");
  void Denied(Action a,string label){try{a();}catch(UnauthorizedAccessException){check(true,label);return;}throw new Exception("FAIL: "+label);}
  void Invalid(Action a,string label){try{a();}catch(ArgumentException){check(true,label);return;}throw new Exception("FAIL: "+label);}
  var role=s.ReadStaffRoles().Single(r=>r.Name=="Engineer");var admin=s.ReadStaffRoles().Single(r=>r.Name=="Administrator");
  check(s.ReadStaffGrants(role.Id).All(g=>!g.Allowed),"new non-admin role denies all grants by default");
  Invalid(()=>s.SetStaffGrant(role.Id,"Administration.Manage",true,false),"administration grant protected");
  Invalid(()=>s.SetStaffGrant(admin.Id,"Clients.View",false,true),"administrator grants protected");
  Denied(()=>new LocalPractice(path).FirmName(),"unauthenticated firm identity denied");
  var user=s.CreateStaffAccount("synthetic-engineer",role.Id,"Synthetic-temporary-0001");
  var staff=new LocalPractice(path);check(!staff.Login("synthetic-engineer","Synthetic-temporary-0001"),"temporary password cannot open session");
  check(!staff.ChangeRequiredPassword("synthetic-engineer","wrong","Synthetic-permanent-0001"),"incorrect temporary password denied");
  check(staff.ChangeRequiredPassword("synthetic-engineer","Synthetic-temporary-0001","Synthetic-permanent-0001")&&!staff.Can("Clients.View"),"required password change creates no session");
  check(staff.Login("synthetic-engineer","Synthetic-permanent-0001"),"new password authenticates");
  check(staff.FirmName()=="Synthetic Staff Firm","authenticated staff may read firm identity without domain grants");
  Denied(()=>staff.ReadStaffAccounts(),"ordinary account cannot enumerate users");
  Denied(()=>staff.ReadEmployees(),"ordinary account cannot enumerate employees without grant");
  var initialStamp=staff.ReadSessionAccessStamp();
  s.SetStaffGrant(role.Id,"Clients.View",true,false);check(staff.Can("Clients.View"),"grant becomes effective on existing session");
  check(staff.ReadSessionAccessStamp()!=initialStamp,"grant change alters desktop access stamp");
  Invalid(()=>s.SetStaffGrant(role.Id,"Clients.View",false,false),"stale grant update denied");
  s.SetStaffGrant(role.Id,"Clients.View",false,true);check(!staff.Can("Clients.View"),"revocation effective immediately");
  s.SetStaffGrant(role.Id,"Clients.View",true,false);
  var row=s.ReadStaffAccounts().Single(a=>a.Id==user);s.ResetStaffPassword(user,row.Revision,"Synthetic-temporary-0002");
  check(!staff.Can("Clients.View"),"reset invalidates existing session");
  Denied(()=>staff.ReadSessionAccessStamp(),"reset invalidates access stamp");
  Denied(()=>staff.FirmName(),"reset invalidates firm identity read");
  var replacement=new LocalPractice(path);check(replacement.ChangeRequiredPassword("synthetic-engineer","Synthetic-temporary-0002","Synthetic-permanent-0002"),"reset temporary password replaced");
  check(!staff.Can("Clients.View"),"old session remains invalid after password changed");
  check(replacement.Login("synthetic-engineer","Synthetic-permanent-0002")&&replacement.Can("Clients.View"),"replacement session authorized");
  row=s.ReadStaffAccounts().Single(a=>a.Id==user);s.SetStaffAccount(user,row.Revision,role.Id,false);
  Denied(()=>replacement.FirmName(),"disabled account cannot read firm identity");
  check(!replacement.Can("Clients.View")&&!replacement.Login("synthetic-engineer","Synthetic-permanent-0002"),"disabled account denied");
  Invalid(()=>s.SetStaffAccount(user,row.Revision,role.Id,true),"stale account change denied");
  Invalid(()=>s.ResetStaffPassword(s.ReadStaffAccounts().Single(a=>a.Role=="Administrator").Id,1,"Synthetic-temporary-0003"),"administrator reset protected");
  row=s.ReadStaffAccounts().Single(a=>a.Id==user);s.SetStaffAccount(user,row.Revision,role.Id,true);
  var employee=s.CreateEmployee(new("Synthetic","Engineer","synthetic@example.invalid","Engineer",true),user);
  var e=s.ReadEmployees().Single();check(e.Number=="EMP-000001"&&e.UserId==user,"employee link and number persisted");
  Invalid(()=>s.CreateEmployee(new("Duplicate","Employee","","",true),user),"duplicate account link denied");
  var day=new DateOnly(2026,11,26);s.CreateEmployeeLicense(employee,"CA","Civil Engineer","SYNTHETIC",day.AddDays(15));
  var other=s.CreateEmployee(new("Synthetic","Other","","Engineer",true));
  var expired=s.CreateEmployeeLicense(other,"CA","Structural Engineer","PRIVATE-EXPIRED",day.AddDays(-1));
  s.CreateEmployeeLicense(other,"CA","Civil Engineer","PRIVATE-TODAY",day);
  s.CreateEmployeeLicense(other,"NV","Civil Engineer","PRIVATE-EDGE",day.AddDays(30));
  s.CreateEmployeeLicense(other,"OR","Civil Engineer","PRIVATE-LATER",day.AddDays(31));
  var review=s.ReadEmployeeLicenseExpiry(day);
  check(review.Expired==1&&review.DueToday==1&&review.Upcoming==2&&review.Items.Count==4,"firm license review includes overdue and inclusive horizon");
  check(review.Items.Select(x=>x.DaysRemaining).SequenceEqual(new[]{-1,0,15,30}),"firm license review sorts by recorded expiration");
  check(review.Items.All(x=>!x.ToString().Contains("PRIVATE-",StringComparison.Ordinal)),"license alert omits license numbers");
  check(s.ReadEmployeeLicenseExpiry(day,31).Items.Count==5,"custom horizon includes next boundary");
  Invalid(()=>s.ReadEmployeeLicenseExpiry(day,0),"invalid license horizon rejected");
  Invalid(()=>s.ReadEmployeeLicenseExpiry(DateOnly.MaxValue,30),"overflowing license horizon rejected");
  var denied=new LocalPractice(path);Denied(()=>denied.ReadEmployeeLicenseExpiry(day),"unauthenticated expiry review denied");
  check(replacement.Login("synthetic-engineer","Synthetic-permanent-0002"),"staff login for license review");
  Denied(()=>replacement.ReadEmployeeLicenseExpiry(day),"missing employee grant denies expiry review");
  s.SetStaffGrant(role.Id,"Employees.View",true,false);
  check(replacement.ReadEmployeeLicenseExpiry(day).Items.Count==4,"employee view grant allows limited expiry review");
  s.SetStaffGrant(role.Id,"Employees.View",false,true);
  Denied(()=>replacement.ReadEmployeeLicenseExpiry(day),"revoked employee grant denies expiry review");
  var otherRow=s.ReadEmployees().Single(x=>x.Id==other);
  s.EditEmployee(other,otherRow.Revision,new(otherRow.First,otherRow.Last,otherRow.Email,otherRow.Title,false));
  check(s.ReadEmployeeLicenseExpiry(day).Items.Single().EmployeeId==employee,"inactive staff excluded from current license alerts");

  var license=s.ReadEmployeeLicenses(employee).Single();check(license.ExpiryStatus(day)=="Due within 30 days"&&license.ExpiryStatus(day.AddDays(16))=="Expired","license date boundaries");
  s.RenewEmployeeLicense(employee,license.Id,license.Revision,day.AddYears(1));check(s.ReadEmployeeLicenses(employee).Single().ExpiryStatus(day)=="Current","license renewal recorded");
  check(s.ReadEmployeeLicenseExpiry(day).Items.Count==0,"renewed license exits current alert window");
  Invalid(()=>s.RenewEmployeeLicense(employee,license.Id,license.Revision,day),"stale license renewal rejected");
  s.EditEmployee(employee,e.Revision,new(e.First,e.Last,e.Email,e.Title,false));check(!s.ReadEmployees().Single(x=>x.Id==employee).Active,"employee status persisted");
  Invalid(()=>s.CreateEmployeeLicense(employee,"CA","Other","SYNTHETIC2",day),"inactive employee cannot receive license");
  check(!s.ReadEmployees().Single(x=>x.Id==employee).ToString().Contains("password",StringComparison.OrdinalIgnoreCase),"employee view has no credentials or rates");
 }
}
