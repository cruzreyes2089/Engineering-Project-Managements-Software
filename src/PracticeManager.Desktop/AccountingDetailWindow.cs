using System.Windows;
using System.Windows.Controls;
using PracticeManager.Persistence;
namespace PracticeManager.Desktop;
internal sealed class AccountingDetailWindow:Window
{
 readonly LocalPractice service;
 readonly Button run=new(){Content="Run document detail checks",Padding=new Thickness(12,8,12,8),Margin=new Thickness(0,10,0,8)};
 readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,4,0,8)};
 readonly TextBox report=new(){IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Height=390};
 CancellationTokenSource? pending;bool closed;
 public AccountingDetailWindow(LocalPractice service)
 {
  this.service=service;Title="Financial document detail discovery — synthetic only";Width=740;SizeToContent=SizeToContent.Height;MaxHeight=SystemParameters.WorkArea.Height-40;WindowStartupLocation=WindowStartupLocation.CenterOwner;ShowInTaskbar=false;
  var panel=new StackPanel{Margin=new Thickness(20)};
  panel.Children.Add(new TextBlock{Text="Financial document detail checks",FontSize=23,FontWeight=FontWeights.SemiBold});
  panel.Children.Add(new TextBlock{Text="Uses the existing test invoice, receipt, payment and two provisional account roles. Checks the known fixture totals, then reports field names and types only. No record values, UUIDs, tokens, imports or Manager writes.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,0)});
  panel.Children.Add(run);panel.Children.Add(status);panel.Children.Add(report);
  var close=new Button{Content="Close",Margin=new Thickness(0,12,0,0)};close.Click+=(_,_)=>Close();panel.Children.Add(close);Content=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
  run.Click+=async (_,_)=>await Run();Closed+=(_,_)=>{closed=true;pending?.Cancel();report.Clear();};
 }
 async Task Run(){
  if(pending!=null)return;
  using var cts=new CancellationTokenSource();pending=cts;run.IsEnabled=false;report.Clear();status.Text="Reading synthetic document details...";
  try{var r=await service.InspectFinancialDetailsAsync(cts.Token);if(closed)return;status.Text=r.Outcome=="Connected"?"Five detail checks completed. Copy the report.":"Detail checks blocked: "+r.Outcome;report.Text=r.Report??"";}
  catch(UnauthorizedAccessException){if(!closed)status.Text="Access denied.";}
  catch(Exception){if(!closed){report.Clear();status.Text="Detail checks failed. No success assumed.";}}
  finally{pending=null;if(!closed)run.IsEnabled=true;}
 }
}
