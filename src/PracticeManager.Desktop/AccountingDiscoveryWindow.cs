using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PracticeManager.Core;
using PracticeManager.Persistence;

namespace PracticeManager.Desktop;
internal sealed class AccountingDiscoveryWindow : Window
{
 readonly LocalPractice service;
 readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,8)};
 readonly TextBox report=new(){IsReadOnly=true,TextWrapping=TextWrapping.Wrap,AcceptsReturn=true,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
 readonly Button run=new(){Content="Run all read-only checks",Padding=new Thickness(12,8,12,8),Margin=new Thickness(0,0,8,0)};
 CancellationTokenSource? pending;
 bool closed;
 public AccountingDiscoveryWindow(LocalPractice service)
 {
  this.service=service;Title="Accounting integration discovery — synthetic test only";Width=760;Height=660;MinWidth=500;MinHeight=400;WindowStartupLocation=WindowStartupLocation.CenterOwner;ShowInTaskbar=false;Background=Brushes.White;
  var panel=new DockPanel{Margin=new Thickness(20)};
  var top=new StackPanel();
  top.Children.Add(new TextBlock{Text="Integration schema discovery",FontSize=23,FontWeight=FontWeights.SemiBold});
  top.Children.Add(new TextBlock{Text="Checks custom field definitions, chart of accounts, sales invoices, receipts and payments in the saved test business. No values, transactions or credentials are displayed. No accounting writes.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,6,0,8)});
  top.Children.Add(status);DockPanel.SetDock(top,Dock.Top);panel.Children.Add(top);
  var nav=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,12,0,0)};
  nav.Children.Add(run);
  var close=new Button{Content="Close",Padding=new Thickness(12,8,12,8)};close.Click+=(_,_)=>Close();nav.Children.Add(close);
  DockPanel.SetDock(nav,Dock.Bottom);panel.Children.Add(nav);panel.Children.Add(report);Content=panel;
  run.Click+=async (_,_)=>await RunAsync();
  Closed+=(_,_)=>{closed=true;pending?.Cancel();report.Clear();};
 }
 async Task RunAsync()
 {
  if(pending!=null||closed)return;
  using var cts=new CancellationTokenSource();pending=cts;run.IsEnabled=false;report.Clear();status.Foreground=Brushes.DimGray;
  var sections=new List<string>();string? revision=null;var successes=0;
  try{
   foreach(var resource in Enum.GetValues<AccountingDiscoveryResource>()){
    status.Text="Checking "+resource+"...";
    var result=await service.InspectSavedResourceAsync(resource,cts.Token);
    if(closed)return;
    if(result.Outcome=="ProfileChanged"||(result.Success&&revision!=null&&result.ProfileRevision!=revision)){
     report.Clear();status.Text="Saved connection changed. Reopen this window and run again.";return;
    }
    if(result.Success)revision=result.ProfileRevision;
    if(result.Success&&result.SchemaSummary!=null){successes++;sections.Add(resource+" — HTTP 200"+Environment.NewLine+result.SchemaSummary);}
    else sections.Add(resource+" — "+result.Outcome+(result.StatusCode.HasValue?" (HTTP "+result.StatusCode+")":""));
    report.Text=string.Join(Environment.NewLine+Environment.NewLine,sections);
   }
   status.Foreground=successes==5?Brushes.DarkGreen:Brushes.DarkOrange;
   status.Text=$"{successes}/5 endpoints returned JSON. Schema evidence only; no synchronization or business identity certification.";
  }catch(UnauthorizedAccessException){if(!closed){report.Clear();status.Text="Access denied. Sign in again.";}}
  catch(Exception){if(!closed){report.Clear();status.Text="Discovery failed. Reopen and retry.";}}
  finally{pending=null;if(!closed)run.IsEnabled=true;}
 }
}
