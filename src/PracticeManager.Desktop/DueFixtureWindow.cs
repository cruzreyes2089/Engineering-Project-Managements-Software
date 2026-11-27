using System.Windows;
using System.Windows.Controls;
using PracticeManager.Core;
using PracticeManager.Persistence;
namespace PracticeManager.Desktop;
internal sealed class DueFixtureWindow:Window
{
 readonly LocalPractice service;readonly TextBox endpoint=new(){Text="http://127.0.0.1:55667/api2",MaxLength=80};readonly PasswordBox token=new(){MaxLength=4096};
 readonly CheckBox confirmed=new(){Content="I verified CRC DUE DATE TEST is open and USD only."};
 readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap};
 readonly DataGrid grid=WorkWindowFields.Grid();readonly Button run=new(){Content="Observe fixture and calculate overdue balances"};
 CancellationTokenSource? pending;bool closed;
 public DueFixtureWindow(LocalPractice service)
 {
  this.service=service;Title="Due-date fixture review — read only";Width=950;Height=600;WindowStartupLocation=WindowStartupLocation.CenterOwner;
  var root=new DockPanel{Margin=new Thickness(16)};var p=new StackPanel();
  p.Children.Add(new TextBlock{Text="Dedicated three-invoice USD test only. Credential used once, not stored. No reminder, posting, or change to saved profile.",TextWrapping=TextWrapping.Wrap});
  p.Children.Add(new TextBlock{Text="Loopback API endpoint"});p.Children.Add(endpoint);p.Children.Add(new TextBlock{Text="API token"});p.Children.Add(token);p.Children.Add(confirmed);p.Children.Add(run);p.Children.Add(status);
  DockPanel.SetDock(p,Dock.Top);root.Children.Add(p);WorkWindowFields.Columns(grid,("Reference","Reference"),("Issued","Issued"),("Due date","DueDate"),("Outstanding USD","Outstanding"),("Days overdue","DaysOverdue"),("Status","Status"));root.Children.Add(grid);Content=root;
  run.Click+=async(_,_)=>await Read();Closed+=(_,_)=>{closed=true;pending?.Cancel();token.Clear();grid.ItemsSource=null;};
  endpoint.TextChanged+=(_,_)=>Invalidate();confirmed.Unchecked+=(_,_)=>Invalidate();token.PasswordChanged+=(_,_)=>{if(pending==null)Invalidate();};
 }
 void Invalidate(){pending?.Cancel();grid.ItemsSource=null;status.Text="";}
 async Task Read()
 {
  if(pending!=null)return;grid.ItemsSource=null;
  using var ct=new CancellationTokenSource();pending=ct;run.IsEnabled=false;endpoint.IsEnabled=false;token.IsEnabled=false;confirmed.IsEnabled=false;status.Text="Observing fixture...";
  try{
   var result=await service.ReadDueDateFixtureAsync(endpoint.Text,token.Password,confirmed.IsChecked==true,ct.Token);token.Clear();
   if(closed||ct.IsCancellationRequested)return;
   if(result.Outcome!="ObservedForReview"||result.DueDates==null){status.Text="Blocked: "+result.Outcome;return;}
   var day=DateOnly.FromDateTime(DateTime.Today);var report=CollectionDueAging.Calculate(result,result.DueDates,day);
   grid.ItemsSource=report.Rows;status.Text=$"Observed UTC: {result.ObservedUtc:u}; evaluated {day:yyyy-MM-dd}. Outstanding USD {report.Outstanding:0.00}; overdue {report.KnownOverdue:0.00}; not yet due {report.NotYetDue:0.00}; due today {report.DueToday:0.00}; unknown due {report.UnknownDue:0.00}. Synthetic review only.";
  }catch(UnauthorizedAccessException){if(!closed)status.Text="Access denied.";}catch(Exception){if(!closed)status.Text="Fixture review unavailable.";}
  finally{token.Clear();pending=null;if(!closed){run.IsEnabled=true;endpoint.IsEnabled=true;token.IsEnabled=true;confirmed.IsEnabled=true;}}
 }
}
