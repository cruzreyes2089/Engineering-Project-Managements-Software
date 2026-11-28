using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using PracticeManager.Core;
using PracticeManager.Persistence;
namespace PracticeManager.Desktop;
public partial class MainWindow : Window
{
 LocalPractice? service; bool setup; DevelopmentWorkspace? workspace;
 readonly DispatcherTimer sessionGuard=new(){Interval=TimeSpan.FromSeconds(5)};
 bool authenticated,locking,lockFault,renewService;
 int operationDepth;
 string? accessStamp;
 LocalPractice S=>service??throw new InvalidOperationException("Local store unavailable.");
 public MainWindow(){InitializeComponent();sessionGuard.Tick+=(_,_)=>CheckSession();InputManager.Current.PreProcessInput+=OnSessionInput;Activated+=(_,_)=>CheckSession();Closed+=(_,_)=>{sessionGuard.Stop();InputManager.Current.PreProcessInput-=OnSessionInput;service?.Logout();};try{workspace=DevelopmentWorkspace.Resolve(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),Environment.GetCommandLineArgs().Skip(1).ToArray());if(workspace.IsAcceptance){Title="CRC Practice Manager — "+(workspace.IsDocumentAcceptance?"SYNTHETIC DOCUMENT ACCEPTANCE":workspace.IsBudgetAcceptance?"SYNTHETIC BUDGET ACCEPTANCE":"SYNTHETIC ACCEPTANCE")+" — "+workspace.AcceptanceId;BuildLabel.Text=(workspace.IsDocumentAcceptance?"SYNTHETIC DOCUMENT ACCEPTANCE":workspace.IsBudgetAcceptance?"SYNTHETIC BUDGET ACCEPTANCE":"SYNTHETIC ACCEPTANCE")+" · "+workspace.AcceptanceId;Status.Text=AcceptanceStatus();}service=workspace.IsDocumentAcceptance?LocalPractice.OpenDocumentAcceptance(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),workspace.AcceptanceId!,Environment.GetCommandLineArgs().Skip(1).SequenceEqual(new[]{"--document-acceptance-new"})):workspace.IsBudgetAcceptance?LocalPractice.OpenBudgetAcceptance(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),workspace.AcceptanceId!,Environment.GetCommandLineArgs().Skip(1).SequenceEqual(new[]{"--budget-acceptance-new"})):new LocalPractice(workspace.DatabasePath);ShowLogin();if(workspace.IsAcceptance)LoginMessage.Text="Synthetic test data only. Session ID is in the title; retain it to reopen."; }catch(ArgumentException e){LoginMessage.Text=e.Message;LoginButton.IsEnabled=false;RequiredPasswordButton.IsEnabled=false;}catch(Exception){LoginMessage.Text="Cannot open requested workspace. No fallback database opened.";LoginButton.IsEnabled=false;RequiredPasswordButton.IsEnabled=false;}}
 string AcceptanceStatus()=>workspace?.IsAcceptance==true?"SYNTHETIC ACCEPTANCE · "+workspace.AcceptanceId+" · No real data or accounting credentials":"Development preview · Local database · No live data";
 void OnSessionInput(object sender,PreProcessInputEventArgs e)
 {
  if(!authenticated||locking)return;
  var input=e.StagingItem.Input;
  if(input is not KeyEventArgs { IsDown:true }&&input is not MouseButtonEventArgs { ButtonState:MouseButtonState.Pressed }&&input is not MouseWheelEventArgs&&input is not TouchEventArgs)return;
  try{S.RegisterSessionActivity();}catch(Exception){LockSession("Session expired. Sign in again.");}
 }
 bool CheckSession()
 {
  if(!authenticated||locking)return false;
  try{if(accessStamp!=null&&S.ReadSessionAccessStamp()==accessStamp)return true;}catch(Exception){}
  LockSession("Session expired or access changed. Sign in again.");return false;
 }
 void ClearWorkspace()
 {
  Workspace.Visibility=Visibility.Collapsed;CrmPanel.Visibility=Visibility.Collapsed;AdvancedNav.IsExpanded=false;AdvancedNav.Visibility=Visibility.Collapsed;
  ClientGrid.ItemsSource=null;ContactGrid.ItemsSource=null;Search.Clear();
  LicenseReminderText.Text="";LicenseReminderPanel.Visibility=Visibility.Collapsed;WorkProfileText.Text="";WorkProfilePanel.Visibility=WorkProfileButton.Visibility=Visibility.Collapsed;
  ClientCount.Text=ProjectSummaryText.Text="";ClientSummaryPanel.Visibility=ProjectSummaryPanel.Visibility=Visibility.Collapsed;ContactsHeading.Text="Select a client to view contacts";FirmLabel.Text="";
  Status.Text=AcceptanceStatus();Password.Clear();Confirm.Clear();Username.Clear();Firm.Clear();Prefix.Text="CL";
 }
 void CloseOwned(Window owner)
 {
  foreach(var child in owner.OwnedWindows.Cast<Window>().ToArray())
  {
   CloseOwned(child);
   try{child.IsEnabled=false;child.Close();if(child.IsVisible){child.Hide();lockFault=true;}}
   catch(Exception){lockFault=true;try{child.Hide();}catch(Exception){}}
  }
  if(owner.OwnedWindows.Count!=0)lockFault=true;
 }
 void UpdateLoginAvailability()
 {
  var ready=!locking&&!lockFault&&!authenticated&&operationDepth==0&&service!=null;
  LoginButton.IsEnabled=ready;RequiredPasswordButton.IsEnabled=ready&&!setup;
 }
 void LockSession(string message)
 {
  if(locking||!authenticated)return;
  locking=true;authenticated=false;accessStamp=null;sessionGuard.Stop();
  // Invalidate the old service before closing any modal or running close callbacks.
  service?.Logout();renewService=true;
  try
  {
   ClearWorkspace();SignOut.Visibility=Visibility.Collapsed;
   CloseOwned(this);ShowLogin();
   LoginMessage.Text=lockFault?"Session closed. Restart the application before signing in.":message;
  }
  catch(Exception){lockFault=true;Workspace.Visibility=Visibility.Collapsed;LoginPanel.Visibility=Visibility.Visible;LoginMessage.Text="Session closed. Restart the application before signing in.";}
  finally{locking=false;UpdateLoginAvailability();}
 }
 void Run(Action a)
 {
  if(!CheckSession())return;
  operationDepth++;
  try{a();}
  catch(UnauthorizedAccessException){if(CheckSession())Status.Text="Access denied.";}
  catch(ArgumentException e){if(CheckSession())MessageBox.Show(this,e.Message,"Check entry",MessageBoxButton.OK,MessageBoxImage.Warning);}
  catch(Exception){if(CheckSession())MessageBox.Show(this,"Operation failed. No successful change is assumed.","Development application",MessageBoxButton.OK,MessageBoxImage.Error);}
  finally{operationDepth--;if(authenticated)CheckSession();else UpdateLoginAvailability();}
 }
 void ShowLogin(){S.Logout();Workspace.Visibility=Visibility.Collapsed;SignOut.Visibility=Visibility.Collapsed;LoginPanel.Visibility=Visibility.Visible;setup=!S.IsInitialized();RequiredPasswordButton.Visibility=setup?Visibility.Collapsed:Visibility.Visible;SetupPanel.Visibility=ConfirmPanel.Visibility=setup?Visibility.Visible:Visibility.Collapsed;LoginHeading.Text=setup?"Set up your practice":"Sign in";LoginButton.Content=setup?"Create company":"Sign in";Password.Clear();Confirm.Clear();LoginMessage.Text="";Username.Focus();UpdateLoginAvailability();}
 void Login_Click(object sender,RoutedEventArgs e){if(authenticated||locking||lockFault||operationDepth!=0)return;try{if(renewService){var path=LocalStoragePath.FilePath(workspace!.DatabasePath,mustExist:true);service=workspace.IsDocumentAcceptance?LocalPractice.OpenDocumentAcceptance(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),workspace.AcceptanceId!,false):workspace.IsBudgetAcceptance?LocalPractice.OpenBudgetAcceptance(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),workspace.AcceptanceId!,false):new LocalPractice(path);renewService=false;}if(setup){if(Password.Password!=Confirm.Password)throw new ArgumentException("Passwords do not match.");S.Bootstrap(Firm.Text,Username.Text,Password.Password,Prefix.Text);setup=false;ShowLogin();LoginMessage.Text="Company created. Sign in with your new credentials.";return;}if(!S.Login(Username.Text,Password.Password)){LoginMessage.Text="Sign-in unsuccessful. Check credentials or try later.";return;}authenticated=true;accessStamp=S.ReadSessionAccessStamp();FirmLabel.Text=S.FirmName();CrmNav.IsEnabled=S.Can("Clients.View");AccountingNav.IsEnabled=S.CanTestAccountingConnection();EngagementNav.IsEnabled=S.Can("Clients.View")&&(S.Can("Engagements.View")||S.Can("Projects.View"));StaffNav.IsEnabled=S.Can("Administration.Manage")||S.Can("Employees.View");WorkNav.IsEnabled=S.Can("Work.Own");ScheduleNav.IsEnabled=S.Can("Projects.Plan")&&S.Can("Projects.View")&&S.Can("Clients.View")&&S.Can("Employees.View");TimeNav.IsEnabled=S.Can("Work.Own");ReviewNav.IsEnabled=S.Can("Work.Review")||S.Can("Billing.Classify");InvoiceNav.IsEnabled=S.Can("Billing.Invoices.View")||S.Can("Billing.Rates");var reportAccess=S.ReadProjectReportAccess();ReportsNav.IsEnabled=reportAccess.Operations||reportAccess.Financial;CompanyNav.IsEnabled=S.Can("Administration.RecoveryCopy")||S.Can("Administration.Manage");LegacyNav.IsEnabled=S.Can("Administration.LegacyCrmImport")&&S.Can("Administration.Manage");HistoricalNav.IsEnabled=S.Can("HistoricalProjects.View")&&S.Can("Clients.View");AdvancedNav.IsExpanded=false;AdvancedNav.Visibility=new[]{AccountingNav,CompanyNav,LegacyNav,HistoricalNav}.Any(b=>b.IsEnabled)?Visibility.Visible:Visibility.Collapsed;Dashboard();if(CheckSession()){LoginPanel.Visibility=Visibility.Collapsed;Workspace.Visibility=Visibility.Visible;SignOut.Visibility=Visibility.Visible;sessionGuard.Start();}}catch(ArgumentException ex){if(authenticated)LockSession("Sign-in setup failed. Sign in again.");else LoginMessage.Text=ex.Message;}catch(Exception){if(authenticated)LockSession("Sign-in setup failed. Sign in again.");else LoginMessage.Text="Operation failed. Check the local development database.";}finally{Password.Clear();Confirm.Clear();}}
 void SignOut_Click(object sender,RoutedEventArgs e)=>LockSession("Signed out.");
 void Dashboard(){DashboardPanel.Visibility=Visibility.Visible;CrmPanel.Visibility=Visibility.Collapsed;Status.Text=AcceptanceStatus();RefreshDashboardCounts();RefreshWorkProfile();RefreshLicenseReminders();}
 void RefreshDashboardCounts(){ClientCount.Text=ProjectSummaryText.Text="";ClientSummaryPanel.Visibility=ProjectSummaryPanel.Visibility=Visibility.Collapsed;if(!authenticated)return;
  try{if(S.Can("Clients.View")){ClientCount.Text=S.ReadClientCount().ToString();ClientSummaryPanel.Visibility=Visibility.Visible;}}catch(UnauthorizedAccessException){}catch(Exception){Status.Text="Client summary unavailable.";}
  try{if(S.Can("Clients.View")&&S.Can("Projects.View")){var counts=S.ReadProjectDashboardCounts();ProjectSummaryText.Text=$"{counts.Active} active · {counts.Pending} pending";ProjectSummaryPanel.Visibility=Visibility.Visible;}}catch(UnauthorizedAccessException){}catch(Exception){Status.Text="Project summary unavailable.";}
 }
 void RefreshWorkProfile()
 {
  WorkProfilePanel.Visibility=WorkProfileButton.Visibility=Visibility.Collapsed;WorkProfileText.Text="";WorkNav.IsEnabled=TimeNav.IsEnabled=false;if(!authenticated)return;
  try{if(!S.Can("Work.Own"))return;var profile=S.ReadOwnWorkProfileStatus();WorkNav.IsEnabled=TimeNav.IsEnabled=profile.State=="Active";if(profile.State=="Active")return;
   WorkProfileText.Text=profile.State=="Inactive"?"Your employee profile is inactive. Contact your administrator.":profile.CanCreate?"Create your employee profile once to use My work and time entry.":"Ask your administrator to link your employee profile before recording work.";
   WorkProfilePanel.Visibility=Visibility.Visible;WorkProfileButton.Visibility=profile.CanCreate?Visibility.Visible:Visibility.Collapsed;
  }catch(UnauthorizedAccessException){}catch(Exception){WorkProfileText.Text="Work profile unavailable. Refresh the dashboard.";WorkProfilePanel.Visibility=Visibility.Visible;}
 }
 void WorkProfile_Click(object sender,RoutedEventArgs e)=>Run(()=>{
  if(!S.ReadOwnWorkProfileStatus().CanCreate)throw new UnauthorizedAccessException();
  var w=new Window{Owner=this,Title="Set up my work profile",Width=480,Height=380,WindowStartupLocation=WindowStartupLocation.CenterOwner};var p=new StackPanel{Margin=new Thickness(18)};
  TextBox Field(string label,int length,string value=""){p.Children.Add(new TextBlock{Text=label});var box=new TextBox{MaxLength=length,Text=value};p.Children.Add(box);return box;}
  var first=Field("First name",60);var last=Field("Last name",60);var email=Field("Email (optional)",150);var title=Field("Job title (optional)",100);var save=new Button{Content="Save profile"};p.Children.Add(save);w.Content=new ScrollViewer{Content=p};
  save.Click+=(_,_)=>{try{S.CreateOwnEmployeeProfile(new(first.Text,last.Text,email.Text,title.Text,true));w.DialogResult=true;}catch(ArgumentException ex){MessageBox.Show(w,ex.Message);}catch(UnauthorizedAccessException){w.DialogResult=false;}catch(Exception){MessageBox.Show(w,"Profile not confirmed. Close and refresh before retrying.");}};
  w.ShowDialog();if(authenticated)RefreshWorkProfile();
 });
 void Time_Click(object sender,RoutedEventArgs e)=>Run(()=>new WorkEntriesWindow(S){Owner=this}.ShowDialog());
 void Legacy_Click(object sender,RoutedEventArgs e)=>Run(()=>new LegacyCrmImportWindow(S){Owner=this}.ShowDialog());
 void Historical_Click(object sender,RoutedEventArgs e)=>Run(()=>new HistoricalProjectWindow(S,workspace?.IsAcceptance==true){Owner=this}.ShowDialog());
 void Company_Click(object sender,RoutedEventArgs e)=>Run(()=>{
  var admin=S.Can("Administration.Manage");var recovery=S.Can("Administration.RecoveryCopy");if(!admin&&!recovery)throw new UnauthorizedAccessException();
  var w=new Window{Owner=this,Title="Company & backups",Width=570,Height=430,WindowStartupLocation=WindowStartupLocation.CenterOwner};var p=new StackPanel{Margin=new Thickness(18)};
  p.Children.Add(new TextBlock{Text="Company",FontSize=22,FontWeight=FontWeights.SemiBold});
  if(admin){var settings=S.ReadCompanySettings();p.Children.Add(new TextBlock{Text="Registered company name"});p.Children.Add(new TextBox{Text=settings.Name,IsReadOnly=true});p.Children.Add(new TextBlock{Text="Client number prefix"});p.Children.Add(new TextBox{Text=settings.ClientPrefix,IsReadOnly=true});p.Children.Add(new TextBlock{Text="Identity and numbering are established at company setup; changes require a reviewed procedure.",TextWrapping=TextWrapping.Wrap});}
  else p.Children.Add(new TextBlock{Text=S.FirmName(),TextWrapping=TextWrapping.Wrap});
  if(recovery){p.Children.Add(new TextBlock{Text="Backups",FontSize=18,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,16,0,4)});p.Children.Add(new TextBlock{Text="Keep confidential recovery copies in protected local storage and retain the verification hash separately.",TextWrapping=TextWrapping.Wrap});var backup=new Button{Content="Create backup"};backup.Click+=(_,_)=>Run(()=>CreateBackup(w));p.Children.Add(backup);if(admin){var restore=new Button{Content="Verify and restore to separate file"};restore.Click+=(_,_)=>Run(()=>RestoreBackup(w));p.Children.Add(restore);}}
  w.Content=new ScrollViewer{Content=p};w.ShowDialog();
 });
 void RestoreBackup(Window owner)
 {
  var open=new Microsoft.Win32.OpenFileDialog{Title="Select trusted recovery copy",Filter="Recovery copy (*.db)|*.db",CheckFileExists=true};if(open.ShowDialog(owner)!=true)return;
  var w=new Window{Owner=owner,Title="Restore to separate file",Width=570,Height=300,WindowStartupLocation=WindowStartupLocation.CenterOwner};var p=new StackPanel{Margin=new Thickness(16)};
  p.Children.Add(new TextBlock{Text="Enter the SHA-256 recorded when this recovery copy was made, from a trusted separate record. This never replaces the active database. Accounting credentials remain absent and posting held.",TextWrapping=TextWrapping.Wrap});
  p.Children.Add(new TextBlock{Text="Original verification hash (SHA-256)"});var hash=new TextBox{MaxLength=64};p.Children.Add(hash);var b=new Button{Content="Choose new destination and verify"};p.Children.Add(b);w.Content=p;
  b.Click+=(_,_)=>{try{var save=new Microsoft.Win32.SaveFileDialog{Title="New isolated restored database",Filter="SQLite database (*.db)|*.db",DefaultExt=".db",FileName="CRC-isolated-restore.db",AddExtension=true,OverwritePrompt=true};if(save.ShowDialog(w)!=true)return;
   var result=S.RestoreRecoveryCopyToNewFile(open.FileName,hash.Text.Trim(),save.FileName);
   MessageBox.Show(w,$"Isolated restore verified.\nClients: {result.Clients}; projects: {result.Projects}; historical projects: {result.HistoricalProjects}; document copies: {result.Documents}\nSHA-256: {result.Sha256}\nCurrent database remains unchanged.","Recovery",MessageBoxButton.OK,MessageBoxImage.Information);w.DialogResult=true;
  }catch(ArgumentException ex){MessageBox.Show(w,ex.Message);}catch(UnauthorizedAccessException){MessageBox.Show(w,"Access denied.");}catch(Exception){MessageBox.Show(w,"Restore failed; no success assumed.");}};
  w.ShowDialog();
 }
 void CreateBackup(Window owner)
 {
  if(!S.Can("Administration.RecoveryCopy"))throw new UnauthorizedAccessException();
  if(MessageBox.Show(owner,"This confidential backup contains firm records, password hashes and documents. Save only to protected local storage. Accounting credentials are removed. The active database remains unchanged.","Create backup",MessageBoxButton.OKCancel,MessageBoxImage.Warning)!=MessageBoxResult.OK)return;
  var dialog=new Microsoft.Win32.SaveFileDialog{Title="Save confidential recovery copy",Filter="SQLite recovery copy (*.db)|*.db",FileName="CRC-recovery-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+".db",AddExtension=true,DefaultExt=".db",OverwritePrompt=true};
  if(dialog.ShowDialog(owner)!=true)return;
  var result=S.CreateRecoveryCopy(dialog.FileName);
  var receipt=new Window{Owner=owner,Title="Backup verified",Width=620,Height=340,WindowStartupLocation=WindowStartupLocation.CenterOwner};var p=new StackPanel{Margin=new Thickness(18)};
  p.Children.Add(new TextBlock{Text="Backup saved and verified. The active database is unchanged.",TextWrapping=TextWrapping.Wrap});p.Children.Add(new TextBlock{Text="Saved file"});p.Children.Add(new TextBox{Text=result.Path,IsReadOnly=true,TextWrapping=TextWrapping.Wrap});p.Children.Add(new TextBlock{Text=$"Size: {result.Bytes:N0} bytes"});p.Children.Add(new TextBlock{Text="Verification hash (keep in a separate trusted record)"});p.Children.Add(new TextBox{Text=result.Sha256,IsReadOnly=true,TextWrapping=TextWrapping.Wrap});p.Children.Add(new TextBlock{Text="Select and copy the hash before closing. The hash verifies integrity; it does not encrypt the backup.",TextWrapping=TextWrapping.Wrap});var done=new Button{Content="Done",IsDefault=true};done.Click+=(_,_)=>receipt.Close();p.Children.Add(done);receipt.Content=new ScrollViewer{Content=p};receipt.ShowDialog();
 }
 void Reports_Click(object sender,RoutedEventArgs e)=>Run(()=>new ProjectReportsWindow(S){Owner=this}.ShowDialog());
 void Invoice_Click(object sender,RoutedEventArgs e)=>Run(()=>new InvoicePreparationWindow(S){Owner=this}.ShowDialog());
 void Review_Click(object sender,RoutedEventArgs e)=>Run(()=>new WorkReviewWindow(S){Owner=this}.ShowDialog());
 void Work_Click(object sender,RoutedEventArgs e)=>Run(()=>new WorkScheduleWindow(S,true){Owner=this}.ShowDialog());
 void Schedule_Click(object sender,RoutedEventArgs e)=>Run(()=>new WorkScheduleWindow(S,false){Owner=this}.ShowDialog());
 void RefreshLicenseReminders()
 {
  LicenseReminderText.Text="";LicenseReminderPanel.Visibility=Visibility.Collapsed;
  if(!authenticated)return;
  try{
   if(!S.Can("Employees.View"))return;
   var counts=S.ReadLicenseReminderCounts(DateOnly.FromDateTime(DateTime.Today));
   LicenseReminderText.Text=$"{counts.Expired} expired • {counts.DueToday} due today • {counts.Upcoming} due within the next 30 days. Recorded dates only; verify regulatory status separately.";
   LicenseReminderPanel.Visibility=counts.Expired+counts.DueToday+counts.Upcoming>0?Visibility.Visible:Visibility.Collapsed;
  }catch(UnauthorizedAccessException){}catch(Exception){if(authenticated){LicenseReminderText.Text="License reminder unavailable. Review the license register.";LicenseReminderPanel.Visibility=Visibility.Visible;}}
 }
 void LicenseReminder_Click(object sender,RoutedEventArgs e)=>Run(()=>{if(!S.Can("Employees.View"))throw new UnauthorizedAccessException();new LicenseExpiryWindow(S){Owner=this}.ShowDialog();RefreshLicenseReminders();});
 void Staff_Click(object sender,RoutedEventArgs e)=>Run(()=>{new StaffWindow(S){Owner=this}.ShowDialog();RefreshWorkProfile();RefreshLicenseReminders();});
 void RequiredPassword_Click(object sender,RoutedEventArgs e)
 {
  if(setup||authenticated||locking||lockFault||operationDepth!=0)return;var w=new Window{Owner=this,Title="Change temporary password",Width=440,Height=290,WindowStartupLocation=WindowStartupLocation.CenterOwner};var p=new StackPanel{Margin=new Thickness(18)};p.Children.Add(new TextBlock{Text="Enter current temporary password on the sign-in screen first. Choose a different permanent password." ,TextWrapping=TextWrapping.Wrap});var next=new PasswordBox{MaxLength=256};var confirm=new PasswordBox{MaxLength=256};p.Children.Add(new TextBlock{Text="New password (14–256 characters)"});p.Children.Add(next);p.Children.Add(new TextBlock{Text="Confirm"});p.Children.Add(confirm);var b=new Button{Content="Change password"};p.Children.Add(b);w.Content=p;
  b.Click+=(_,_)=>{try{if(next.Password!=confirm.Password)throw new ArgumentException("Passwords do not match.");if(S.ChangeRequiredPassword(Username.Text,Password.Password,next.Password)){w.DialogResult=true;}else MessageBox.Show(w,"Password change unsuccessful. Verify temporary credentials or try later.");}catch(ArgumentException ex){MessageBox.Show(w,ex.Message);}catch(Exception){MessageBox.Show(w,"Password change failed.");}finally{next.Clear();confirm.Clear();}};
  if(w.ShowDialog()==true)LoginMessage.Text="Password changed. Sign in with your new password.";Password.Clear();
 }
 void Engagement_Click(object sender,RoutedEventArgs e)=>Run(()=>{new EngagementWindow(S){Owner=this}.ShowDialog();if(authenticated)RefreshDashboardCounts();});
 void Accounting_Click(object sender,RoutedEventArgs e)=>Run(()=>{if(!S.CanTestAccountingConnection())throw new UnauthorizedAccessException();new AccountingConnectionWindow(S){Owner=this}.ShowDialog();});
 void Dashboard_Click(object sender,RoutedEventArgs e)=>Run(Dashboard);
 void Crm_Click(object sender,RoutedEventArgs e)=>Run(()=>{DashboardPanel.Visibility=Visibility.Collapsed;CrmPanel.Visibility=Visibility.Visible;RefreshClients();});
 void Search_Changed(object sender,TextChangedEventArgs e){if(service!=null&&CrmPanel?.Visibility==Visibility.Visible)Run(()=>RefreshClients());}
 void RefreshClients(long? select=null){ClientGrid.ItemsSource=null;ContactGrid.ItemsSource=null;var rows=S.SearchClients(Search.Text);ClientGrid.ItemsSource=rows;if(select!=null)ClientGrid.SelectedItem=rows.FirstOrDefault(x=>x.Id==select);NewClientButton.IsEnabled=S.Can("Clients.Edit");UpdateContacts();}
 void Client_Changed(object sender,SelectionChangedEventArgs e){if(service!=null&&CrmPanel?.Visibility==Visibility.Visible)Run(UpdateContacts);}
 void Contact_Changed(object sender,SelectionChangedEventArgs e){if(service!=null&&CrmPanel?.Visibility==Visibility.Visible)Run(UpdateButtons);}
 void UpdateButtons(){var client=ClientGrid.SelectedItem as Client;EditClientButton.IsEnabled=client!=null&&S.Can("Clients.Edit");var edit=client?.Status=="Active"&&S.Can("Contacts.View")&&S.Can("Contacts.Edit");NewContactButton.IsEnabled=edit;EditContactButton.IsEnabled=edit&&ContactGrid.SelectedItem is Contact;}
 void UpdateContacts(){var client=ClientGrid.SelectedItem as Client;ContactGrid.ItemsSource=null;ContactsHeading.Text=client==null?"Select a client to view contacts":$"Contacts · {client.DisplayName}";if(client!=null&&S.Can("Contacts.View"))ContactGrid.ItemsSource=S.Contacts(client.Id);UpdateButtons();}
 void NewClient_Click(object sender,RoutedEventArgs e)=>Run(()=>EditClient(null));
 void EditClient_Click(object sender,RoutedEventArgs e)=>Run(()=>{if(ClientGrid.SelectedItem is Client x)EditClient(x);});
 void EditClient(Client? x){if(!S.Can("Clients.Edit"))throw new UnauthorizedAccessException();var fields=new (string,string)[]{("Client type",x?.Type??"Business / Company"),("Company",x?.Company??""),("First name",x?.First??""),("Last name",x?.Last??""),("Email",x?.Email??""),("Phone",x?.Phone??""),("Address 1",x?.Address1??""),("Address 2",x?.Address2??""),("City",x?.City??""),("State",x?.State??""),("ZIP",x?.Zip??""),("Notes",x?.Notes??""),("Status (Active / Inactive)",x?.Status??"Active")};long? savedId=null;if(EditDialog(x==null?"New client":"Edit client",fields,v=>{savedId=S.SaveClient(new Client(x?.Id??0,x?.Number??"",v[0],v[1],v[2],v[3],v[4],v[5],v[6],v[7],v[8],v[9],v[10],v[11],v[12]));})){Search.Text="";RefreshClients(savedId);}}
 void NewContact_Click(object sender,RoutedEventArgs e)=>Run(()=>EditContact(null));
 void EditContact_Click(object sender,RoutedEventArgs e)=>Run(()=>{if(ContactGrid.SelectedItem is Contact x)EditContact(x);});
 void EditContact(Contact? x){if(ClientGrid.SelectedItem is not Client client)return;if(!S.Can("Contacts.Edit"))throw new UnauthorizedAccessException();var fields=new (string,string)[]{("First name",x?.First??""),("Last name",x?.Last??""),("Title",x?.Title??""),("Email",x?.Email??""),("Phone",x?.Phone??""),("Primary (Yes / No)",x?.Primary==true?"Yes":"No")};if(EditDialog("Contact · "+client.DisplayName,fields,v=>{if(!string.Equals(v[5],"Yes",StringComparison.OrdinalIgnoreCase)&&!string.Equals(v[5],"No",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Enter Yes or No for Primary.");S.SaveContact(new Contact(x?.Id??0,client.Id,v[0],v[1],v[2],v[3],v[4],string.Equals(v[5],"Yes",StringComparison.OrdinalIgnoreCase)));}))UpdateContacts();}
 bool EditDialog(string title,(string Label,string Value)[] fields,Action<string[]> save){var dialog=new Window{Owner=this,Title=title,Width=480,Height=Math.Min(710,150+fields.Length*64),WindowStartupLocation=WindowStartupLocation.CenterOwner,ShowInTaskbar=false,Background=System.Windows.Media.Brushes.White};var panel=new DockPanel{Margin=new Thickness(20)};var footer=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};DockPanel.SetDock(footer,Dock.Bottom);var ok=new Button{Content="Save",IsDefault=true};var cancel=new Button{Content="Cancel",IsCancel=true};footer.Children.Add(ok);footer.Children.Add(cancel);panel.Children.Add(footer);var stack=new StackPanel();var getters=new List<Func<string>>();foreach(var (label,value) in fields){stack.Children.Add(new TextBlock{Text=label});string[]? choices=label=="Client type"?PracticeManager.Core.Validation.ClientTypes.ToArray():label.StartsWith("Status (")?new[]{"Active","Inactive"}:label.StartsWith("Primary (")?new[]{"Yes","No"}:null;if(choices!=null){var combo=new ComboBox{ItemsSource=choices,SelectedItem=value,Margin=new Thickness(0,3,0,8),Padding=new Thickness(7)};getters.Add(()=>combo.SelectedItem as string??"");stack.Children.Add(combo);}else{var isPhone=label=="Phone";var box=new TextBox{Text=isPhone?PracticeManager.Core.Validation.PhoneForDisplay(value):value};if(isPhone){box.MaxLength=40;box.ToolTip="10 digits, optional +1; format (XXX)XXX-XXXX";box.LostFocus+=(_,_)=>{box.Text=PracticeManager.Core.Validation.PhoneForDisplay(box.Text);};}getters.Add(()=>isPhone?PracticeManager.Core.Validation.Phone(box.Text):box.Text);stack.Children.Add(box);}}panel.Children.Add(new ScrollViewer{Content=stack,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});dialog.Content=panel;ok.Click+=(_,_)=>{try{save(getters.Select(g=>g()).ToArray());dialog.DialogResult=true;}catch(ArgumentException ex){MessageBox.Show(dialog,ex.Message,"Check entry");}catch(UnauthorizedAccessException){MessageBox.Show(dialog,"Permission denied.");}catch(Exception){MessageBox.Show(dialog,"Operation failed; refresh the register before retrying.");}};return dialog.ShowDialog()==true;}
}
