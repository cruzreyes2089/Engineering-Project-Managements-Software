using System.Windows;
using System.Windows.Controls;
using PracticeManager.Persistence;
namespace PracticeManager.Desktop;
internal sealed class ProjectLifecycleWindow:Window
{
 readonly LocalPractice service;readonly long project;
 readonly TextBlock summary=new(){TextWrapping=TextWrapping.Wrap},status=new(){TextWrapping=TextWrapping.Wrap};
 readonly TextBox note=new(){MaxLength=1000,AcceptsReturn=true,Height=85,TextWrapping=TextWrapping.Wrap};
 readonly Button close=new(){Content="Record operational closure"},reopen=new(){Content="Explicitly reopen project"};
 readonly DataGrid history=WorkWindowFields.Grid();
 long? expected;bool loaded;
 public ProjectLifecycleWindow(LocalPractice service,long project)
 {
  this.service=service;this.project=project;Title="Operational project lifecycle — development";Width=820;Height=650;WindowStartupLocation=WindowStartupLocation.CenterOwner;
  var panel=new StackPanel{Margin=new Thickness(18)};
  panel.Children.Add(new TextBlock{Text=Title,FontSize=22,FontWeight=FontWeights.SemiBold});
  panel.Children.Add(new TextBlock{Text="Operational closure stops new planning, work submissions and additional-scope authorization. It is not contractual release, final delivery or financial settlement. Synthetic development data only.",TextWrapping=TextWrapping.Wrap});
  panel.Children.Add(summary);panel.Children.Add(new TextBlock{Text="Decision evidence / reason"});panel.Children.Add(note);panel.Children.Add(close);panel.Children.Add(reopen);
  var refresh=new Button{Content="Refresh"};refresh.Click+=(_,_)=>Refresh();panel.Children.Add(refresh);panel.Children.Add(status);
  history.Height=220;WorkWindowFields.Columns(history,("Event","Id"),("State","State"),("Reason","Note"),("Actor","ActorId"),("UTC","RecordedUtc"));panel.Children.Add(history);
  Content=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
  close.Click+=(_,_)=>Record(true);reopen.Click+=(_,_)=>Record(false);Refresh();
 }
 void Refresh()
 {
  loaded=false;expected=null;close.IsEnabled=reopen.IsEnabled=false;history.ItemsSource=null;summary.Text="";note.Clear();
  try{
   var review=service.ReadProjectLifecycleReview(project);var r=review.Snapshot;var events=review.History;history.ItemsSource=events;expected=events.FirstOrDefault()?.Id;
   summary.Text=$"{r.ProjectNumber} — {r.ProjectState}\nSetup: {r.SetupState}; conflict: {r.ConflictStatus}\nOutstanding tasks: {r.OpenTasks}; QA: {r.OpenQualityItems}; scope: {r.PendingScopeDecisions}\nPending time: {r.PendingTimeSubmissions}; expenses: {r.PendingExpenseSubmissions}";
   close.IsEnabled=r.CanRequestClose;reopen.IsEnabled=r.CanRequestReopen;loaded=true;
   status.Text=r.ProjectState=="Active"&&!r.CanRequestClose?"Resolve outstanding setup, conflict or operational items shown above before closing.":
    r.ProjectState=="OperationalClosed"&&!r.CanRequestReopen?"Resolve current conflict clearance before requesting reopening.":
    "Current operational snapshot. Service rechecks authorization and evidence before recording.";
  }catch(UnauthorizedAccessException){status.Text="Access denied.";}catch(ArgumentException e){status.Text=e.Message;}catch(Exception){status.Text="Lifecycle unavailable.";}
 }
 void Record(bool closing)
 {
  if(!loaded||!(closing?close.IsEnabled:reopen.IsEnabled))return;
  if(string.IsNullOrWhiteSpace(note.Text)){status.Text="Enter the decision evidence or reason before confirming.";note.Focus();return;}
  var question=closing?"Record operational closure? This does not settle invoices or release contractual obligations.":"Explicitly reopen this project for operational work subject to current controls?";
  if(MessageBox.Show(this,question,"Confirm lifecycle decision",MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes)return;
  try{var result=service.RecordProjectLifecycle(project,expected,closing,note.Text,true);Refresh();status.Text="Recorded: "+result.State;}
  catch(UnauthorizedAccessException){Refresh();status.Text="Access denied.";}catch(ArgumentException e){status.Text=e.Message;loaded=false;close.IsEnabled=reopen.IsEnabled=false;}catch(Exception){loaded=false;close.IsEnabled=reopen.IsEnabled=false;status.Text="Decision unavailable. Refresh before retrying.";}
 }
}
