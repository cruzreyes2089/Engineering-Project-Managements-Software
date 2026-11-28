using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using PracticeManager.Core;
using PracticeManager.Persistence;
namespace PracticeManager.Desktop;
internal static class WorkWindowFields
{
 public static DataGrid Grid()=>new(){AutoGenerateColumns=false,IsReadOnly=true,CanUserAddRows=false,SelectionMode=DataGridSelectionMode.Single};
 public static void Columns(DataGrid g,params (string Label,string Property)[] cols){foreach(var (label,prop) in cols)g.Columns.Add(new DataGridTextColumn{Header=label,Binding=new Binding(prop),Width=DataGridLength.Auto});}
 public static Func<int> Duration(StackPanel p,int? initial)
 {
  var values=WorkDuration.Fields(initial);var h=new TextBox{MaxLength=2,Width=70,Text=values.Hours};var m=new TextBox{MaxLength=2,Width=70,Text=values.Minutes};
  p.Children.Add(new TextBlock{Text="Actual time"});var row=new WrapPanel();row.Children.Add(h);row.Children.Add(new TextBlock{Text="hours",Margin=new Thickness(4,8,16,0)});row.Children.Add(m);row.Children.Add(new TextBlock{Text="minutes",Margin=new Thickness(4,8,0,0)});p.Children.Add(row);
  System.Windows.Automation.AutomationProperties.SetName(h,"Hours");System.Windows.Automation.AutomationProperties.SetName(m,"Minutes");return ()=>WorkDuration.Parse(h.Text,m.Text);
 }
 public static DateOnly Day(DatePicker d)=>d.SelectedDate is DateTime day?DateOnly.FromDateTime(day):throw new ArgumentException("Select a date.");
 public static void Button(WrapPanel p,string label,Action action){var b=new Button{Content=label,Margin=new Thickness(2)};b.Click+=(_,_)=>action();p.Children.Add(b);}
 public static string? Note(Window owner,string title)
 {
  var w=new Window{Owner=owner,Title=title,Width=500,Height=270,WindowStartupLocation=WindowStartupLocation.CenterOwner};var p=new StackPanel{Margin=new Thickness(16)};p.Children.Add(new TextBlock{Text="Record the reason / evidence for this decision."});var n=new TextBox{MaxLength=1000,AcceptsReturn=true,Height=110,TextWrapping=TextWrapping.Wrap};p.Children.Add(n);var ok=new Button{Content="Confirm"};p.Children.Add(ok);ok.Click+=(_,_)=>{if(!string.IsNullOrWhiteSpace(n.Text))w.DialogResult=true;};w.Content=p;return w.ShowDialog()==true?n.Text:null;
 }
}
internal sealed class WorkEntriesWindow:Window
{
 readonly LocalPractice service;readonly DataGrid entries=WorkWindowFields.Grid(),attendance=WorkWindowFields.Grid(),overhead=WorkWindowFields.Grid();
 readonly DatePicker start=new(){SelectedDate=DateTime.Today.AddDays(-30)},end=new(){SelectedDate=DateTime.Today};
 readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap};
 Button? clockIn,clockOut;
 public WorkEntriesWindow(LocalPractice service)
 {
  this.service=service;Title="My time, expenses and attendance";Width=1050;Height=650;WindowStartupLocation=WindowStartupLocation.CenterOwner;
  var root=new DockPanel{Margin=new Thickness(16)};var head=new StackPanel();head.Children.Add(new TextBlock{Text=Title,FontSize=24,FontWeight=FontWeights.SemiBold});head.Children.Add(new TextBlock{Text="Record actual project time, expenses or non-project time. Attendance is separate from project time. Submitted records are retained; corrections require a linked replacement.",TextWrapping=TextWrapping.Wrap});
  var bar=new WrapPanel();bar.Children.Add(new TextBlock{Text="From",Margin=new Thickness(4,8,4,0)});bar.Children.Add(start);bar.Children.Add(new TextBlock{Text="Through",Margin=new Thickness(4,8,4,0)});bar.Children.Add(end);WorkWindowFields.Button(bar,"Refresh",()=>Run(Refresh));head.Children.Add(bar);head.Children.Add(status);DockPanel.SetDock(head,Dock.Top);root.Children.Add(head);
  WorkWindowFields.Columns(entries,("Record ID","Id"),("Replaces","ReplacesId"),("Replacement","ReplacementId"),("Current closure","CurrentClosureId"),("Entry closure","ClosureId"),("Historical evidence","HistoricalReason"),("Project","ProjectNumber"),("Task","Task"),("Kind","Kind"),("Date","Date"),("Minutes","Minutes"),("Expense USD","Amount"),("Category","Category"),("Description","Description"),("Review","ReviewState"));
  WorkWindowFields.Columns(overhead,("Record ID","Id"),("Replaces","ReplacesId"),("Replacement","ReplacementId"),("Date","Date"),("Minutes","Minutes"),("Category","Category"),("Description","Description"),("Review","ReviewState"));
  WorkWindowFields.Columns(attendance,("Session ID","Id"),("Source","EntrySource"),("State","State"),("Recorded interval in UTC","ClockIn"),("Recorded interval out UTC","ClockOut"),("Correction ID","CorrectionId"));
  var tabs=new TabControl();
  DockPanel Page(DataGrid g,Action<WrapPanel> build){var p=new DockPanel();var buttons=new WrapPanel();build(buttons);DockPanel.SetDock(buttons,Dock.Top);p.Children.Add(buttons);p.Children.Add(g);return p;}
  Button ActionButton(WrapPanel p,string label,Action action){var button=new Button{Content=label,Margin=new Thickness(2)};button.Click+=(_,_)=>Run(action);p.Children.Add(button);return button;}
  Button? correction=null,overheadCorrection=null;
  tabs.Items.Add(new TabItem{Header="Project time & expenses",Content=Page(entries,p=>{
   ActionButton(p,"Submit time",()=>Submit(false));ActionButton(p,"Submit expense",()=>Submit(true));
   correction=ActionButton(p,"Correct selected",ReplaceRejected);correction.IsEnabled=false;
   var more=new MenuItem{Header="Closed project entries"};var menu=new Menu{VerticalAlignment=VerticalAlignment.Center};menu.Items.Add(more);
   var oldTime=new MenuItem{Header="Report earlier time"};oldTime.Click+=(_,_)=>Run(()=>Submit(false,null,true));more.Items.Add(oldTime);
   var oldExpense=new MenuItem{Header="Report earlier expense"};oldExpense.Click+=(_,_)=>Run(()=>Submit(true,null,true));more.Items.Add(oldExpense);p.Children.Add(menu);
  })});
  tabs.Items.Add(new TabItem{Header="Non-project time",Content=Page(overhead,p=>{ActionButton(p,"Submit time",()=>SubmitOverhead());overheadCorrection=ActionButton(p,"Correct selected",ReplaceOverhead);overheadCorrection.IsEnabled=false;})});
  tabs.Items.Add(new TabItem{Header="Attendance (UTC dates)",Content=Page(attendance,p=>{
   clockIn=ActionButton(p,"Clock in",()=>{var outcome=service.ClockIn();Refresh();status.Text=outcome;});
   clockOut=ActionButton(p,"Clock out",()=>{var a=service.ReadMyOpenAttendance();if(a==null)throw new ArgumentException("No open attendance session.");var outcome=service.ClockOut(a.Id);Refresh();status.Text=outcome;});
  })});
  entries.SelectionChanged+=(_,_)=>{if(correction!=null)correction.IsEnabled=entries.SelectedItem is OwnWorkSubmission x&&(x.ReviewState is "Rejected" or "Invalidated")&&!x.ReplacementId.HasValue;};
  overhead.SelectionChanged+=(_,_)=>{if(overheadCorrection!=null)overheadCorrection.IsEnabled=overhead.SelectedItem is OwnOverheadTime x&&(x.ReviewState is "Rejected" or "Invalidated")&&!x.ReplacementId.HasValue;};
  root.Children.Add(tabs);Content=root;Run(Refresh);
 }
 void ClearOwn(){entries.ItemsSource=attendance.ItemsSource=overhead.ItemsSource=null;if(clockIn!=null)clockIn.IsEnabled=false;if(clockOut!=null)clockOut.IsEnabled=false;}
 void Run(Action a){try{a();}catch(UnauthorizedAccessException){ClearOwn();status.Text="Access denied or no active employee link.";}catch(ArgumentException e){status.Text=e.Message;}catch(Exception){ClearOwn();status.Text="Operation failed. Refresh before retrying.";}}
 void Refresh(){ClearOwn();var a=WorkWindowFields.Day(start);var b=WorkWindowFields.Day(end);entries.ItemsSource=service.ReadMySubmissions(a,b);attendance.ItemsSource=service.ReadMyAttendance(a,b);overhead.ItemsSource=service.ReadMyOverheadTime(a,b);var open=service.ReadMyOpenAttendance()!=null;if(clockIn!=null)clockIn.IsEnabled=!open;if(clockOut!=null)clockOut.IsEnabled=open;status.Text=open?"Attendance session open.":"Not clocked in.";}
 void ReplaceOverhead()
 {
  if(overhead.SelectedItem is not OwnOverheadTime x||(x.ReviewState!="Rejected"&&x.ReviewState!="Invalidated")||x.ReplacementId.HasValue)throw new ArgumentException("Select an unreplaced rejected or invalidated overhead record.");
  SubmitOverhead(x);
 }
 void SubmitOverhead(OwnOverheadTime? original=null)
 {
  var w=new Window{Owner=this,Title=original==null?"Submit non-project overhead time":"Replace rejected / invalidated overhead",Width=560,Height=420,WindowStartupLocation=WindowStartupLocation.CenterOwner};var p=new StackPanel{Margin=new Thickness(16)};
  p.Children.Add(new TextBlock{Text="Overhead is nonbillable and is never allocated to a client invoice. Actual minutes count toward the same daily limit as project work.",TextWrapping=TextWrapping.Wrap});
  if(original!=null)p.Children.Add(new TextBlock{Text=$"Original #{original.Id}. Original decision remains in history; correction requires new review.",TextWrapping=TextWrapping.Wrap});
  var date=new DatePicker{SelectedDate=original?.Date.ToDateTime(TimeOnly.MinValue)??DateTime.Today};p.Children.Add(new TextBlock{Text="Date"});p.Children.Add(date);
  var category=new ComboBox{ItemsSource=LocalPractice.OverheadCategories,SelectedItem=original?.Category};p.Children.Add(new TextBlock{Text="Category"});p.Children.Add(category);
  var duration=WorkWindowFields.Duration(p,original?.Minutes);
  var note=new TextBox{MaxLength=1000,Height=80,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,Text=original?.Description??""};p.Children.Add(new TextBlock{Text="Description"});p.Children.Add(note);
  var confirm=new CheckBox{Content="I confirm these are corrected actual minutes."};if(original!=null)p.Children.Add(confirm);var save=new Button{Content=original==null?"Submit time":"Submit correction"};p.Children.Add(save);w.Content=new ScrollViewer{Content=p};var key=Guid.NewGuid();
  save.Click+=(_,_)=>{try{if(category.SelectedItem is not string cat)throw new ArgumentException("Select category.");var m=duration();if(original!=null&&confirm.IsChecked!=true)throw new ArgumentException("Confirm corrected actual facts.");var input=new OverheadTimeInput(key,WorkWindowFields.Day(date),m,cat,note.Text);if(original==null)service.SubmitOverheadTime(input);else if(original.ReviewState=="Invalidated")service.ReplaceInvalidatedOverheadTime(original.Id,input);else service.ReplaceRejectedOverheadTime(original.Id,input);w.DialogResult=true;}catch(ArgumentException ex){MessageBox.Show(w,ex.Message);}catch(UnauthorizedAccessException){MessageBox.Show(w,"Access denied.");}catch(Exception){MessageBox.Show(w,"Submission failed; check history before retrying.");}};if(w.ShowDialog()==true)Refresh();
 }
 void ReplaceRejected()
 {
  if(entries.SelectedItem is not OwnWorkSubmission x||(x.ReviewState!="Rejected"&&x.ReviewState!="Invalidated")||x.ReplacementId.HasValue)throw new ArgumentException("Select an unreplaced rejected or invalidated project record.");
  Submit(x.Kind=="Expense",x,x.CurrentClosureId.HasValue);
 }
 void Submit(bool expense,OwnWorkSubmission? original=null,bool historical=false)
 {
  var w=new Window{Owner=this,Title=historical?(original!=null?"Correct historical record — closed project":"Report historical actual work — closed project"):original!=null?"Replace rejected or invalidated record":expense?"Submit expense":"Submit time",Width=570,Height=historical?620:470,WindowStartupLocation=WindowStartupLocation.CenterOwner};var p=new StackPanel{Margin=new Thickness(16)};
  var task=new ComboBox{DisplayMemberPath="WorkEntryLabel"};p.Children.Add(new TextBlock{Text="Own started task — discontinued assignments: historical actual work only, through stop date.",TextWrapping=TextWrapping.Wrap});if(original==null)p.Children.Add(task);else{task.Visibility=Visibility.Collapsed;p.Children.Add(new TextBlock{Text=$"Original #{original.Id}: {original.ProjectNumber} / {original.Task}. Original decision remains in history; corrected facts require new review.",TextWrapping=TextWrapping.Wrap});}
  var date=new DatePicker{SelectedDate=original?.Date.ToDateTime(TimeOnly.MinValue)??DateTime.Today};p.Children.Add(new TextBlock{Text="Work / expense date"});if(historical&&original!=null)date.IsEnabled=false;p.Children.Add(date);
  if(original==null)
  {
   var taskStatus=new TextBlock{TextWrapping=TextWrapping.Wrap};p.Children.Add(taskStatus);
   void LoadTasks(){
    var selected=(task.SelectedItem as ProjectTask)?.Id;task.ItemsSource=null;
    try{var day=WorkWindowFields.Day(date);var available=service.ReadMyWork(day,day).Where(x=>(historical?x.ProjectState=="OperationalClosed"&&x.ClosureId.HasValue&&x.ClosedOn.HasValue&&day<=x.ClosedOn.Value:x.ProjectState=="Active")&&(x.State is "InProgress" or "Completed" or "Discontinued")&&(!x.StoppedOn.HasValue||day<=x.StoppedOn.Value)).ToArray();task.ItemsSource=available;task.SelectedItem=available.FirstOrDefault(x=>x.Id==selected);if(task.SelectedItem==null&&available.Length==1)task.SelectedIndex=0;taskStatus.Text=available.Length==0?"No eligible own task for this date.":"Choose the task for this date.";}
    catch(UnauthorizedAccessException){taskStatus.Text="Access denied.";}catch(ArgumentException ex){taskStatus.Text=ex.Message;}catch(Exception){taskStatus.Text="Task list unavailable.";}
   }
   date.SelectedDateChanged+=(_,_)=>LoadTasks();LoadTasks();
  }
  var amount=new TextBox{MaxLength=20,Text=original?.Amount?.ToString("0.00",CultureInfo.InvariantCulture)??""};Func<int>? duration=null;if(expense){p.Children.Add(new TextBlock{Text="Amount (USD)"});p.Children.Add(amount);}else duration=WorkWindowFields.Duration(p,original?.Minutes);
  var category=new ComboBox{ItemsSource=LocalPractice.ExpenseCategories,SelectedItem=original?.Category};if(expense){p.Children.Add(new TextBlock{Text="Expense category"});p.Children.Add(category);}
  var note=new TextBox{MaxLength=1000,Height=100,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,Text=original?.Description??""};p.Children.Add(new TextBlock{Text=expense?"Expense description and receipt evidence reference":"Work description"});p.Children.Add(note);var historicalReason=new TextBox{MaxLength=1000,Height=65,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap};if(historical){p.Children.Add(new TextBlock{Text="Reason for late reporting / evidence. Only actual work performed before closure is permitted; this does not authorize further work.",TextWrapping=TextWrapping.Wrap});p.Children.Add(historicalReason);}var confirm=new CheckBox{Content=historical?"I confirm the actual work or expense occurred before closure.":"I confirm these are corrected actual facts."};if(original!=null||historical)p.Children.Add(confirm);var save=new Button{Content=original==null?"Submit":"Submit correction"};p.Children.Add(save);w.Content=new ScrollViewer{Content=p};var key=Guid.NewGuid();
  save.Click+=(_,_)=>{try{var taskId=original?.TaskId??(task.SelectedItem as ProjectTask)?.Id??throw new ArgumentException("Select a task.");if(original!=null&&confirm.IsChecked!=true)throw new ArgumentException("Confirm corrected actual facts.");int? minutes=null;decimal? cost=null;if(expense){if(!decimal.TryParse(amount.Text,NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out var v))throw new ArgumentException("Enter USD amount.");cost=v;}else minutes=duration!();var input=new WorkSubmissionInput(key,taskId,WorkWindowFields.Day(date),expense?"Expense":"Time",minutes,cost,expense?category.SelectedItem as string:null,note.Text);if(historical){if(original!=null){if(!original.CurrentClosureId.HasValue)throw new ArgumentException("Current closure required.");service.ReplaceClosedProjectWork(original.Id,input,original.CurrentClosureId.Value,original.ReviewState=="Invalidated",historicalReason.Text,confirm.IsChecked==true);}else{if(task.SelectedItem is not ProjectTask selected||!selected.ClosureId.HasValue)throw new ArgumentException("Select current closed task.");service.SubmitHistoricalWork(input,selected.ClosureId.Value,historicalReason.Text,confirm.IsChecked==true);}}else if(original==null)service.SubmitWork(input);else if(original.ReviewState=="Invalidated")service.ReplaceInvalidatedWork(original.Id,input);else service.ReplaceRejectedWork(original.Id,input);w.DialogResult=true;}catch(ArgumentException ex){MessageBox.Show(w,ex.Message);}catch(Exception){MessageBox.Show(w,"Submission failed. Do not recreate before checking history.");}};if(w.ShowDialog()==true)Refresh();
 }
}
internal sealed class WorkReviewWindow:Window
{
 readonly LocalPractice service;readonly DataGrid reviews=WorkWindowFields.Grid(),billing=WorkWindowFields.Grid(),overheadReviews=WorkWindowFields.Grid();
 readonly DatePicker start=new(){SelectedDate=DateTime.Today.AddDays(-30)},end=new(){SelectedDate=DateTime.Today};
 readonly CheckBox pending=new(){Content="Unreviewed / unclassified only",IsChecked=true};
 readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap},selectedInfo=new(){TextWrapping=TextWrapping.Wrap};
 readonly List<(Button Button,Func<bool> Allowed)> reviewActions=new();
 bool loaded;
 public WorkReviewWindow(LocalPractice service)
 {
  this.service=service;Title="Work approvals and billing";Width=1080;Height=650;WindowStartupLocation=WindowStartupLocation.CenterOwner;
  var root=new DockPanel{Margin=new Thickness(16)};var head=new StackPanel();head.Children.Add(new TextBlock{Text=Title,FontSize=24,FontWeight=FontWeights.SemiBold});head.Children.Add(new TextBlock{Text="First approve actual work or expenses, then decide whether approved project work is billable. Independent review is required unless the founding-owner exception applies. No payments or invoices are created here.",TextWrapping=TextWrapping.Wrap});var filters=new WrapPanel();filters.Children.Add(new TextBlock{Text="From",Margin=new Thickness(4,8,4,0)});filters.Children.Add(start);filters.Children.Add(new TextBlock{Text="Through",Margin=new Thickness(4,8,4,0)});filters.Children.Add(end);filters.Children.Add(pending);WorkWindowFields.Button(filters,"Refresh",()=>Run(Refresh));if(service.Can("Work.Review")&&service.Can("Work.Correct"))WorkWindowFields.Button(filters,"Attendance corrections (UTC)",()=>Run(()=>new AttendanceCorrectionWindow(service){Owner=this}.ShowDialog()));head.Children.Add(filters);head.Children.Add(status);DockPanel.SetDock(head,Dock.Top);root.Children.Add(head);
  var tabs=new TabControl();
  bool ReviewGrant()=>loaded&&service.Can("Work.Review")&&service.Can("Projects.View")&&service.Can("Clients.View")&&service.Can("Employees.View");
  bool CorrectGrant()=>ReviewGrant()&&service.Can("Work.Correct");
  WorkReviewRow? ProjectRecord()=>reviews.SelectedItem as WorkReviewRow;
  OverheadReviewRow? NonProjectRecord()=>overheadReviews.SelectedItem as OverheadReviewRow;
  WorkBillingRow? BillingRecord()=>billing.SelectedItem as WorkBillingRow;
  DockPanel Page(DataGrid g,params (string Label,Action Action,Func<bool> Allowed)[] buttons)
  {
   var p=new DockPanel();var bar=new WrapPanel();
   foreach(var (label,action,allowed) in buttons)
   {
    var button=new Button{Content=label,Margin=new Thickness(2),IsEnabled=false};
    button.Click+=(_,_)=>Run(()=>{if(!allowed())throw new ArgumentException("Select an eligible record.");action();});
    reviewActions.Add((button,allowed));bar.Children.Add(button);
   }
   DockPanel.SetDock(bar,Dock.Top);p.Children.Add(bar);p.Children.Add(g);return p;
  }
  foreach(var g in new[]{reviews,billing})WorkWindowFields.Columns(g,("Employee","Employee"),("Project","ProjectNumber"),("Task","Task"),("Kind","Kind"),("Date","Date"),("Minutes","Minutes"),("Expense USD","Amount"),("Category","Category"),("Description","Description"));
  WorkWindowFields.Columns(reviews,("Review","ReviewState"),("Record ID","Id"));
  WorkWindowFields.Columns(billing,("Billing","Classification"));
  WorkWindowFields.Columns(overheadReviews,("Employee","Employee"),("Date","Date"),("Minutes","Minutes"),("Category","Category"),("Description","Description"),("Review","ReviewState"),("Record ID","Id"));
  if(service.Can("Work.Review"))
  {
   var actions=new List<(string Label,Action Action,Func<bool> Allowed)>{
    ("Approve",()=>Review(true),()=>ReviewGrant()&&ProjectRecord()?.ReviewState=="Submitted"),
    ("Reject",()=>Review(false),()=>ReviewGrant()&&ProjectRecord()?.ReviewState=="Submitted")};
   if(service.Can("Work.Correct"))
   {
    actions.Add(("Correct erroneous approval",InvalidateApproved,()=>CorrectGrant()&&ProjectRecord()?.ReviewState=="Approved"));
    actions.Add(("View correction evidence",ViewInvalidation,()=>CorrectGrant()&&ProjectRecord()?.ReviewState=="Invalidated"));
    if(service.Can("Billing.Invoices.View")&&service.Can("Billing.Invoices.Edit"))
     actions.Add(("Review cancelled allocations",ReconcileAllocations,()=>CorrectGrant()&&service.Can("Billing.Invoices.View")&&service.Can("Billing.Invoices.Edit")&&ProjectRecord()?.ReviewState=="Approved"));
   }
   tabs.Items.Add(new TabItem{Header="Project time & expenses",Content=Page(reviews,actions.ToArray())});
   var overheadActions=new List<(string Label,Action Action,Func<bool> Allowed)>{
    ("Approve",()=>ReviewOverhead(true),()=>ReviewGrant()&&NonProjectRecord()?.ReviewState=="Submitted"),
    ("Reject",()=>ReviewOverhead(false),()=>ReviewGrant()&&NonProjectRecord()?.ReviewState=="Submitted")};
   if(service.Can("Work.Correct"))
   {
    overheadActions.Add(("Correct erroneous approval",InvalidateOverhead,()=>CorrectGrant()&&NonProjectRecord()?.ReviewState=="Approved"));
    overheadActions.Add(("View correction evidence",ViewOverheadInvalidation,()=>CorrectGrant()&&NonProjectRecord()?.ReviewState=="Invalidated"));
   }
   tabs.Items.Add(new TabItem{Header="Non-project time",Content=Page(overheadReviews,overheadActions.ToArray())});
  }
  if(service.Can("Billing.Classify"))
   tabs.Items.Add(new TabItem{Header="Billing decisions",Content=Page(billing,
    ("Billable",()=>Classify(true),()=>loaded&&service.Can("Billing.Classify")&&service.Can("Projects.View")&&service.Can("Clients.View")&&service.Can("Employees.View")&&BillingRecord()?.Classification=="Unclassified"),
    ("Nonbillable",()=>Classify(false),()=>loaded&&service.Can("Billing.Classify")&&service.Can("Projects.View")&&service.Can("Clients.View")&&service.Can("Employees.View")&&BillingRecord()?.Classification=="Unclassified"))});
  DockPanel.SetDock(selectedInfo,Dock.Bottom);selectedInfo.Margin=new Thickness(0,8,0,0);root.Children.Add(selectedInfo);
  void Details()
  {
   UpdateActions();selectedInfo.Text="";
   if(!loaded)return;
   if(tabs.SelectedItem is not TabItem tab)return;
   if(Equals(tab.Header,"Project time & expenses")&&ReviewGrant()&&ProjectRecord() is WorkReviewRow r)
    selectedInfo.Text=$"Record #{r.Id} | Replaces: {r.ReplacesId?.ToString()??"—"} | Replacement: {r.ReplacementId?.ToString()??"—"} | Entry closure: {r.ClosureId?.ToString()??"—"} | Current closure: {r.CurrentClosureId?.ToString()??"—"}\nHistorical evidence: {r.HistoricalReason??"Not applicable"}";
   else if(Equals(tab.Header,"Non-project time")&&ReviewGrant()&&NonProjectRecord() is OverheadReviewRow o)
    selectedInfo.Text=$"Record #{o.Id} | Replaces: {o.ReplacesId?.ToString()??"—"} | Replacement: {o.ReplacementId?.ToString()??"—"}";
  }
  reviews.SelectionChanged+=(_,_)=>Details();overheadReviews.SelectionChanged+=(_,_)=>Details();billing.SelectionChanged+=(_,_)=>Details();tabs.SelectionChanged+=(_,_)=>Details();
  pending.Checked+=(_,_)=>Run(Refresh);pending.Unchecked+=(_,_)=>Run(Refresh);
  root.Children.Add(tabs);Content=root;Run(Refresh);
 }
 void UpdateActions(){foreach(var a in reviewActions){try{a.Button.IsEnabled=a.Allowed();}catch(Exception){a.Button.IsEnabled=false;}}}
 void Clear(){loaded=false;reviews.ItemsSource=billing.ItemsSource=overheadReviews.ItemsSource=null;selectedInfo.Text="";UpdateActions();}
 void Run(Action a){try{a();}catch(UnauthorizedAccessException){Clear();status.Text="Access denied.";}catch(ArgumentException e){status.Text=e.Message;}catch(Exception){Clear();status.Text="Operation failed. Refresh before retrying.";}finally{UpdateActions();}}
 void Refresh()
 {
  Clear();var a=WorkWindowFields.Day(start);var b=WorkWindowFields.Day(end);var counts=new List<string>();
  if(service.Can("Work.Review")&&service.Can("Projects.View")&&service.Can("Clients.View")&&service.Can("Employees.View")){var project=service.ReadWorkReview(a,b,pending.IsChecked==true);var nonProject=service.ReadOverheadReview(a,b,pending.IsChecked==true);reviews.ItemsSource=project;overheadReviews.ItemsSource=nonProject;counts.Add(project.Count+" project records");counts.Add(nonProject.Count+" non-project records");}
  if(service.Can("Billing.Classify")&&service.Can("Projects.View")&&service.Can("Clients.View")&&service.Can("Employees.View")){var records=service.ReadBillingWork(a,b,pending.IsChecked==true);billing.ItemsSource=records;counts.Add(records.Count+" billing records");}
  loaded=true;status.Text=counts.Count==0?"No review access.":string.Join(" · ",counts);UpdateActions();
 }
 bool ConfirmOwner(bool applicable)=>applicable&&service.Can("Administration.Manage")&&service.ReadOwnerApprovalPolicy().Enabled&&MessageBox.Show(this,"If this is your own submission, explicitly invoke the single-owner exception using the justification just entered? Otherwise choose No for ordinary independent review.","Owner exception",MessageBoxButton.YesNo,MessageBoxImage.Warning)==MessageBoxResult.Yes;
 void Review(bool approved){if(reviews.SelectedItem is not WorkReviewRow r)throw new ArgumentException("Select a submission.");var n=WorkWindowFields.Note(this,approved?"Approve facts":"Reject facts");if(n==null)return;var outcome=service.ReviewWork(r.Id,approved,n,ConfirmOwner(approved));Refresh();status.Text=outcome;}
 void InvalidateApproved()
 {
  if(reviews.SelectedItem is not WorkReviewRow r||r.ReviewState!="Approved")throw new ArgumentException("Select an approved fact (disable pending-only filter).");
  if(MessageBox.Show(this,"Only demonstrably erroneous or duplicate entries never allocated to an invoice may be invalidated. Do not invalidate actual earned wages or valid reimbursement. Original approval stays in history. Continue?","Confirm correction",MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes)return;
  var reason=WorkWindowFields.Note(this,"Document factual error");if(reason==null)return;
  if(r.CurrentClosureId.HasValue)service.InvalidateClosedProjectWork(r.Id,r.CurrentClosureId.Value,reason,true);else service.InvalidateApprovedWork(r.Id,reason,true);Refresh();status.Text="Invalidation recorded. No payroll or accounting transaction created.";
 }
 void ReconcileAllocations()
 {
  if(reviews.SelectedItem is not WorkReviewRow r||r.ReviewState!="Approved")throw new ArgumentException("Select approved erroneous work.");
  var review=service.ReadWorkAllocationReview(r.ProjectId,r.Id);
  var details=string.Join("\n",review.Allocations.Select(x=>$"{x.DraftNumber} | {x.State} | USD {x.Amount:0.00}\nSHA256: {x.PayloadHash}"));
  if(!review.Eligible){MessageBox.Show(this,"Not eligible for cancelled-only correction. Approved, live, exported or otherwise unresolved allocations remain protected.\n"+details,"Allocation review");return;}
  if(MessageBox.Show(this,$"Source #{r.Id}; exact allocation evidence:\n{details}\n\nConfirm only if the work fact is demonstrably erroneous AND independent review confirms no external invoice was issued, including manual issuance outside this software. This does not reverse wages, payments or invoices. Continue?","Cancelled allocation reconciliation",MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes)return;
  var note=WorkWindowFields.Note(this,"Document factual error and evidence of no external issuance");if(note==null)return;
  service.ReconcileCancelledWorkAllocations(r.ProjectId,r.Id,review.EvidenceHash,r.CurrentClosureId,note,true);Refresh();status.Text="Cancelled allocation reconciliation recorded; no accounting transaction created.";
 }
 void ViewInvalidation()
 {
  if(reviews.SelectedItem is not WorkReviewRow r)throw new ArgumentException("Select a submission.");
  var e=service.ReadWorkInvalidation(r.Id)??throw new ArgumentException("No invalidation recorded.");
  MessageBox.Show(this,$"Record #{e.SubmissionId}\nActor ID: {e.ActorId}\nUTC: {e.RecordedUtc}\nReason: {e.Reason}\nClosure ID: {e.ClosureId?.ToString()??"Active project"}\nAllocation review SHA256: {e.AllocationReviewHash??"Not applicable"}","Invalidation evidence");
 }
 void InvalidateOverhead()
 {
  if(overheadReviews.SelectedItem is not OverheadReviewRow r||r.ReviewState!="Approved")throw new ArgumentException("Select an approved overhead fact (disable pending-only filter).");
  if(MessageBox.Show(this,"Invalidate only a demonstrably erroneous or duplicate entry, never actual earned wages or valid time worked. Original approval remains in history. Continue?","Confirm overhead correction",MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes)return;
  var reason=WorkWindowFields.Note(this,"Document factual error");if(reason==null)return;
  service.InvalidateApprovedOverhead(r.Id,reason,true);Refresh();status.Text="Overhead invalidation recorded. No payroll or accounting transaction created.";
 }
 void ViewOverheadInvalidation()
 {
  if(overheadReviews.SelectedItem is not OverheadReviewRow r)throw new ArgumentException("Select overhead submission.");
  var e=service.ReadOverheadInvalidation(r.Id)??throw new ArgumentException("No invalidation recorded.");
  MessageBox.Show(this,$"Record #{e.SubmissionId}\nActor ID: {e.ActorId}\nUTC: {e.RecordedUtc}\nReason: {e.Reason}","Overhead invalidation evidence");
 }
 void ReviewOverhead(bool approved){if(overheadReviews.SelectedItem is not OverheadReviewRow r)throw new ArgumentException("Select overhead submission.");var n=WorkWindowFields.Note(this,approved?"Approve overhead facts":"Reject overhead facts");if(n==null)return;var outcome=service.ReviewOverheadTime(r.Id,approved,n,ConfirmOwner(approved));Refresh();status.Text=outcome;}
 void Classify(bool billable){if(billing.SelectedItem is not WorkBillingRow r)throw new ArgumentException("Select approved work.");var n=WorkWindowFields.Note(this,"Classify billing");if(n==null)return;var outcome=service.ClassifyWork(r.Id,billable,n,ConfirmOwner(true));Refresh();status.Text=outcome;}
}
