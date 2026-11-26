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
  try{
   var s=service.ReadPostingRehearsalStatus();
   report.Text=$"Posting: DISABLED\nLifecycle: {(s.Held?"HOLD — "+s.HoldReason:"No incident declared; identity not certified")}\nReserved: {s.Reserved}\nIn flight (treat as uncertain after interruption): {s.InFlight}\nUncertain: {s.Uncertain}\nSimulated confirmed: {s.SimulatedConfirmed}\nCancelled: {s.Cancelled}";
   reason.IsEnabled=!s.Held;declare.IsEnabled=!s.Held&&reason.SelectedItem is LifecycleHoldReason;
  }catch(UnauthorizedAccessException){report.Text="Access denied.";declare.IsEnabled=reason.IsEnabled=false;}
   catch(Exception){report.Text="Status unavailable. No authorization assumed.";declare.IsEnabled=reason.IsEnabled=false;}
  try{
   var w=service.ReadSyntheticWireDraftStatus();
   wireReport.Text=$"Drafts: {w.Draft}\nHistorically approved: {w.Approved}\nCancelled: {w.Cancelled}\nApproval counts are not posting authorization.";
  }catch(UnauthorizedAccessException){wireReport.Text="Draft history access denied.";}
   catch(Exception){wireReport.Text="Draft history unavailable.";}
  try{
   var a=service.ReadSyntheticWireAttemptStatus();
   attemptReport.Text=$"Reserved: {a.Reserved}\nWitnessed: {a.Witnessed}\nUncertain: {a.Uncertain}\nAwaiting review: {a.ConfirmationForReview}\nReviewed simulations: {a.ReviewedSimulation}\nNo real posting authority.";
  }catch(UnauthorizedAccessException){attemptReport.Text="Attempt history access denied.";}
   catch(Exception){attemptReport.Text="Attempt history unavailable.";}
 }
 void Declare()
 {
  if(reason.SelectedItem is not LifecycleHoldReason value)return;
  if(MessageBox.Show(this,"Confirm that this local accounting journal must be quarantined. No reset is available in this development version. This does not alter Manager records.","Declare accounting lifecycle hold",MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes)return;
  try{service.DeclareAccountingLifecycleHold(value);Refresh();}
  catch(UnauthorizedAccessException){report.Text="Access denied.";declare.IsEnabled=reason.IsEnabled=false;}
  catch(Exception){report.Text="Hold request failed; no success assumed.";declare.IsEnabled=reason.IsEnabled=false;}
 }
}
