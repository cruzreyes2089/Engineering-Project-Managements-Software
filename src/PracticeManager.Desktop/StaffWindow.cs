using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using PracticeManager.Core;
using PracticeManager.Persistence;
namespace PracticeManager.Desktop;
internal sealed class StaffWindow:Window
{
 readonly LocalPractice service;
 readonly DataGrid accounts=Grid(),grants=Grid(),employees=Grid(),licenses=Grid();
 readonly ComboBox roles=new(){DisplayMemberPath="Name"};
 readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap};
 readonly TabControl tabs=new();
 static DataGrid Grid()=>new(){AutoGenerateColumns=false,IsReadOnly=true,CanUserAddRows=false,SelectionMode=DataGridSelectionMode.Single};
 static void Col(DataGrid g,string title,string property)=>g.Columns.Add(new DataGridTextColumn{Header=title,Binding=new Binding(property),Width=DataGridLength.Auto});
 public StaffWindow(LocalPractice service)
 {
  this.service=service;Title="Staff and permissions — development";Width=1050;Height=680;WindowStartupLocation=WindowStartupLocation.CenterOwner;
  var root=new DockPanel{Margin=new Thickness(16)};var head=new StackPanel();head.Children.Add(new TextBlock{Text="Staff and permissions",FontSize=24,FontWeight=FontWeights.SemiBold});head.Children.Add(new TextBlock{Text="Synthetic only. Roles start with no grants. Employee directory contains no compensation or billing rates. Account access and employee status are independent.",TextWrapping=TextWrapping.Wrap});head.Children.Add(status);DockPanel.SetDock(head,Dock.Top);root.Children.Add(head);root.Children.Add(tabs);Content=root;
  if(service.Can("Administration.Manage"))BuildAdmin();
  if(service.Can("Employees.View"))BuildEmployees();
 }
 void Run(Action a){try{a();}catch(UnauthorizedAccessException){status.Text="Access denied.";}catch(ArgumentException ex){status.Text=ex.Message;}catch(Exception){status.Text="Operation failed. Refresh before retrying.";}}
 Button Btn(string label,Action action){var b=new Button{Content=label,Margin=new Thickness(2)};b.Click+=(_,_)=>Run(action);return b;}
 DockPanel Page(DataGrid data,params UIElement[] controls){var p=new DockPanel();var bar=new WrapPanel();foreach(var x in controls)bar.Children.Add(x);DockPanel.SetDock(bar,Dock.Top);p.Children.Add(bar);p.Children.Add(data);return p;}
 void BuildAdmin()
 {
  foreach(var (label,prop) in new[]{("Username","Username"),("Role","Role"),("Active","Active"),("Must change password","PasswordChangeRequired")})Col(accounts,label,prop);
  Col(grants,"Permission","Permission");Col(grants,"Allowed","Allowed");
  tabs.Items.Add(new TabItem{Header="Accounts",Content=Page(accounts,Btn("Create account",()=>AccountEditor(null)),Btn("Edit account",()=>AccountEditor(accounts.SelectedItem as StaffAccount??throw new ArgumentException("Select an account."))),Btn("Reset temporary password",Reset),Btn("Refresh",RefreshAccounts))});
  tabs.Items.Add(new TabItem{Header="Role permissions",Content=Page(grants,roles,Btn("Grant selected",()=>SetGrant(true)),Btn("Revoke selected",()=>SetGrant(false)))});
  var policyPanel=new StackPanel{Margin=new Thickness(16)};policyPanel.Children.Add(new TextBlock{Text="Single-owner approval exception. Explicit opt-in only; automatically suspended when another active account receives an approval grant. Every owner self-approval requires a separate confirmation and justification.",TextWrapping=TextWrapping.Wrap});var policyStatus=new TextBlock{TextWrapping=TextWrapping.Wrap};policyPanel.Children.Add(policyStatus);void RefreshPolicy(){var p=service.ReadOwnerApprovalPolicy();policyStatus.Text=$"Enabled: {p.Enabled} • Eligible: {p.Eligible}\n{p.Reason}";}void ChangePolicy(bool enable){var p=service.ReadOwnerApprovalPolicy();var note=WorkWindowFields.Note(this,enable?"Enable single-owner exception":"Disable single-owner exception");if(note==null)return;service.SetOwnerApprovalPolicy(enable,p.EventId,note);RefreshPolicy();}policyPanel.Children.Add(Btn("Enable exception",()=>ChangePolicy(true)));policyPanel.Children.Add(Btn("Disable exception",()=>ChangePolicy(false)));policyPanel.Children.Add(Btn("Refresh policy",RefreshPolicy));policyPanel.Children.Add(Btn("View approval audit",()=>new OwnerApprovalHistoryWindow(service){Owner=this}.ShowDialog()));tabs.Items.Add(new TabItem{Header="Owner approval policy",Content=policyPanel});Run(RefreshPolicy);
  if(service.Can("Documents.Restricted"))tabs.Items.Add(new TabItem{Header="Business storage",Content=Btn("Configure storage preferences",()=>new FirmStorageSettingsWindow(service){Owner=this}.ShowDialog())});
  if(service.Can("Compliance.View"))tabs.Items.Add(new TabItem{Header="Jurisdiction policy",Content=Btn("Open jurisdiction register",()=>new JurisdictionPolicyWindow(service){Owner=this}.ShowDialog())});
  roles.ItemsSource=service.ReadStaffRoles();roles.SelectionChanged+=(_,_)=>Run(RefreshGrants);RefreshAccounts();
 }
 void RefreshAccounts()=>accounts.ItemsSource=service.ReadStaffAccounts();
 void RefreshGrants(){grants.ItemsSource=null;if(roles.SelectedItem is StaffRole r)grants.ItemsSource=service.ReadStaffGrants(r.Id);}
 void SetGrant(bool value){if(roles.SelectedItem is not StaffRole role||grants.SelectedItem is not StaffGrant grant)throw new ArgumentException("Select role and grant.");if(MessageBox.Show(this,$"{(value?"Grant":"Revoke")} {grant.Permission} for {role.Name}?","Permission change",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;service.SetStaffGrant(role.Id,grant.Permission,value,grant.Allowed);RefreshGrants();}
 Window Editor(string title,StackPanel panel)=>new(){Owner=this,Title=title,Width=520,Height=470,WindowStartupLocation=WindowStartupLocation.CenterOwner,Content=new ScrollViewer{Content=panel}};
 TextBox Field(StackPanel p,string label,string value=""){p.Children.Add(new TextBlock{Text=label});var b=new TextBox{Text=value,MaxLength=200};p.Children.Add(b);return b;}
 void SaveButton(Window w,StackPanel p,Action save){var b=new Button{Content="Save"};p.Children.Add(b);b.Click+=(_,_)=>{try{save();w.DialogResult=true;}catch(ArgumentException ex){MessageBox.Show(w,ex.Message);}catch(UnauthorizedAccessException){MessageBox.Show(w,"Access denied.");}catch(Exception){MessageBox.Show(w,"Save failed; refresh before retrying.");}};}
 void AccountEditor(StaffAccount? a)
 {
  var p=new StackPanel{Margin=new Thickness(18)};var w=Editor("Staff account",p);var username=Field(p,"Username",a?.Username??"");username.IsReadOnly=a!=null;
  var role=new ComboBox{ItemsSource=service.ReadStaffRoles().Where(r=>r.Name!="Administrator").ToArray(),DisplayMemberPath="Name"};p.Children.Add(new TextBlock{Text="Role"});p.Children.Add(role);if(a!=null)role.SelectedItem=((IEnumerable<StaffRole>)role.ItemsSource).FirstOrDefault(r=>r.Name==a.Role);
  var active=new CheckBox{Content="Active",IsChecked=a?.Active??true,IsEnabled=a!=null};p.Children.Add(active);var secret=new PasswordBox{MaxLength=256};
  if(a==null){p.Children.Add(new TextBlock{Text="Temporary password (14–256 characters)"});p.Children.Add(secret);}
  SaveButton(w,p,()=>{if(role.SelectedItem is not StaffRole r)throw new ArgumentException("Select a non-administrator role.");if(a==null)service.CreateStaffAccount(username.Text,r.Id,secret.Password);else service.SetStaffAccount(a.Id,a.Revision,r.Id,active.IsChecked==true);});if(w.ShowDialog()==true)RefreshAccounts();secret.Clear();
 }
 void Reset(){var a=accounts.SelectedItem as StaffAccount??throw new ArgumentException("Select an account.");var p=new StackPanel{Margin=new Thickness(18)};var w=Editor("Reset temporary password",p);p.Children.Add(new TextBlock{Text="Reset "+a.Username+"; existing sessions will be invalidated."});var secret=new PasswordBox{MaxLength=256};p.Children.Add(secret);SaveButton(w,p,()=>service.ResetStaffPassword(a.Id,a.Revision,secret.Password));if(w.ShowDialog()==true)RefreshAccounts();secret.Clear();}
 void BuildEmployees()
 {
  foreach(var (label,prop) in new[]{("Number","Number"),("Name","Name"),("Email","Email"),("Title","Title"),("Active","Active")})Col(employees,label,prop);
  foreach(var (label,prop) in new[]{("Jurisdiction","Jurisdiction"),("Type","Type"),("Number","Number"),("Expires","Expires"),("Status","Status")})Col(licenses,label,prop);
  var p=new DockPanel();var top=new WrapPanel();top.Children.Add(Btn("New employee",()=>EmployeeEditor(null)));top.Children.Add(Btn("Edit employee",()=>EmployeeEditor(SelectedEmployee())));top.Children.Add(Btn("New license",()=>LicenseEditor(null)));top.Children.Add(Btn("Renew selected license",()=>LicenseEditor((licenses.SelectedItem as LicenseDisplay)?.License??throw new ArgumentException("Select a license."))));top.Children.Add(Btn("Refresh",RefreshEmployees));DockPanel.SetDock(top,Dock.Top);p.Children.Add(top);
  var split=new System.Windows.Controls.Grid();split.RowDefinitions.Add(new RowDefinition());split.RowDefinitions.Add(new RowDefinition());System.Windows.Controls.Grid.SetRow(employees,0);System.Windows.Controls.Grid.SetRow(licenses,1);split.Children.Add(employees);split.Children.Add(licenses);p.Children.Add(split);tabs.Items.Add(new TabItem{Header="Employees and licenses",Content=p});employees.SelectionChanged+=(_,_)=>Run(RefreshLicenses);RefreshEmployees();
 }
 EmployeeRecord SelectedEmployee()=>employees.SelectedItem as EmployeeRecord??throw new ArgumentException("Select an employee.");
 void RefreshEmployees(){employees.ItemsSource=service.ReadEmployees();licenses.ItemsSource=null;}
 private sealed record LicenseDisplay(EmployeeLicense License,string Jurisdiction,string Type,string Number,string Expires,string Status);
 void RefreshLicenses(){licenses.ItemsSource=null;if(employees.SelectedItem is EmployeeRecord e)licenses.ItemsSource=service.ReadEmployeeLicenses(e.Id).Select(l=>new LicenseDisplay(l,l.Jurisdiction,l.Type,l.Number,l.Expires.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),l.ExpiryStatus(DateOnly.FromDateTime(DateTime.Today)))).ToArray();}
 void EmployeeEditor(EmployeeRecord? e)
 {
  var p=new StackPanel{Margin=new Thickness(18)};var w=Editor("Employee",p);var first=Field(p,"First name",e?.First??"");var last=Field(p,"Last name",e?.Last??"");var email=Field(p,"Email",e?.Email??"");var title=Field(p,"Title",e?.Title??"");var active=new CheckBox{Content="Active employee",IsChecked=e?.Active??true};p.Children.Add(active);
  var linked=new ComboBox{DisplayMemberPath="Username"};if(e==null&&service.Can("Administration.Manage")){p.Children.Add(new TextBlock{Text="Optional account link (immutable)"});linked.ItemsSource=service.ReadStaffAccounts().Where(a=>a.Active&&!service.ReadEmployees().Any(x=>x.UserId==a.Id)).ToArray();p.Children.Add(linked);}
  SaveButton(w,p,()=>{var v=new EmployeeInput(first.Text,last.Text,email.Text,title.Text,active.IsChecked==true);if(e==null)service.CreateEmployee(v,(linked.SelectedItem as StaffAccount)?.Id);else service.EditEmployee(e.Id,e.Revision,v);});if(w.ShowDialog()==true)RefreshEmployees();
 }
 void LicenseEditor(EmployeeLicense? l)
 {
  var e=SelectedEmployee();var p=new StackPanel{Margin=new Thickness(18)};var w=Editor("Employee license",p);var j=Field(p,"Jurisdiction",l?.Jurisdiction??"");var k=Field(p,"Type",l?.Type??"");var n=Field(p,"License number",l?.Number??"");j.IsReadOnly=k.IsReadOnly=n.IsReadOnly=l!=null;var d=Field(p,"Expiration yyyy-MM-dd",l?.Expires.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)??"");SaveButton(w,p,()=>{if(!DateOnly.TryParseExact(d.Text,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var day))throw new ArgumentException("Enter a valid ISO date.");if(l==null)service.CreateEmployeeLicense(e.Id,j.Text,k.Text,n.Text,day);else service.RenewEmployeeLicense(e.Id,l.Id,l.Revision,day);});if(w.ShowDialog()==true)RefreshLicenses();
 }
}
