using System.Globalization;
using Microsoft.Data.Sqlite;
using PracticeManager.Core;
namespace PracticeManager.Persistence;
public sealed partial class LocalPractice
{
 private long RequireAdministration(SqliteConnection c,SqliteTransaction t)
 {
  var actor=Require(c,t,"Administration.Manage");
  if(Convert.ToInt64(Scalar(c,t,"SELECT count(*) FROM users u JOIN roles r ON r.id=u.role_id WHERE u.id=$i AND r.name='Administrator'",("$i",actor)))!=1)throw new UnauthorizedAccessException("Administrator role required.");
  return actor;
 }
 public IReadOnlyList<StaffRole> ReadStaffRoles()
 {
  using var c=Open();using var t=c.BeginTransaction();RequireAdministration(c,t);using var q=Command(c,t,"SELECT id,name FROM roles ORDER BY name");using var r=q.ExecuteReader();var rows=new List<StaffRole>();while(r.Read())rows.Add(new(r.GetInt64(0),r.GetString(1)));return rows.AsReadOnly();
 }
 public IReadOnlyList<StaffAccount> ReadStaffAccounts()
 {
  using var c=Open();using var t=c.BeginTransaction();RequireAdministration(c,t);using var q=Command(c,t,"SELECT u.id,u.username,r.name,u.active,u.force_password_change,u.credential_revision FROM users u JOIN roles r ON r.id=u.role_id ORDER BY u.username");using var r=q.ExecuteReader();var rows=new List<StaffAccount>();while(r.Read())rows.Add(new(r.GetInt64(0),r.GetString(1),r.GetString(2),r.GetInt64(3)==1,r.GetInt64(4)==1,r.GetInt64(5)));return rows.AsReadOnly();
 }
 public IReadOnlyList<StaffGrant> ReadStaffGrants(long role)
 {
  using var c=Open();using var t=c.BeginTransaction();RequireAdministration(c,t);
  if(Scalar(c,t,"SELECT name FROM roles WHERE id=$r",("$r",role)) is not string)throw new ArgumentException("Role unavailable.");
  using var q=Command(c,t,"SELECT p.code,coalesce(rp.allowed,0) FROM permissions p LEFT JOIN role_permissions rp ON rp.permission_code=p.code AND rp.role_id=$r ORDER BY p.code",("$r",role));using var r=q.ExecuteReader();var rows=new List<StaffGrant>();while(r.Read())rows.Add(new(r.GetString(0),r.GetInt64(1)==1));return rows.AsReadOnly();
 }
 public void SetStaffGrant(long role,string permission,bool allowed,bool expected)
 {
  using var c=Open();using var t=c.BeginTransaction(deferred:false);var actor=RequireAdministration(c,t);
  if(Scalar(c,t,"SELECT name FROM roles WHERE id=$r",("$r",role)) is not string name||name=="Administrator"||permission=="Administration.Manage"||!Grants.Contains(permission))throw new ArgumentException("Protected or unknown grant.");
  var current=Convert.ToInt64(Scalar(c,t,"SELECT coalesce((SELECT allowed FROM role_permissions WHERE role_id=$r AND permission_code=$p),0)",("$r",role),("$p",permission)))==1;
  if(current!=expected)throw new ArgumentException("Grant changed. Refresh.");
  if(current!=allowed){Exec(c,t,"INSERT INTO role_permissions(role_id,permission_code,allowed) VALUES($r,$p,$a) ON CONFLICT(role_id,permission_code) DO UPDATE SET allowed=excluded.allowed",("$r",role),("$p",permission),("$a",allowed?1:0));Audit(c,t,actor,(allowed?"Grant:":"Revoke:")+permission,"Role",role);}t.Commit();
 }
 public long CreateStaffAccount(string username,long role,string temporaryPassword)
 {
  using var c=Open();using var t=c.BeginTransaction(deferred:false);var actor=RequireAdministration(c,t);
  var n=Validation.Username(username);
  if(Scalar(c,t,"SELECT name FROM roles WHERE id=$r",("$r",role)) is not string name||name=="Administrator")throw new ArgumentException("Select a non-administrator role.");
  if(Convert.ToInt64(Scalar(c,t,"SELECT count(*) FROM users WHERE normalized_username=$n",("$n",n)))!=0)throw new ArgumentException("Username already exists.");
  var hash=PasswordCodec.Hash(temporaryPassword);
  Exec(c,t,"INSERT INTO users(username,normalized_username,password_hash,role_id,active,force_password_change) VALUES($u,$n,$h,$r,1,1)",("$u",username.Trim()),("$n",n),("$h",hash),("$r",role));
  var id=Last(c,t);Audit(c,t,actor,"CreateStaffAccount","User",id);t.Commit();return id;
 }
 private static void RequireManagedStaff(SqliteConnection c,SqliteTransaction t,long id,long actor,long revision)
 {
  if(id==actor||Convert.ToInt64(Scalar(c,t,"SELECT count(*) FROM users u JOIN roles r ON r.id=u.role_id WHERE u.id=$i AND r.name!='Administrator' AND u.credential_revision=$v",("$i",id),("$v",revision)))!=1)throw new ArgumentException("Account changed or is protected.");
 }
 public void SetStaffAccount(long id,long expectedRevision,long role,bool active)
 {
  using var c=Open();using var t=c.BeginTransaction(deferred:false);var actor=RequireAdministration(c,t);RequireManagedStaff(c,t,id,actor,expectedRevision);
  if(Scalar(c,t,"SELECT name FROM roles WHERE id=$r",("$r",role)) is not string name||name=="Administrator")throw new ArgumentException("Select a non-administrator role.");
  Exec(c,t,"UPDATE users SET role_id=$r,active=$a,credential_revision=credential_revision+1 WHERE id=$i",("$r",role),("$a",active?1:0),("$i",id));Audit(c,t,actor,"UpdateStaffAccount","User",id);t.Commit();
 }
 public void ResetStaffPassword(long id,long expectedRevision,string temporaryPassword)
 {
  using var c=Open();using var t=c.BeginTransaction(deferred:false);var actor=RequireAdministration(c,t);RequireManagedStaff(c,t,id,actor,expectedRevision);
  var hash=PasswordCodec.Hash(temporaryPassword);
  Exec(c,t,"UPDATE users SET password_hash=$h,force_password_change=1,failed_attempts=0,locked_until=0,credential_revision=credential_revision+1 WHERE id=$i",("$h",hash),("$i",id));Audit(c,t,actor,"ResetStaffPassword","User",id);t.Commit();
 }
 // This public credential-only path never creates an authenticated session.
 public bool ChangeRequiredPassword(string username,string temporaryPassword,string newPassword)
 {
  Logout();string n;try{n=Validation.Username(username);}catch(ArgumentException){return false;}
  if(newPassword.Length is <14 or >256||newPassword==temporaryPassword)throw new ArgumentException("Choose a different password of 14–256 characters.");
  using var c=Open();using var t=c.BeginTransaction(deferred:false);
  long id,attempts,locked;string hash;
  using(var q=Command(c,t,"SELECT id,password_hash,failed_attempts,locked_until FROM users WHERE normalized_username=$n AND active=1 AND force_password_change=1",("$n",n)))
  using(var r=q.ExecuteReader()){if(!r.Read())return false;id=r.GetInt64(0);hash=r.GetString(1);attempts=r.GetInt64(2);locked=r.GetInt64(3);}
  var now=DateTimeOffset.UtcNow.ToUnixTimeSeconds();if(locked>now)return false;
  if(!PasswordCodec.Verify(temporaryPassword,hash)){attempts=locked>0&&locked<=now?1:attempts+1;Exec(c,t,"UPDATE users SET failed_attempts=$a,locked_until=$l WHERE id=$i",("$a",attempts),("$l",attempts>=5?now+300:0),("$i",id));Audit(c,t,id,"RequiredPasswordFailed","User",id);t.Commit();return false;}
  var next=PasswordCodec.Hash(newPassword);
  Exec(c,t,"UPDATE users SET password_hash=$h,force_password_change=0,failed_attempts=0,locked_until=0,credential_revision=credential_revision+1 WHERE id=$i",("$h",next),("$i",id));Audit(c,t,id,"RequiredPasswordChanged","User",id);t.Commit();return true;
 }
 private static (string First,string Last,string Email,string Title) ValidateEmployee(EmployeeInput input)=>(Validation.Text(input.First,60,"First name",true),Validation.Text(input.Last,60,"Last name",true),Validation.Text(input.Email,150,"Email"),Validation.Text(input.Title,100,"Title"));
 public IReadOnlyList<EmployeeRecord> ReadEmployees()
 {
  using var c=Open();using var t=c.BeginTransaction();Require(c,t,"Employees.View");using var q=Command(c,t,"SELECT id,employee_no,user_id,first_name,last_name,email,title,active,revision FROM employees ORDER BY last_name,first_name,id LIMIT 501");using var r=q.ExecuteReader();var rows=new List<EmployeeRecord>();while(r.Read()){if(rows.Count==500)throw new ArgumentException("Employee register exceeds development limit.");rows.Add(new(r.GetInt64(0),r.GetString(1),r.IsDBNull(2)?null:r.GetInt64(2),r.GetString(3),r.GetString(4),r.GetString(5),r.GetString(6),r.GetInt64(7)==1,r.GetInt32(8)));}return rows.AsReadOnly();
 }
 public long CreateEmployee(EmployeeInput input,long? account=null)
 {
  using var c=Open();using var t=c.BeginTransaction(deferred:false);var actor=Require(c,t,"Employees.Edit");Require(c,t,"Employees.View");var v=ValidateEmployee(input);
  if(account.HasValue){RequireAdministration(c,t);if(Convert.ToInt64(Scalar(c,t,"SELECT count(*) FROM users WHERE id=$i AND active=1",("$i",account)))!=1||Convert.ToInt64(Scalar(c,t,"SELECT count(*) FROM employees WHERE user_id=$i",("$i",account)))!=0)throw new ArgumentException("Account unavailable or already linked.");}
  var n=NextNumber(c,t,"Employee","EMP");Exec(c,t,"INSERT INTO employees(employee_no,user_id,first_name,last_name,email,title,active,revision,created_by,created_utc) VALUES($n,$i,$f,$l,$e,$t,$a,1,$by,$u)",("$n",n),("$i",account),("$f",v.First),("$l",v.Last),("$e",v.Email),("$t",v.Title),("$a",input.Active?1:0),("$by",actor),("$u",UtcNow()));var id=Last(c,t);Audit(c,t,actor,"Create","Employee",id);t.Commit();return id;
 }
 public void EditEmployee(long id,int revision,EmployeeInput input)
 {
  using var c=Open();using var t=c.BeginTransaction(deferred:false);var actor=Require(c,t,"Employees.Edit");Require(c,t,"Employees.View");var v=ValidateEmployee(input);
  if(Exec(c,t,"UPDATE employees SET first_name=$f,last_name=$l,email=$e,title=$t,active=$a,revision=revision+1 WHERE id=$i AND revision=$r",("$f",v.First),("$l",v.Last),("$e",v.Email),("$t",v.Title),("$a",input.Active?1:0),("$i",id),("$r",revision))!=1)throw new ArgumentException("Employee changed. Refresh.");Audit(c,t,actor,"Update","Employee",id);t.Commit();
 }
 public IReadOnlyList<EmployeeLicense> ReadEmployeeLicenses(long employee)
 {
  using var c=Open();using var t=c.BeginTransaction();Require(c,t,"Employees.View");using var q=Command(c,t,"SELECT id,employee_id,jurisdiction,license_type,license_number,expires_on,revision FROM employee_licenses WHERE employee_id=$i ORDER BY expires_on,id",("$i",employee));using var r=q.ExecuteReader();var rows=new List<EmployeeLicense>();while(r.Read())rows.Add(new(r.GetInt64(0),r.GetInt64(1),r.GetString(2),r.GetString(3),r.GetString(4),DateOnly.ParseExact(r.GetString(5),"yyyy-MM-dd",CultureInfo.InvariantCulture),r.GetInt32(6)));return rows.AsReadOnly();
 }
 public long CreateEmployeeLicense(long employee,string jurisdiction,string type,string number,DateOnly expires)
 {
  using var c=Open();using var t=c.BeginTransaction(deferred:false);var actor=Require(c,t,"Employees.Edit");Require(c,t,"Employees.View");
  if(expires==DateOnly.MinValue||Convert.ToInt64(Scalar(c,t,"SELECT count(*) FROM employees WHERE id=$i AND active=1",("$i",employee)))!=1)throw new ArgumentException("Active employee and expiration date required.");
  var j=Validation.Text(jurisdiction,80,"Jurisdiction",true);var k=Validation.Text(type,80,"License type",true);var n=Validation.Text(number,80,"License number",true);
  Exec(c,t,"INSERT INTO employee_licenses(employee_id,jurisdiction,license_type,license_number,expires_on,revision) VALUES($i,$j,$t,$n,$d,1)",("$i",employee),("$j",j),("$t",k),("$n",n),("$d",expires.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)));var id=Last(c,t);Audit(c,t,actor,"Create","EmployeeLicense",id);t.Commit();return id;
 }
 public void RenewEmployeeLicense(long employee,long id,int revision,DateOnly expires)
 {
  using var c=Open();using var t=c.BeginTransaction(deferred:false);var actor=Require(c,t,"Employees.Edit");Require(c,t,"Employees.View");
  if(expires==DateOnly.MinValue||Exec(c,t,"UPDATE employee_licenses SET expires_on=$d,revision=revision+1 WHERE id=$i AND employee_id=$e AND revision=$r AND EXISTS(SELECT 1 FROM employees WHERE id=$e AND active=1)",("$d",expires.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)),("$i",id),("$e",employee),("$r",revision))!=1)throw new ArgumentException("License changed or employee inactive.");Audit(c,t,actor,"Renew","EmployeeLicense",id);t.Commit();
 }
}
