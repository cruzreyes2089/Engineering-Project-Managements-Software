using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using PracticeManager.Core;
using PracticeManager.Persistence;
namespace PracticeManager.Desktop;
internal sealed class ProjectEffortWindow:Window
{
 readonly LocalPractice service;readonly long project;
 ProjectEffortReview? snapshot;
 readonly DataGrid grid=new(){AutoGenerateColumns=false,IsReadOnly=true,CanUserAddRows=false};
 readonly TextBox remaining=new(){AcceptsReturn=true,MaxLength=40000,Height=90,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
 readonly TextBlock summary=new(){TextWrapping=TextWrapping.Wrap},result=new(){TextWrapping=TextWrapping.Wrap};
 public ProjectEffortWindow(LocalPractice service,long project)
 {
  this.service=service;this.project=project;Title="Project effort — current plan and all recorded time";Width=1070;Height=740;WindowStartupLocation=WindowStartupLocation.CenterOwner;
  var root=new DockPanel{Margin=new Thickness(16)};var head=new StackPanel();
  head.Children.Add(new TextBlock{Text=Title,FontSize=22,FontWeight=FontWeights.SemiBold});
  head.Children.Add(new TextBlock{Text="All work dates; current task estimates, not original approved contract baseline. Approved time is recorded fact; pending, rejected and invalidated time remain separate. Cancelled/discontinued tasks are historical effort, not current plan.",TextWrapping=TextWrapping.Wrap});
  var refresh=new Button{Content="Refresh snapshot (clears assumptions)"};refresh.Click+=(_,_)=>Refresh();head.Children.Add(refresh);head.Children.Add(summary);DockPanel.SetDock(head,Dock.Top);root.Children.Add(head);
  var bottom=new StackPanel{Margin=new Thickness(0,8,0,0)};
  bottom.Children.Add(new TextBlock{Text="Optional forecast: enter task ID, remaining unsubmitted minutes, one line per open task. Enter zero only if explicitly assessed. Completed/cancelled/discontinued tasks have no remaining allowance in this scenario. Assumes pending time is accepted; no billing or work authorization inferred.",TextWrapping=TextWrapping.Wrap});
  bottom.Children.Add(remaining);var calc=new Button{Content="Calculate provisional forecast from snapshot"};calc.Click+=(_,_)=>Calculate();bottom.Children.Add(calc);bottom.Children.Add(result);
  DockPanel.SetDock(bottom,Dock.Bottom);root.Children.Add(bottom);
  foreach(var (label,prop) in new[]{("Task ID","TaskId"),("Phase","Phase"),("Task","Title"),("State","State"),("Estimate h","EstimateHours"),("Approved h","ApprovedHours"),("Pending h","PendingHours"),("Rejected h","RejectedHours"),("Invalidated h","InvalidatedHours")})
  {
   var binding=new Binding(prop);if(prop.EndsWith("Hours",StringComparison.Ordinal))binding.StringFormat="0.00";
   grid.Columns.Add(new DataGridTextColumn{Header=label,Binding=binding,Width=DataGridLength.Auto});
  }
  root.Children.Add(grid);Content=root;
  remaining.TextChanged+=(_,_)=>result.Text="";Loaded+=(_,_)=>Refresh();Closed+=(_,_)=>Clear();
 }
 void Clear(){snapshot=null;grid.ItemsSource=null;remaining.Clear();summary.Text=result.Text="";}
 static string Hours(long? minutes)=>minutes.HasValue?$"{minutes.Value/60m:0.00} h":"Incomplete";
 void Refresh()
 {
  Clear();
  try
  {
   var r=service.ReadProjectEffort(project);snapshot=r;grid.ItemsSource=r.Tasks;
   summary.Text=$"{r.ProjectNumber} | Current estimate {Hours(r.CurrentPlanMinutes)} | Approved {Hours(r.CurrentApprovedMinutes)} | Pending {Hours(r.CurrentPendingMinutes)} | Historical approved {Hours(r.HistoricalApprovedMinutes)} | Historical pending {Hours(r.HistoricalPendingMinutes)} | Rejected {Hours(r.RejectedMinutes)} | Invalidated {Hours(r.InvalidatedMinutes)} | {r.OpenTasks} open tasks";
  }
  catch(UnauthorizedAccessException){summary.Text="Access denied.";}
  catch(Exception){summary.Text="Project effort unavailable.";}
 }
 void Calculate()
 {
  result.Text="";
  try
  {
   if(snapshot is not ProjectEffortReview r)throw new ArgumentException("Refresh snapshot first.");
   var fresh=service.ReadProjectEffort(project);
   if(fresh.ProjectId!=r.ProjectId||fresh.ProjectNumber!=r.ProjectNumber||fresh.SourceEvidenceHash!=r.SourceEvidenceHash||!fresh.Tasks.SequenceEqual(r.Tasks)){Clear();result.Text="Project effort changed. Refresh and review assumptions again.";return;}
   if(r.Tasks.Count==0)throw new ArgumentException("No task estimates available.");
   var f=ProjectEffort.Forecast(r,ProjectEffort.ParseRemaining(remaining.Text));
   result.Text=$"Entered remaining {Hours(f.EnteredRemainingMinutes)} | Missing open estimates: {f.MissingOpenTasks} | Provisional current scope total {Hours(f.CurrentForecastMinutes)} | Variance from current estimate {Hours(f.CurrentForecastVarianceMinutes)} | Provisional total including historical effort {Hours(f.TotalProjectForecastMinutes)}.";
  }
  catch(UnauthorizedAccessException){Clear();result.Text="Access denied.";}
  catch(ArgumentException e){result.Text=e.Message;}
  catch(Exception){Clear();result.Text="Forecast unavailable.";}
 }
}
