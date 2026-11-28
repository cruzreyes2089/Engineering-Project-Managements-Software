using System.Windows;
using System.Windows.Controls;
using PracticeManager.Core;
using PracticeManager.Persistence;
namespace PracticeManager.Desktop;
internal sealed class CollectionsReviewWindow:Window
{
 readonly LocalPractice service;
 readonly DataGrid grid=WorkWindowFields.Grid();
 readonly CheckBox currency=new(){Content="I verified the synthetic Manager business uses USD only."};
 readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap},summary=new(){TextWrapping=TextWrapping.Wrap};
 readonly Button run=new(){Content="Acquire current invoice balances",Margin=new Thickness(0,8,0,8)};
 CancellationTokenSource? pending;bool closed;
 public CollectionsReviewWindow(LocalPractice service)
 {
  this.service=service;Title="Collections review — synthetic read-only";Width=950;Height=600;WindowStartupLocation=WindowStartupLocation.CenterOwner;
  var root=new DockPanel{Margin=new Thickness(16)};var head=new StackPanel();head.Children.Add(new TextBlock{Text=Title,FontSize=24,FontWeight=FontWeights.SemiBold});
  head.Children.Add(new TextBlock{Text="Manager invoice balances observed twice. Age is measured from issue date to today's local date, not days overdue. Payment terms and due dates are not yet qualified. No reminders, posting or local receivable ledger.",TextWrapping=TextWrapping.Wrap});
  head.Children.Add(currency);head.Children.Add(run);var due=new Button{Content="Review dedicated due-date fixture",Margin=new Thickness(0,4,0,4)};due.Click+=(_,_)=>new DueFixtureWindow(service){Owner=this}.ShowDialog();head.Children.Add(due);head.Children.Add(status);head.Children.Add(summary);DockPanel.SetDock(head,Dock.Top);root.Children.Add(head);
  WorkWindowFields.Columns(grid,("Reference","Reference"),("Issued","Issued"),("Invoice USD","Amount"),("Outstanding USD","Outstanding"),("Days since issue","DaysSinceIssue"),("Age band","AgeBand"));root.Children.Add(grid);Content=root;
  run.Click+=async(_,_)=>await RefreshAsync();Closed+=(_,_)=>{closed=true;pending?.Cancel();grid.ItemsSource=null;};
  currency.Unchecked+=(_,_)=>{pending?.Cancel();grid.ItemsSource=null;summary.Text="";status.Text="Currency confirmation required.";};
 }
 async Task RefreshAsync()
 {
  if(pending!=null)return;grid.ItemsSource=null;summary.Text="";
  using var ct=new CancellationTokenSource();pending=ct;run.IsEnabled=false;status.Text="Acquiring marker and invoice evidence...";
  try{
   var result=await service.ReadSavedCollectionsAsync(currency.IsChecked==true,ct.Token);
   if(closed||ct.IsCancellationRequested)return;
   if(result.Outcome!="ObservedForReview"){status.Text="Blocked: "+result.Outcome;return;}
   var day=DateOnly.FromDateTime(DateTime.Today);var report=CollectionAging.Calculate(result,day);
   grid.ItemsSource=report.Rows;status.Text=$"Observed UTC: {result.ObservedUtc:u}; age date: {day:yyyy-MM-dd}. Not a historical snapshot.";
   summary.Text=$"Outstanding USD {report.Outstanding:0.00} | Future {report.FutureDated:0.00} | 0–30 {report.Age0To30:0.00} | 31–60 {report.Age31To60:0.00} | 61–90 {report.Age61To90:0.00} | 91+ {report.AgeOver90:0.00}";
  }catch(UnauthorizedAccessException){if(!closed)status.Text="Access denied.";}catch(Exception){if(!closed){grid.ItemsSource=null;summary.Text="";status.Text="Review unavailable.";}}
  finally{pending=null;if(!closed)run.IsEnabled=true;}
 }
}
