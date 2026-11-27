using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using PracticeManager.Core;
using PracticeManager.Persistence;
namespace PracticeManager.Desktop;
internal sealed class ProjectPlanningWindow:Window
{
 readonly LocalPractice service;readonly long project;
 readonly DataGrid phases=new(){IsReadOnly=true,AutoGenerateColumns=false,CanUserAddRows=false,SelectionMode=DataGridSelectionMode.Single};
 readonly DataGrid tasks=new(){IsReadOnly=true,AutoGenerateColumns=false,CanUserAddRows=false,SelectionMode=DataGridSelectionMode.Single};
 readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap};
 bool loaded;
 readonly List<(Button Button,Func<bool> Allowed)> actions=new();
 public ProjectPlanningWindow(LocalPractice service,long project)
 {
  this.service=service;this.project=project;Title="Project phases and tasks";Width=1000;Height=670;WindowStartupLocation=WindowStartupLocation.CenterOwner;
  var root=new DockPanel{Margin=new Thickness(16)};var head=new StackPanel();head.Children.Add(new TextBlock{Text="Project planning",FontSize=24,FontWeight=FontWeights.SemiBold});head.Children.Add(new TextBlock{Text="Planning is allowed while Pending; employees may start work only after explicit project authorization. Estimate is original total task hours, not allocated daily capacity. Cancelled and discontinued assignments remain visible for history and are excluded from operational estimate totals.",TextWrapping=TextWrapping.Wrap});head.Children.Add(status);
  DockPanel.SetDock(head,Dock.Top);root.Children.Add(head);
  bool CanPlan()=>loaded&&service.Can("Projects.Plan")&&service.Can("Employees.View")&&service.Can("Projects.View")&&service.Can("Clients.View");
  bool Open()=>CanPlan()&&tasks.ItemsSource!=null&&service.ReadProjectOverview(project).State is "Pending" or "Active";
  ProjectTask? Selected()=>tasks.SelectedItem as ProjectTask;
  DockPanel Section(string title,DataGrid g,Action<WrapPanel> build){
   var p=new DockPanel();var h=new StackPanel();h.Children.Add(new TextBlock{Text=title,FontSize=17,FontWeight=FontWeights.SemiBold});var bar=new WrapPanel();build(bar);h.Children.Add(bar);DockPanel.SetDock(h,Dock.Top);p.Children.Add(h);p.Children.Add(g);return p;
  }
  void Add(WrapPanel bar,string label,Action action,Func<bool> allowed){var button=new Button{Content=label,Margin=new Thickness(2),IsEnabled=false};button.Click+=(_,_)=>Run(()=>{if(!allowed())throw new ArgumentException("Refresh and select an eligible record.");action();});actions.Add((button,allowed));bar.Children.Add(button);}
  var phaseSection=Section("Project phases",phases,bar=>{
   Add(bar,"New phase",()=>EditPhase(null),Open);
   Add(bar,"Edit phase",()=>EditPhase(phases.SelectedItem as ProjectPhase??throw new ArgumentException("Select phase.")),()=>Open()&&phases.SelectedItem is ProjectPhase);
   WorkWindowFields.Button(bar,"Refresh",()=>Run(Refresh));
  });
  var taskSection=Section("Tasks and assignments",tasks,bar=>{
   Add(bar,"New task",()=>EditTask(null),()=>Open()&&phases.Items.Count>0);
   Add(bar,"Edit task",()=>EditTask(Selected()),()=>Open()&&Selected()?.State=="Planned");
   Add(bar,"Cancel task",CancelTask,()=>Open()&&Selected()?.State=="Planned");
   Add(bar,"Stop task",DiscontinueTask,()=>Open()&&Selected()?.State=="InProgress");
   Add(bar,"View cancellation",ViewCancellation,()=>CanPlan()&&Selected()?.State=="Cancelled");
   Add(bar,"View stop record",ViewDiscontinuation,()=>CanPlan()&&Selected()?.State=="Discontinued");
  });
  void Col(DataGrid g,string title,string prop)=>g.Columns.Add(new DataGridTextColumn{Header=title,Binding=new Binding(prop),Width=DataGridLength.Auto});
  foreach(var (label,prop) in new[]{("Phase","Name"),("Start","Start"),("End","End")})Col(phases,label,prop);
  foreach(var (label,prop) in new[]{("Task","Title"),("Phase","Phase"),("Employee","Employee"),("Start","Start"),("End","End"),("Estimated hours","EstimatedHours"),("Scope status","ScopeState"),("Conflict screening","ConflictStatus"),("State","State"),("Stopped on","StoppedOn")})Col(tasks,label,prop);
  var split=new Grid();split.RowDefinitions.Add(new RowDefinition());split.RowDefinitions.Add(new RowDefinition{Height=new GridLength(2,GridUnitType.Star)});Grid.SetRow(phaseSection,0);Grid.SetRow(taskSection,1);split.Children.Add(phaseSection);split.Children.Add(taskSection);root.Children.Add(split);Content=root;phases.SelectionChanged+=(_,_)=>UpdateActions();tasks.SelectionChanged+=(_,_)=>UpdateActions();Run(Refresh);
 }
 void UpdateActions(){foreach(var a in actions){try{a.Button.IsEnabled=a.Allowed();}catch(Exception){a.Button.IsEnabled=false;}}}
 void Clear(){loaded=false;phases.ItemsSource=tasks.ItemsSource=null;UpdateActions();}
 void Run(Action a){try{a();}catch(UnauthorizedAccessException){Clear();status.Text="Access denied.";}catch(ArgumentException e){status.Text=e.Message;}catch(Exception){Clear();status.Text="Operation failed. Refresh before retrying.";}finally{UpdateActions();}}
 void Refresh(){var phase=(phases.SelectedItem as ProjectPhase)?.Id;var task=(tasks.SelectedItem as ProjectTask)?.Id;Clear();var overview=service.ReadProjectOverview(project);var ps=service.ReadProjectPhases(project);var ts=service.ReadProjectTasks(project);phases.ItemsSource=ps;tasks.ItemsSource=ts;phases.SelectedItem=ps.FirstOrDefault(x=>x.Id==phase);tasks.SelectedItem=ts.FirstOrDefault(x=>x.Id==task);loaded=true;status.Text=overview.Number+" — "+overview.State+" · "+ps.Count+" phases · "+ts.Count+" tasks";UpdateActions();}
 TextBox Field(StackPanel p,string label,string value){p.Children.Add(new TextBlock{Text=label});var b=new TextBox{Text=value,MaxLength=200};p.Children.Add(b);return b;}
 DatePicker DateField(StackPanel p,string label,DateOnly day){p.Children.Add(new TextBlock{Text=label});var d=new DatePicker{SelectedDate=day.ToDateTime(TimeOnly.MinValue)};p.Children.Add(d);return d;}
 static DateOnly Day(DatePicker d)=>d.SelectedDate.HasValue?DateOnly.FromDateTime(d.SelectedDate.Value):throw new ArgumentException("Select a date.");
 void Editor(string title,StackPanel panel,Action save)
 {
  var w=new Window{Owner=this,Title=title,Width=500,Height=500,WindowStartupLocation=WindowStartupLocation.CenterOwner};var b=new Button{Content="Save",IsDefault=true};var cancel=new Button{Content="Cancel",IsCancel=true};var bar=new WrapPanel();bar.Children.Add(b);bar.Children.Add(cancel);panel.Children.Add(bar);w.Content=new ScrollViewer{Content=panel};b.Click+=(_,_)=>{try{save();w.DialogResult=true;}catch(ArgumentException e){MessageBox.Show(w,e.Message);}catch(Exception){MessageBox.Show(w,"Save failed. Refresh before retrying.");}};if(w.ShowDialog()==true)Refresh();
 }
 void CancelTask()
 {
  var x=tasks.SelectedItem as ProjectTask??throw new ArgumentException("Select task.");
  if(x.State!="Planned")throw new ArgumentException("Select an unstarted planned task.");
  var p=new StackPanel{Margin=new Thickness(18)};p.Children.Add(new TextBlock{Text=$"Cancel {x.Title}? Original assignment and estimate will remain in history. This cannot be undone.",TextWrapping=TextWrapping.Wrap});
  var reason=Field(p,"Cancellation reason","");reason.MaxLength=1000;
  var confirm=new CheckBox{Content="I explicitly authorize cancellation of this task."};p.Children.Add(confirm);
  Editor("Cancel planned task",p,()=>service.CancelPlannedTask(project,x.Id,x.Revision,reason.Text,confirm.IsChecked==true));
 }
 void ViewCancellation()
 {
  var x=tasks.SelectedItem as ProjectTask??throw new ArgumentException("Select task.");
  var evidence=service.ReadTaskCancellation(project,x.Id)??throw new ArgumentException("No cancellation recorded.");
  MessageBox.Show(this,$"Task: {x.Title}\nPlan revision: {evidence.PlanRevision}\nRecorded by ID: {evidence.ActorId}\nUTC: {evidence.RecordedUtc}\nReason: {evidence.Reason}","Cancellation evidence");
 }
 void DiscontinueTask()
 {
  var x=tasks.SelectedItem as ProjectTask??throw new ArgumentException("Select task.");
  if(x.State!="InProgress")throw new ArgumentException("Select a started task.");
  var p=new StackPanel{Margin=new Thickness(18)};p.Children.Add(new TextBlock{Text=$"Discontinue {x.Title}? Stop future work now. Actual work and financial history remain. This cannot be undone; create a separate assignment for remaining work.",TextWrapping=TextWrapping.Wrap});
  var reason=Field(p,"Discontinuation reason","");reason.MaxLength=1000;
  var confirm=new CheckBox{Content="I explicitly authorize stopping this assignment."};p.Children.Add(confirm);
  Editor("Discontinue started task",p,()=>service.DiscontinueStartedTask(project,x.Id,x.Revision,reason.Text,confirm.IsChecked==true));
 }
 void ViewDiscontinuation()
 {
  var x=tasks.SelectedItem as ProjectTask??throw new ArgumentException("Select task.");
  var evidence=service.ReadTaskDiscontinuation(project,x.Id)??throw new ArgumentException("No discontinuation recorded.");
  MessageBox.Show(this,$"Task: {x.Title}\nRevision: {evidence.TaskRevision}\nStop date: {evidence.StoppedOn:yyyy-MM-dd}\nRecorded by ID: {evidence.ActorId}\nUTC: {evidence.RecordedUtc}\nReason: {evidence.Reason}","Discontinuation evidence");
 }
 void EditPhase(ProjectPhase? x)
 {
  var p=new StackPanel{Margin=new Thickness(18)};var name=Field(p,"Phase",x?.Name??"");var today=DateOnly.FromDateTime(DateTime.Today);var s=DateField(p,"Start",x?.Start??today);var e=DateField(p,"End",x?.End??today);Editor("Phase",p,()=>{if(x==null)service.CreateProjectPhase(project,name.Text,Day(s),Day(e));else service.EditProjectPhase(project,x.Id,x.Revision,name.Text,Day(s),Day(e));});
 }
 void EditTask(ProjectTask? x)
 {
  if(x!=null&&x.State!="Planned")throw new ArgumentException("Select an unstarted planned task.");
  var p=new StackPanel{Margin=new Thickness(18)};var phase=new ComboBox{ItemsSource=service.ReadProjectPhases(project),DisplayMemberPath="Name",SelectedValuePath="Id",IsEnabled=x==null};phase.SelectedValue=x?.PhaseId??(phases.SelectedItem as ProjectPhase)?.Id;if(x==null&&phase.SelectedItem==null&&phase.Items.Count==1)phase.SelectedIndex=0;p.Children.Add(new TextBlock{Text="Phase"});p.Children.Add(phase);
  var staff=new ComboBox{ItemsSource=service.ReadEmployees().Where(e=>e.Active).ToArray(),DisplayMemberPath="Name",SelectedValuePath="Id"};if(x!=null)staff.SelectedValue=x.EmployeeId;else if(staff.Items.Count==1)staff.SelectedIndex=0;p.Children.Add(new TextBlock{Text="Assigned employee"});p.Children.Add(staff);
  var options=service.ReadTaskScopeOptions(project);var scope=new ComboBox{ItemsSource=options,DisplayMemberPath="Label",IsEnabled=x==null};scope.SelectedItem=options.FirstOrDefault(o=>o.Id==x?.ScopeChangeId);p.Children.Add(new TextBlock{Text="Authorized base scope or additional scope request"});p.Children.Add(scope);
  var name=Field(p,"Task",x?.Title??"");var today=DateOnly.FromDateTime(DateTime.Today);var chosen=phase.SelectedItem as ProjectPhase;var s=DateField(p,"Start",x?.Start??chosen?.Start??today);var e=DateField(p,"End",x?.End??chosen?.End??today);if(x==null)phase.SelectionChanged+=(_,_)=>{if(phase.SelectedItem is ProjectPhase selected){s.SelectedDate=selected.Start.ToDateTime(TimeOnly.MinValue);e.SelectedDate=selected.End.ToDateTime(TimeOnly.MinValue);}};var estimate=Field(p,"Estimated total time (hours, e.g. 2.5 or 2:30)",TaskEstimate.Format(x?.EstimateMinutes??60));
  Editor("Task plan",p,()=>{if(phase.SelectedItem is not ProjectPhase h||staff.SelectedItem is not EmployeeRecord emp )throw new ArgumentException("Select a phase and active employee.");var minutes=TaskEstimate.Parse(estimate.Text);if(scope.SelectedItem is not TaskScopeOption option)throw new ArgumentException("Select scope.");var input=new ProjectTaskPlan(h.Id,emp.Id,name.Text,Day(s),Day(e),minutes,option.Id);if(x==null)service.CreateProjectTask(project,input);else service.EditProjectTask(project,x.Id,x.Revision,input);});
 }
}
internal sealed class WorkScheduleWindow:Window
{
 readonly LocalPractice service;readonly bool own;
 readonly DataGrid grid=new(){IsReadOnly=true,AutoGenerateColumns=false,CanUserAddRows=false,SelectionMode=DataGridSelectionMode.Single};
 readonly DatePicker start=new(){SelectedDate=DateTime.Today.AddDays(-30)},end=new(){SelectedDate=DateTime.Today.AddDays(60)};
 readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap};
 public WorkScheduleWindow(LocalPractice service,bool own)
 {
  this.service=service;this.own=own;Title=own?"My work":"Staff schedule";Width=1050;Height=590;WindowStartupLocation=WindowStartupLocation.CenterOwner;
  var root=new DockPanel{Margin=new Thickness(16)};var head=new StackPanel();head.Children.Add(new TextBlock{Text=Title,FontSize=24,FontWeight=FontWeights.SemiBold});head.Children.Add(new TextBlock{Text="Inclusive overlapping date range; original task estimate is not a daily workload allocation. Cancelled and discontinued rows are history, not outstanding workload. A conflict hold blocks starting new work. Contact the responsible reviewer; record actual work accurately.",TextWrapping=TextWrapping.Wrap});var bar=new WrapPanel();bar.Children.Add(new TextBlock{Text="From"});bar.Children.Add(start);bar.Children.Add(new TextBlock{Text="Through"});bar.Children.Add(end);void Add(string label,Action action){var b=new Button{Content=label,Margin=new Thickness(2)};b.Click+=(_,_)=>Run(action);bar.Children.Add(b);}Add("Refresh",Refresh);if(own){Add("Start selected",()=>Advance("InProgress"));Add("Complete selected",()=>Advance("Completed"));}head.Children.Add(bar);head.Children.Add(status);DockPanel.SetDock(head,Dock.Top);root.Children.Add(head);
  foreach(var (label,prop) in new[]{("Project","ProjectNumber"),("Project state","ProjectState"),("Phase","Phase"),("Task","Title"),("Employee","Employee"),("Start","Start"),("End","End"),("Estimate hours","EstimatedHours"),("Scope status","ScopeState"),("Conflict screening","ConflictStatus"),("State","State"),("Stopped on","StoppedOn")})grid.Columns.Add(new DataGridTextColumn{Header=label,Binding=new Binding(prop),Width=DataGridLength.Auto});
  root.Children.Add(grid);Content=root;Run(Refresh);
 }
 void Run(Action action){try{action();}catch(UnauthorizedAccessException){grid.ItemsSource=null;status.Text="Access denied or no active employee link.";}catch(ArgumentException e){status.Text=e.Message;}catch(Exception){status.Text="Operation failed. Refresh before retrying.";}}
 void Refresh(){grid.ItemsSource=null;if(start.SelectedDate==null||end.SelectedDate==null)throw new ArgumentException("Select both dates.");var a=DateOnly.FromDateTime(start.SelectedDate.Value);var b=DateOnly.FromDateTime(end.SelectedDate.Value);grid.ItemsSource=own?service.ReadMyWork(a,b):service.ReadStaffSchedule(a,b);status.Text="";}
 void Advance(string state){if(grid.SelectedItem is not ProjectTask x)throw new ArgumentException("Select a task.");service.AdvanceMyTask(x.Id,x.Revision,state);Refresh();}
}
