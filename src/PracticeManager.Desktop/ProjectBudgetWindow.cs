using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using PracticeManager.Core;
using PracticeManager.Persistence;

namespace PracticeManager.Desktop;

internal sealed class ProjectBudgetWindow:Window
{
 readonly LocalPractice service;readonly long project;
 EffortBudgetWorkspace? review;
 bool closed,recording;
 readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap},summary=new(){TextWrapping=TextWrapping.Wrap};
 readonly DataGrid tasks=Grid(),budgets=Grid(),forecasts=Grid();
 readonly TextBox minutes=new(){Width=120,MaxLength=9},reason=new(){MaxLength=1000},
  remaining=new(){AcceptsReturn=true,Height=75,MaxLength=40000,VerticalScrollBarVisibility=ScrollBarVisibility.Auto},
  forecastReason=new(){MaxLength=1000};
 readonly ComboBox kind=new(){Width=160};
 readonly Button recordBudget=new(){Content="Review and record budget",IsEnabled=false},
  recordForecast=new(){Content="Review and record forecast",IsEnabled=false};
 readonly Button refresh=new(){Content="Refresh (clears unrecorded inputs)"};
 static DataGrid Grid()=>new(){AutoGenerateColumns=false,IsReadOnly=true,CanUserAddRows=false,CanUserDeleteRows=false};
 static void Columns(DataGrid grid,params (string Header,string Binding)[] columns)
 {
  foreach(var (header,path) in columns)grid.Columns.Add(new DataGridTextColumn{Header=header,Binding=new Binding(path),Width=DataGridLength.Auto});
 }
 static TextBlock Label(string text)=>new(){Text=text,TextWrapping=TextWrapping.Wrap};
 static string Hours(long value)=>$"{value/60m:0.00} h";
 public ProjectBudgetWindow(LocalPractice service,long project)
 {
  this.service=service;this.project=project;Title="Project labor budget — candidate";Width=1100;Height=760;
  WindowStartupLocation=WindowStartupLocation.CenterOwner;
  var root=new DockPanel{Margin=new Thickness(16)};var top=new StackPanel();
  top.Children.Add(Label("Internal labor budget only. First recorded is not necessarily original contractual budget. Forecast includes historical effort and assumes pending time is accepted. No fee, scope or work authorization changes."));
  refresh.Click+=(_,_)=>Refresh();top.Children.Add(refresh);top.Children.Add(summary);top.Children.Add(status);
  DockPanel.SetDock(top,Dock.Top);root.Children.Add(top);
  var tabs=new TabControl();
  Columns(tasks,("Task ID","TaskId"),("Phase","Phase"),("Task","Title"),("State","State"),("Estimate min","EstimateMinutes"),("Approved min","ApprovedMinutes"),("Pending min","PendingMinutes"));
  Columns(budgets,("Revision","Model.Revision"),("Budget min","Model.Minutes"),("Category","Model.Kind"),("Reason","Model.Reason"),("Actor ID","Actor"),("Recorded UTC","Utc"));
  Columns(forecasts,("Forecast","Sequence"),("First budget min","Model.FirstBudgetMinutes"),("Applicable budget min","Model.CurrentBudgetMinutes"),("Forecast min","Model.TotalForecastMinutes"),("Variance current min","Model.VarianceFromCurrentMinutes"),("Variance first min","Model.VarianceFromFirstMinutes"),("Reason","Reason"),("Actor ID","Actor"),("Recorded UTC","Utc"));
  var budgetPanel=new DockPanel();var budgetInputs=new StackPanel{Margin=new Thickness(0,8,0,0)};
  var row=new WrapPanel();row.Children.Add(Label("Budget minutes:"));row.Children.Add(minutes);row.Children.Add(Label("Review category:"));row.Children.Add(kind);
  budgetInputs.Children.Add(row);budgetInputs.Children.Add(Label("Reason:"));budgetInputs.Children.Add(reason);budgetInputs.Children.Add(recordBudget);
  DockPanel.SetDock(budgetInputs,Dock.Bottom);budgetPanel.Children.Add(budgetInputs);budgetPanel.Children.Add(budgets);
  var forecastPanel=new DockPanel();var forecastInputs=new StackPanel{Margin=new Thickness(0,8,0,0)};
  forecastInputs.Children.Add(Label("One line per open task: task ID, remaining unsubmitted minutes. Explicit zero permitted; no default."));
  forecastInputs.Children.Add(remaining);forecastInputs.Children.Add(Label("Forecast review reason:"));forecastInputs.Children.Add(forecastReason);forecastInputs.Children.Add(recordForecast);
  DockPanel.SetDock(forecastInputs,Dock.Bottom);forecastPanel.Children.Add(forecastInputs);forecastPanel.Children.Add(forecasts);
  tabs.Items.Add(new TabItem{Header="Current tasks (reference)",Content=tasks});
  tabs.Items.Add(new TabItem{Header="Budget revisions",Content=budgetPanel});
  tabs.Items.Add(new TabItem{Header="Forecast history",Content=forecastPanel});
  root.Children.Add(tabs);Content=root;
  recordBudget.Click+=(_,_)=>RecordBudget();recordForecast.Click+=(_,_)=>RecordForecast();
  Loaded+=(_,_)=>Refresh();Closed+=(_,_)=>{closed=true;Clear();};
 }
 void Clear()
 {
  review=null;tasks.ItemsSource=budgets.ItemsSource=forecasts.ItemsSource=null;
  minutes.Clear();reason.Clear();remaining.Clear();forecastReason.Clear();kind.ItemsSource=null;
  summary.Text=status.Text="";SetControls();
 }
 void SetControls()
 {
  bool allowed=!closed&&!recording&&review?.CanRecord==true&&review.ReviewedSnapshotHash!=null;
  minutes.IsEnabled=reason.IsEnabled=kind.IsEnabled=recordBudget.IsEnabled=allowed;
  remaining.IsEnabled=forecastReason.IsEnabled=recordForecast.IsEnabled=allowed&&review!.History.Budgets.Count>0;
  refresh.IsEnabled=!closed&&!recording;
 }
 void Refresh()
 {
  if(closed||recording)return;
  Clear();
  try
  {
   var r=service.ReadProjectBudgetWorkspace(project);review=r;
   tasks.ItemsSource=r.Effort.Tasks;budgets.ItemsSource=r.History.Budgets;forecasts.ItemsSource=r.History.Forecasts;
   var first=r.History.Budgets.FirstOrDefault();var latest=r.History.Budgets.LastOrDefault();
   summary.Text=$"{r.Effort.ProjectNumber} | First recorded: {(first==null?"None":Hours(first.Model.Minutes))} | Current recorded: {(latest==null?"None":Hours(latest.Model.Minutes))} | Current task estimate: {Hours(r.Effort.CurrentPlanMinutes)} | Total approved effort: {Hours(r.Effort.CurrentApprovedMinutes+r.Effort.HistoricalApprovedMinutes)} | Pending effort: {Hours(r.Effort.CurrentPendingMinutes+r.Effort.HistoricalPendingMinutes)}.";
   kind.ItemsSource=first==null?new[]{"FirstRecorded"}:new[]{"ScopeReview","EstimateReview","ManagementReview"};
   kind.SelectedIndex=0;status.Text=r.CanRecord?"Review current task evidence before recording.":"Read only — recording unavailable.";
  }
  catch(UnauthorizedAccessException){Clear();status.Text="Access denied or budget history not enabled.";}
  catch(Exception){Clear();status.Text="Budget review unavailable; no history is assumed.";}
  finally{SetControls();}
 }
 void RecordBudget()
 {
  if(recording||closed||review is not {CanRecord:true,ReviewedSnapshotHash:string hash} r)return;
  try
  {
   if(!long.TryParse(minutes.Text,NumberStyles.None,CultureInfo.InvariantCulture,out var value)||kind.SelectedItem is not string category)
    throw new ArgumentException("Enter whole budget minutes and review category.");
   var note=reason.Text;var prior=r.History.Budgets.LastOrDefault()?.Model.Hash;
   var prepared=ProjectBudgetReview.PrepareRevision(project,r.History.Budgets.Select(x=>x.Model).ToArray(),prior,r.Effort,hash,value,category,note,true);
   var operation=Guid.NewGuid();
   ConfirmAndRecord(r,$"Record internal budget revision {prepared.Revision}: {Hours(prepared.Minutes)}?\nCategory: {category}\nReason: {note}\nThis does not authorize scope or fees.",
    ()=>service.RecordProjectBudgetRevision(project,operation,prior,hash,value,category,note,true).Replayed);
  }
  catch(ArgumentException ex){status.Text=ex.Message;}
  catch(Exception){Clear();status.Text="Budget preview unavailable. Refresh before trying again.";}
 }
 void RecordForecast()
 {
  if(recording||closed||review is not {CanRecord:true,ReviewedSnapshotHash:string hash} r)return;
  try
  {
   var budget=r.History.Budgets.LastOrDefault()?.Model.Hash??throw new ArgumentException("Record a reviewed budget first.");
   var previous=r.History.Forecasts.LastOrDefault()?.Hash;
   var inputs=ProjectEffort.ParseRemaining(remaining.Text);var note=forecastReason.Text;
   if(string.IsNullOrWhiteSpace(note)||note.Length>1000||note!=note.Trim()||note.Any(char.IsControl))throw new ArgumentException("Enter an explicit concise review reason.");
   var prepared=ProjectBudgetReview.PrepareForecast(project,r.History.Budgets.Select(x=>x.Model).ToArray(),budget,r.Effort,hash,inputs);
   var operation=Guid.NewGuid();
   ConfirmAndRecord(r,$"Record provisional forecast: {Hours(prepared.TotalForecastMinutes)}?\nVariance from current budget: {Hours(prepared.VarianceFromCurrentMinutes)}\nVariance from first budget: {Hours(prepared.VarianceFromFirstMinutes)}\nAssumes pending time accepted.\nReason: {note}",
    ()=>service.RecordProjectEffortForecast(project,operation,previous,budget,hash,inputs,note,true).Replayed);
  }
  catch(ArgumentException ex){status.Text=ex.Message;}
  catch(Exception){Clear();status.Text="Forecast preview unavailable. Refresh before trying again.";}
 }
 void ConfirmAndRecord(EffortBudgetWorkspace expected,string message,Func<bool> action)
 {
  recording=true;SetControls();
  try
  {
   if(MessageBox.Show(this,message,"Confirm internal review",MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes)return;
   if(closed||!ReferenceEquals(review,expected))return;
   // Service rechecks authorization, exact reviewed evidence and history predecessor
   // within the immediate transaction. No automatic retry on ambiguous failure.
   var replayed=action();recording=false;Refresh();
   if(!closed&&review!=null)status.Text=replayed?"Existing identical record retrieved.":"Record committed and history refreshed.";
  }
  catch(UnauthorizedAccessException){Clear();status.Text="Access denied. Refresh or sign in again.";}
  catch(Exception){Clear();status.Text="Record result not confirmed. Refresh and reconcile history before any new attempt.";}
  finally{recording=false;SetControls();}
 }
}
