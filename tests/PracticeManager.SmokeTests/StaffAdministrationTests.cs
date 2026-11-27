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
  var user=s.CreateStaffAccount("synthetic-engineer",role.Id,"Synthetic-temporary-0001");
  var staff=new LocalPractice(path);check(!staff.Login("synthetic-engineer","Synthetic-temporary-0001"),"temporary password cannot open session");
  check(!staff.ChangeRequiredPassword("synthetic-engineer","wrong","Synthetic-permanent-0001"),"incorrect temporary password denied");
  check(staff.ChangeRequiredPassword("synthetic-engineer","Synthetic-temporary-0001","Synthetic-permanent-0001")&&!staff.Can("Clients.View"),"required password change creates no session");
  check(staff.Login("synthetic-engineer","Synthetic-permanent-0001"),"new password authenticates");
  Denied(()=>staff.ReadStaffAccounts(),"ordinary account cannot enumerate users");
  Denied(()=>staff.ReadEmployees(),"ordinary account cannot enumerate employees without grant");
  s.SetStaffGrant(role.Id,"Clients.View",true,false);check(staff.Can("Clients.View"),"grant becomes effective on existing session");
  Invalid(()=>s.SetStaffGrant(role.Id,"Clients.View",false,false),"stale grant update denied");
  s.SetStaffGrant(role.Id,"Clients.View",false,true);check(!staff.Can("Clients.View"),"revocation effective immediately");
  s.SetStaffGrant(role.Id,"Clients.View",true,false);
  var row=s.ReadStaffAccounts().Single(a=>a.Id==user);s.ResetStaffPassword(user,row.Revision,"Synthetic-temporary-0002");
  check(!staff.Can("Clients.View"),"reset invalidates existing session");
  var replacement=new LocalPractice(path);check(replacement.ChangeRequiredPassword("synthetic-engineer","Synthetic-temporary-0002","Synthetic-permanent-0002"),"reset temporary password replaced");
  check(!staff.Can("Clients.View"),"old session remains invalid after password changed");
  check(replacement.Login("synthetic-engineer","Synthetic-permanent-0002")&&replacement.Can("Clients.View"),"replacement session authorized");
  row=s.ReadStaffAccounts().Single(a=>a.Id==user);s.SetStaffAccount(user,row.Revision,role.Id,false);
  check(!replacement.Can("Clients.View")&&!replacement.Login("synthetic-engineer","Synthetic-permanent-0002"),"disabled account denied");
  Invalid(()=>s.SetStaffAccount(user,row.Revision,role.Id,true),"stale account change denied");
  Invalid(()=>s.ResetStaffPassword(s.ReadStaffAccounts().Single(a=>a.Role=="Administrator").Id,1,"Synthetic-temporary-0003"),"administrator reset protected");
  row=s.ReadStaffAccounts().Single(a=>a.Id==user);s.SetStaffAccount(user,row.Revision,role.Id,true);
  var employee=s.CreateEmployee(new("Synthetic","Engineer","synthetic@example.invalid","Engineer",true),user);
  var e=s.ReadEmployees().Single();check(e.Number=="EMP-000001"&&e.UserId==user,"employee link and number persisted");
  Invalid(()=>s.CreateEmployee(new("Duplicate","Employee","","",true),user),"duplicate account link denied");
  var day=new DateOnly(2026,11,26);s.CreateEmployeeLicense(employee,"CA","Civil Engineer","SYNTHETIC",day.AddDays(15));
  var license=s.ReadEmployeeLicenses(employee).Single();check(license.ExpiryStatus(day)=="Due within 30 days"&&license.ExpiryStatus(day.AddDays(16))=="Expired","license date boundaries");
  s.RenewEmployeeLicense(employee,license.Id,license.Revision,day.AddYears(1));check(s.ReadEmployeeLicenses(employee).Single().ExpiryStatus(day)=="Current","license renewal recorded");
  Invalid(()=>s.RenewEmployeeLicense(employee,license.Id,license.Revision,day),"stale license renewal rejected");
  s.EditEmployee(employee,e.Revision,new(e.First,e.Last,e.Email,e.Title,false));check(!s.ReadEmployees().Single().Active,"employee status persisted");
  Invalid(()=>s.CreateEmployeeLicense(employee,"CA","Other","SYNTHETIC2",day),"inactive employee cannot receive license");
  check(!s.ReadEmployees().Single().ToString().Contains("password",StringComparison.OrdinalIgnoreCase),"employee view has no credentials or rates");
 }
}
