using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using PracticeManager.Core;
using PracticeManager.Persistence;
namespace PracticeManager.Desktop;
internal sealed class AccountingDirectoryWindow:Window
{
 readonly LocalPractice service;
 readonly DataGrid grid=new(){AutoGenerateColumns=false,IsReadOnly=true,CanUserAddRows=false,CanUserDeleteRows=false,ClipboardCopyMode=DataGridClipboardCopyMode.None,Height=180,Margin=new Thickness(0,8,0,8)};
 readonly TextBox report=new(){IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,Height=110,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
 readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,8)};
 readonly Button sync=new(){Content="Synchronize customers to local directory",Margin=new Thickness(0,6,0,0)};
 readonly Button load=new(){Content="Load accounts for role selection",Margin=new Thickness(0,6,0,0)};
 readonly Button save=new(){Content="Save provisional account role",Margin=new Thickness(0,6,0,0)};
 readonly Button remove=new(){Content="Remove selected role",Margin=new Thickness(0,6,0,0)};
 readonly ComboBox role=new(){ItemsSource=new[]{"ServiceIncome","OfficeExpense"},SelectedIndex=0,Margin=new Thickness(0,6,0,0)};
 readonly ComboBox accounts=new(){DisplayMemberPath="Label",Margin=new Thickness(0,6,0,0)};
 readonly TextBlock assignments=new(){TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,0)};
 sealed record Choice(AccountingAccount Account,string Label);
 DirectoryReport? current;string? accountRevision;CancellationTokenSource? pending;bool closed;
 public AccountingDirectoryWindow(LocalPractice service)
 {
  this.service=service;Title="Customer synchronization and account roles — development";Width=760;SizeToContent=SizeToContent.Height;MaxHeight=SystemParameters.WorkArea.Height-40;WindowStartupLocation=WindowStartupLocation.CenterOwner;ShowInTaskbar=false;
  var panel=new StackPanel{Margin=new Thickness(20)};
  panel.Children.Add(new TextBlock{Text="Customer directory and account roles",FontSize=23,FontWeight=FontWeights.SemiBold});
  panel.Children.Add(new TextBlock{Text="Synthetic business only. Synchronization updates a separate local directory, never CRM or Manager. Missing customers are retained and flagged. Cached data and account role selections are not approved for posting.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,0)});
  panel.Children.Add(sync);panel.Children.Add(status);
  foreach(var (header,path) in new[]{("Customer name","Name"),("Accounting status","Status"),("Present at last sync","Present"),("Linked locally","Linked")})
   grid.Columns.Add(new DataGridTextColumn{Header=header,Binding=new Binding(path),Width=new DataGridLength(1,DataGridLengthUnitType.Star)});
  panel.Children.Add(grid);panel.Children.Add(report);
  panel.Children.Add(new TextBlock{Text="Provisional account roles (manual classification)",FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,12,0,0)});
  panel.Children.Add(new TextBlock{Text="Choose the income or office expense account only after checking its classification in Manager. CRC cannot yet verify account type. No posting uses these assignments.",TextWrapping=TextWrapping.Wrap});
  panel.Children.Add(load);panel.Children.Add(role);panel.Children.Add(accounts);panel.Children.Add(save);panel.Children.Add(remove);panel.Children.Add(assignments);
  var close=new Button{Content="Close",Margin=new Thickness(0,12,0,0)};close.Click+=(_,_)=>Close();panel.Children.Add(close);Content=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
  sync.Click+=async (_,_)=>await Run("sync");load.Click+=async (_,_)=>await Run("accounts");save.Click+=async (_,_)=>await Run("save");remove.Click+=async (_,_)=>await Run("remove");
  accounts.SelectionChanged+=(_,_)=>Enable();role.SelectionChanged+=(_,_)=>Enable();
  Closed+=(_,_)=>{closed=true;pending?.Cancel();grid.ItemsSource=null;accounts.ItemsSource=null;current=null;};
  try{Apply(service.ReadCachedDirectory());}catch{status.Text="Cached directory unavailable.";}
  Enable();
 }
 void Apply(DirectoryResult result)
 {
  if(result.Report==null){status.Text=result.Outcome+". Existing cache not changed.";return;}
  current=result.Report;grid.ItemsSource=current.Customers;
  status.Text=result.Outcome=="Cached"?"Cached only — not reverified against Manager.":"Result: "+result.Outcome;
  report.Text=$"Directory rows present: {current.Customers.Count(x=>x.Present)}\nMissing historical rows: {current.Customers.Count(x=>!x.Present)}\nPresent linked: {current.Customers.Count(x=>x.Present&&x.Linked)}\nPresent unlinked: {current.Customers.Count(x=>x.Present&&!x.Linked)}\nMissing linked customers: {current.MissingLinked}\nActive CRC clients without link: {current.ActiveUnlinked}\nProvisional account roles: {current.Roles.Count}\nNo CRM or Manager records changed.";
  assignments.Text=string.Join(Environment.NewLine,current.Roles.Select(x=>x.Role+" — "+(x.Code??"")+" "+x.Name+" (last selected; not verified classification)"));
 }
 void Enable()
 {
  var idle=pending==null&&!closed;
  sync.IsEnabled=idle;load.IsEnabled=idle&&current!=null;role.IsEnabled=idle&&current!=null;accounts.IsEnabled=idle&&accountRevision==current?.Revision;
  save.IsEnabled=idle&&current!=null&&accountRevision==current.Revision&&accounts.SelectedItem is Choice;
  remove.IsEnabled=idle&&current!=null&&role.SelectedItem is string;
 }
 async Task Run(string action)
 {
  if(pending!=null||closed)return;
  var selection=accounts.SelectedItem as Choice;var selectedRole=role.SelectedItem as string;var revision=current?.Revision;
  if(action=="save"&&(selection==null||selectedRole==null||MessageBox.Show(this,"Save this provisional local role only? Confirm that the account classification is correct in Manager.","Confirm role",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes))return;
  using var cts=new CancellationTokenSource();pending=cts;Enable();status.Text="Working...";
  try{
   if(action=="sync"){
    accounts.ItemsSource=null;accountRevision=null;
    var r=await service.SyncCustomerDirectoryAsync(cts.Token);if(!closed)Apply(r);
   }else if(action=="accounts"){
    accountRevision=null;accounts.ItemsSource=null;
    var r=await service.ReadSavedAccountsAsync(cts.Token);if(closed)return;
    if(!r.Success||r.Accounts==null||r.ProfileRevision!=revision){status.Text="Account list unavailable: "+r.Outcome;return;}
    accountRevision=r.ProfileRevision;accounts.ItemsSource=r.Accounts.Rows.Select(x=>new Choice(x,(x.Code??"")+" "+x.Name)).ToList();status.Text="Select a role and an account.";
   }else if(action=="save"&&selection!=null&&selectedRole!=null&&revision!=null){
    var r=await service.SaveProvisionalAccountRoleAsync(revision,selectedRole,selection.Account,cts.Token);if(!closed)Apply(r);
   }else if(action=="remove"&&revision!=null&&selectedRole!=null)Apply(service.RemoveProvisionalAccountRole(revision,selectedRole));
  }catch(UnauthorizedAccessException){if(!closed)status.Text="Access denied.";}
  catch(Exception){if(!closed)status.Text="Operation failed; no successful change assumed.";}
  finally{pending=null;if(!closed)Enable();}
 }
}
