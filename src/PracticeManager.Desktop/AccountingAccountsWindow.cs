using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using PracticeManager.Core;
using PracticeManager.Persistence;
namespace PracticeManager.Desktop;
internal sealed class AccountingAccountsWindow:Window
{
 readonly LocalPractice service;
 readonly DataGrid grid=new(){AutoGenerateColumns=false,IsReadOnly=true,CanUserAddRows=false,CanUserDeleteRows=false,ClipboardCopyMode=DataGridClipboardCopyMode.None,Margin=new Thickness(0,8,0,8)};
 readonly TextBox filter=new(){Margin=new Thickness(0,8,0,0),MaxLength=512};
 readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,0)};
 readonly TextBox report=new(){IsReadOnly=true,TextWrapping=TextWrapping.Wrap,AcceptsReturn=true,Height=100,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
 readonly Button refresh=new(){Content="Refresh accounts",Padding=new Thickness(12,6,12,6)};
 AccountingAccountRegister? current;string? revision;CancellationTokenSource? pending;bool closed;
 public AccountingAccountsWindow(LocalPractice service)
 {
  this.service=service;Title="Chart of accounts — read-only development preview";Width=880;Height=650;MinWidth=580;MinHeight=420;WindowStartupLocation=WindowStartupLocation.CenterOwner;ShowInTaskbar=false;
  var panel=new DockPanel{Margin=new Thickness(20)};var top=new StackPanel();
  top.Children.Add(new TextBlock{Text="Chart of accounts",FontSize=23,FontWeight=FontWeights.SemiBold});
  top.Children.Add(new TextBlock{Text="Synthetic business only. No account imports, mappings or changes. Account code may be blank. Share only the sanitized report.",TextWrapping=TextWrapping.Wrap});
  top.Children.Add(status);top.Children.Add(new TextBlock{Text="Filter by name or code:"});top.Children.Add(filter);
  DockPanel.SetDock(top,Dock.Top);panel.Children.Add(top);
  var bottom=new StackPanel();var nav=new StackPanel{Orientation=Orientation.Horizontal};nav.Children.Add(refresh);
  var close=new Button{Content="Close",Padding=new Thickness(12,6,12,6),Margin=new Thickness(8,0,0,0)};close.Click+=(_,_)=>Close();nav.Children.Add(close);bottom.Children.Add(nav);
  bottom.Children.Add(new TextBlock{Text="Sanitized verification report",Margin=new Thickness(0,8,0,4)});bottom.Children.Add(report);DockPanel.SetDock(bottom,Dock.Bottom);panel.Children.Add(bottom);
  grid.Columns.Add(new DataGridTextColumn{Header="Code",Binding=new Binding("Code"),Width=new DataGridLength(1,DataGridLengthUnitType.Star)});
  grid.Columns.Add(new DataGridTextColumn{Header="Account name",Binding=new Binding("Name"),Width=new DataGridLength(2,DataGridLengthUnitType.Star)});
  grid.Columns.Add(new DataGridTextColumn{Header="Manager account UUID",Binding=new Binding("Key"),Width=new DataGridLength(2,DataGridLengthUnitType.Star)});
  panel.Children.Add(grid);Content=panel;
  filter.TextChanged+=(_,_)=>ApplyFilter();
  refresh.Click+=async (_,_)=>await LoadAsync();Loaded+=async (_,_)=>await LoadAsync();
  Closed+=(_,_)=>{closed=true;pending?.Cancel();grid.ItemsSource=null;current=null;report.Clear();};
 }
 void ApplyFilter(){var term=filter.Text.Trim();grid.ItemsSource=current?.Rows.Where(r=>r.Name.Contains(term,StringComparison.OrdinalIgnoreCase)||(r.Code?.Contains(term,StringComparison.OrdinalIgnoreCase)??false)).ToList();}
 async Task LoadAsync()
 {
  if(pending!=null||closed)return;
  using var cts=new CancellationTokenSource();pending=cts;refresh.IsEnabled=false;
  var prior=current;var priorRevision=revision;current=null;revision=null;grid.ItemsSource=null;report.Clear();status.Text="Retrieving accounts...";
  try{
   var r=await service.ReadSavedAccountsAsync(cts.Token);if(closed)return;
   if(!r.Success||r.Accounts==null){status.Foreground=Brushes.DarkRed;status.Text="Account read failed: "+r.Outcome+". No accounts displayed.";return;}
   current=r.Accounts;revision=r.ProfileRevision;ApplyFilter();
   status.Foreground=Brushes.DarkGreen;status.Text=$"HTTP 200 — {current.Rows.Count} accounts. No records imported.";
   var repeat=prior!=null&&revision==priorRevision&&current.BusinessName==prior.BusinessName;
   report.Text=$"HTTP 200 | Accounts: {current.Rows.Count}\nUnique UUIDs: {current.Rows.Select(x=>x.Key).Distinct().Count()}\nBlank codes: {current.Rows.Count(x=>string.IsNullOrEmpty(x.Code))}\nRepeated account UUIDs: "+(repeat?(prior!.Rows.Select(x=>x.Key).ToHashSet().SetEquals(current.Rows.Select(x=>x.Key))?"Same set":"Changed set"):"Refresh accounts to compare.")+"\nIdentity is not certified. No account mappings or writes.";
  }catch(UnauthorizedAccessException){if(!closed)status.Text="Access denied. Sign in again.";}
  catch(Exception){if(!closed){current=null;revision=null;grid.ItemsSource=null;status.Text="Account read failed. No accounts displayed.";}}
  finally{pending=null;if(!closed)refresh.IsEnabled=true;}
 }
}
