using System.Windows;
using System.Windows.Controls;
using PracticeManager.Core;
using PracticeManager.Persistence;
namespace PracticeManager.Desktop;
internal sealed class AccountingPostingSafetyWindow:Window
{
 readonly LocalPractice service;
 readonly TextBlock report=new(){TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,12,0,8)};
 readonly TextBlock attemptReport=new(){TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,8)};
 readonly TextBlock wireReport=new(){TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,8)};
 readonly ComboBox reason=new(){Margin=new Thickness(0,8,0,8)};
 readonly Button declare=new(){Content="Declare lifecycle hold",Padding=new Thickness(12,8,12,8)};
 public AccountingPostingSafetyWindow(LocalPractice service)
 {
  this.service=service;Title="Accounting safeguards — offline rehearsal";Width=640;SizeToContent=SizeToContent.Height;MaxHeight=SystemParameters.WorkArea.Height-40;WindowStartupLocation=WindowStartupLocation.CenterOwner;ShowInTaskbar=false;
  var panel=new StackPanel{Margin=new Thickness(20)};
  panel.Children.Add(new TextBlock{Text="Posting safeguards and recovery",FontSize=23,FontWeight=FontWeights.SemiBold});
  panel.Children.Add(new TextBlock{Text="Real posting is disabled in this development version. The counts below are local simulated intents only, not Manager transactions. A matching business marker cannot identify an exact clone.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,8)});
  panel.Children.Add(report);
  panel.Children.Add(new TextBlock{Text="Full-wire draft history (offline only)",FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,8,0,0)});
  panel.Children.Add(wireReport);
  panel.Children.Add(new TextBlock{Text="Synthetic attempt history (offline only)",FontWeight=FontWeights.SemiBold});
  panel.Children.Add(attemptReport);
  var history=new Button{Content="Inspect transaction history (read only)",Margin=new Thickness(0,8,0,8),Padding=new Thickness(8)};
  history.Click+=(_,_)=>{try{new AccountingWireHistoryWindow(service){Owner=this}.ShowDialog();}catch(UnauthorizedAccessException){MessageBox.Show(this,"Access denied.");}catch(Exception){MessageBox.Show(this,"History unavailable.");}finally{Refresh();}};
  panel.Children.Add(history);
  panel.Children.Add(new TextBlock{Text="Declare a hold only if a clone, restore or suspected identity change has occurred. This is sticky; there is no reset in this version. Read-only inspection remains available.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,8)});
  reason.ItemsSource=Enum.GetValues<LifecycleHoldReason>();reason.SelectedIndex=-1;
  panel.Children.Add(reason);panel.Children.Add(declare);
  var close=new Button{Content="Close",Margin=new Thickness(0,12,0,0)};close.Click+=(_,_)=>Close();panel.Children.Add(close);
  Content=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
  declare.Click+=(_,_)=>Declare();reason.SelectionChanged+=(_,_)=>{if(reason.IsEnabled)declare.IsEnabled=reason.SelectedItem is LifecycleHoldReason;};
  Refresh();
 }
 void Refresh()
 {
  report.Text=wireReport.Text=attemptReport.Text="";
  declare.IsEnabled=reason.IsEnabled=false;
  try{
   var overview=service.ReadAccountingSafetyOverview();
   var s=overview.Rehearsals;var w=overview.Drafts;var a=overview.Attempts;
   report.Text=$"Posting: DISABLED\nStatus: {overview.Status}\nLifecycle: {(s.Held?"HOLD — "+s.HoldReason:"No incident declared; identity not certified")}\nUnresolved simulated attempts: {overview.Unresolved}\nSaved profile: {(overview.ProfilePresent?"Present":"Absent")}\nMarker: {(overview.MarkerPresent?"Present":"Absent")}\nLegacy rehearsal reserved: {s.Reserved}\nIn flight: {s.InFlight}\nUncertain: {s.Uncertain}\nSimulated confirmed: {s.SimulatedConfirmed}\nCancelled: {s.Cancelled}";
   wireReport.Text=$"Drafts: {w.Draft}\nHistorically approved: {w.Approved}\nCancelled: {w.Cancelled}\nApproval counts are not posting authorization.";
   attemptReport.Text=$"Reserved: {a.Reserved}\nWitnessed: {a.Witnessed}\nUncertain: {a.Uncertain}\nAwaiting review: {a.ConfirmationForReview}\nReviewed simulations: {a.ReviewedSimulation}\nNo real posting authority.";
   reason.IsEnabled=!s.Held;declare.IsEnabled=!s.Held&&reason.SelectedItem is LifecycleHoldReason;
  }catch(UnauthorizedAccessException){report.Text="Access denied.";}
   catch(Exception){report.Text="Status unavailable. No authorization assumed.";}
 }
 void Declare()
 {
  if(reason.SelectedItem is not LifecycleHoldReason value)return;
  if(MessageBox.Show(this,"Confirm that this local accounting journal must be quarantined. No reset is available in this development version. This does not alter Manager records.","Declare accounting lifecycle hold",MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes)return;
  try{service.DeclareAccountingLifecycleHold(value);Refresh();}
  catch(UnauthorizedAccessException){report.Text="Access denied.";wireReport.Text=attemptReport.Text="";declare.IsEnabled=reason.IsEnabled=false;}
  catch(Exception){report.Text="Hold request failed; no success assumed.";wireReport.Text=attemptReport.Text="";declare.IsEnabled=reason.IsEnabled=false;}
 }
}
