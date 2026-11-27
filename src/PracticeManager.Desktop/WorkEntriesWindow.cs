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
 public WorkEntriesWindow(LocalPractice service)
 {
  this.service=service;Title="My time, expenses and attendance";Width=1050;Height=650;WindowStartupLocation=WindowStartupLocation.CenterOwner;
  var root=new DockPanel{Margin=new Thickness(16)};var head=new StackPanel();head.Children.Add(new TextBlock{Text=Title,FontSize=24,FontWeight=FontWeights.SemiBold});head.Children.Add(new TextBlock{Text="Attendance is not billable time. Submit actual project work against your own started task, or non-project overhead time separately. Submitted facts cannot be edited; rejection permits new submission. No rates or billing classifications shown.",TextWrapping=TextWrapping.Wrap});var bar=new WrapPanel();bar.Children.Add(start);bar.Children.Add(end);WorkWindowFields.Button(bar,"Refresh",()=>Run(Refresh));WorkWindowFields.Button(bar,"Clock in",()=>Run(()=>{var outcome=service.ClockIn();Refresh();status.Text=outcome;}));WorkWindowFields.Button(bar,"Clock out",()=>Run(()=>{var a=service.ReadMyOpenAttendance();if(a==null)throw new ArgumentException("No open attendance session.");var outcome=service.ClockOut(a.Id);Refresh();status.Text=outcome;}));WorkWindowFields.Button(bar,"Submit time",()=>Run(()=>Submit(false)));WorkWindowFields.Button(bar,"Submit expense",()=>Run(()=>Submit(true)));WorkWindowFields.Button(bar,"Submit overhead time",()=>Run(SubmitOverhead));head.Children.Add(bar);head.Children.Add(status);DockPanel.SetDock(head,Dock.Top);root.Children.Add(head);
  WorkWindowFields.Columns(entries,("Project","ProjectNumber"),("Task","Task"),("Kind","Kind"),("Date","Date"),("Minutes","Minutes"),("Expense USD","Amount"),("Category","Category"),("Description","Description"),("Review","ReviewState"));
  WorkWindowFields.Columns(overhead,("Date","Date"),("Minutes","Minutes"),("Category","Category"),("Description","Description"),("Review","ReviewState"));
  WorkWindowFields.Columns(attendance,("Clock in UTC","ClockIn"),("Clock out UTC","ClockOut"));
  var tabs=new TabControl();tabs.Items.Add(new TabItem{Header="Project submissions",Content=entries});tabs.Items.Add(new TabItem{Header="Nonbillable overhead",Content=overhead});tabs.Items.Add(new TabItem{Header="Attendance (UTC range)",Content=attendance});root.Children.Add(tabs);Content=root;Run(Refresh);
 }
 void Run(Action a){try{a();}catch(UnauthorizedAccessException){entries.ItemsSource=attendance.ItemsSource=overhead.ItemsSource=null;status.Text="Access denied or no active employee link.";}catch(ArgumentException e){status.Text=e.Message;}catch(Exception){status.Text="Operation failed. Refresh before retrying.";}}
 void Refresh(){var a=WorkWindowFields.Day(start);var b=WorkWindowFields.Day(end);entries.ItemsSource=null;attendance.ItemsSource=null;entries.ItemsSource=service.ReadMySubmissions(a,b);attendance.ItemsSource=service.ReadMyAttendance(a,b);overhead.ItemsSource=service.ReadMyOverheadTime(a,b);status.Text=service.ReadMyOpenAttendance() is null?"Not clocked in.":"Attendance session open.";}
 void SubmitOverhead()
 {
  var w=new Window{Owner=this,Title="Submit non-project overhead time",Width=560,Height=420,WindowStartupLocation=WindowStartupLocation.CenterOwner};var p=new StackPanel{Margin=new Thickness(16)};
  p.Children.Add(new TextBlock{Text="Overhead is nonbillable and is never allocated to a client invoice. Actual minutes count toward the same daily limit as project work.",TextWrapping=TextWrapping.Wrap});
  var date=new DatePicker{SelectedDate=DateTime.Today};p.Children.Add(new TextBlock{Text="Date"});p.Children.Add(date);
  var category=new ComboBox{ItemsSource=LocalPractice.OverheadCategories};p.Children.Add(new TextBlock{Text="Category"});p.Children.Add(category);
  var minutes=new TextBox{MaxLength=4};p.Children.Add(new TextBlock{Text="Actual minutes"});p.Children.Add(minutes);
  var note=new TextBox{MaxLength=1000,Height=80,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap};p.Children.Add(new TextBlock{Text="Description"});p.Children.Add(note);
  var save=new Button{Content="Submit immutable overhead time"};p.Children.Add(save);w.Content=new ScrollViewer{Content=p};var key=Guid.NewGuid();
  save.Click+=(_,_)=>{try{if(category.SelectedItem is not string cat||!int.TryParse(minutes.Text,NumberStyles.None,CultureInfo.InvariantCulture,out var m))throw new ArgumentException("Select category and enter actual minutes.");service.SubmitOverheadTime(new(key,WorkWindowFields.Day(date),m,cat,note.Text));w.DialogResult=true;}catch(ArgumentException ex){MessageBox.Show(w,ex.Message);}catch(UnauthorizedAccessException){MessageBox.Show(w,"Access denied.");}catch(Exception){MessageBox.Show(w,"Submission failed; check history before retrying.");}};if(w.ShowDialog()==true)Refresh();
 }
 void Submit(bool expense)
 {
  var w=new Window{Owner=this,Title=expense?"Submit expense":"Submit time",Width=570,Height=470,WindowStartupLocation=WindowStartupLocation.CenterOwner};var p=new StackPanel{Margin=new Thickness(16)};
  var task=new ComboBox{ItemsSource=service.ReadMyWork(WorkWindowFields.Day(start),WorkWindowFields.Day(end)).Where(x=>x.ProjectState=="Active"&&x.State is "InProgress" or "Completed").ToArray(),DisplayMemberPath="Title"};p.Children.Add(new TextBlock{Text="Own started task"});p.Children.Add(task);
  var date=new DatePicker{SelectedDate=DateTime.Today};p.Children.Add(new TextBlock{Text="Work / expense date"});p.Children.Add(date);
  var amount=new TextBox{MaxLength=20};p.Children.Add(new TextBlock{Text=expense?"Expense USD":"Actual minutes"});p.Children.Add(amount);
  var category=new ComboBox{ItemsSource=LocalPractice.ExpenseCategories};if(expense){p.Children.Add(new TextBlock{Text="Expense category"});p.Children.Add(category);}
  var note=new TextBox{MaxLength=1000,Height=100,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap};p.Children.Add(new TextBlock{Text=expense?"Expense description and receipt evidence reference":"Work description"});p.Children.Add(note);var save=new Button{Content="Submit immutable record"};p.Children.Add(save);w.Content=new ScrollViewer{Content=p};var key=Guid.NewGuid();
  save.Click+=(_,_)=>{try{if(task.SelectedItem is not ProjectTask t)throw new ArgumentException("Select a task.");int? minutes=null;decimal? cost=null;if(expense){if(!decimal.TryParse(amount.Text,NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out var v))throw new ArgumentException("Enter USD amount.");cost=v;}else{if(!int.TryParse(amount.Text,NumberStyles.None,CultureInfo.InvariantCulture,out var m))throw new ArgumentException("Enter minutes.");minutes=m;}service.SubmitWork(new(key,t.Id,WorkWindowFields.Day(date),expense?"Expense":"Time",minutes,cost,expense?category.SelectedItem as string:null,note.Text));w.DialogResult=true;}catch(ArgumentException ex){MessageBox.Show(w,ex.Message);}catch(Exception){MessageBox.Show(w,"Submission failed. Do not recreate before checking history.");}};if(w.ShowDialog()==true)Refresh();
 }
}
internal sealed class WorkReviewWindow:Window
{
 readonly LocalPractice service;readonly DataGrid reviews=WorkWindowFields.Grid(),billing=WorkWindowFields.Grid(),overheadReviews=WorkWindowFields.Grid();
 readonly DatePicker start=new(){SelectedDate=DateTime.Today.AddDays(-30)},end=new(){SelectedDate=DateTime.Today};
 readonly CheckBox pending=new(){Content="Unreviewed / unclassified only",IsChecked=true};
 readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap};
 public WorkReviewWindow(LocalPractice service)
 {
  this.service=service;Title="Work review and billing classification";Width=1080;Height=650;WindowStartupLocation=WindowStartupLocation.CenterOwner;
  var root=new DockPanel{Margin=new Thickness(16)};var head=new StackPanel();head.Children.Add(new TextBlock{Text=Title,FontSize=24,FontWeight=FontWeights.SemiBold});head.Children.Add(new TextBlock{Text="Review factual submissions separately from billing classification. Self-approval is prohibited except for an explicitly enabled founding-owner exception. Independent review is otherwise required. No invoices, reimbursements or payments are created.",TextWrapping=TextWrapping.Wrap});var filters=new WrapPanel();filters.Children.Add(start);filters.Children.Add(end);filters.Children.Add(pending);WorkWindowFields.Button(filters,"Refresh",()=>Run(Refresh));head.Children.Add(filters);head.Children.Add(status);DockPanel.SetDock(head,Dock.Top);root.Children.Add(head);
  var tabs=new TabControl();DockPanel Page(DataGrid g,params (string Label,Action Action)[] buttons){var p=new DockPanel();var bar=new WrapPanel();foreach(var (label,action) in buttons)WorkWindowFields.Button(bar,label,()=>Run(action));DockPanel.SetDock(bar,Dock.Top);p.Children.Add(bar);p.Children.Add(g);return p;}
  foreach(var g in new[]{reviews,billing})WorkWindowFields.Columns(g,("Employee","Employee"),("Project","ProjectNumber"),("Task","Task"),("Kind","Kind"),("Date","Date"),("Minutes","Minutes"),("Expense USD","Amount"),("Category","Category"),("Description","Description"));
  WorkWindowFields.Columns(reviews,("Review","ReviewState"));WorkWindowFields.Columns(billing,("Classification","Classification"));
  WorkWindowFields.Columns(overheadReviews,("Employee","Employee"),("Date","Date"),("Minutes","Minutes"),("Category","Category"),("Description","Description"),("Review","ReviewState"));
  if(service.Can("Work.Review")){tabs.Items.Add(new TabItem{Header="Fact review",Content=Page(reviews,("Approve",()=>Review(true)),("Reject",()=>Review(false)))});tabs.Items.Add(new TabItem{Header="Overhead review",Content=Page(overheadReviews,("Approve",()=>ReviewOverhead(true)),("Reject",()=>ReviewOverhead(false)))});}
  if(service.Can("Billing.Classify"))tabs.Items.Add(new TabItem{Header="Billing classification",Content=Page(billing,("Billable",()=>Classify(true)),("Nonbillable",()=>Classify(false)))});
  root.Children.Add(tabs);Content=root;Run(Refresh);
 }
 void Run(Action a){try{a();}catch(UnauthorizedAccessException){reviews.ItemsSource=billing.ItemsSource=overheadReviews.ItemsSource=null;status.Text="Access denied.";}catch(ArgumentException e){status.Text=e.Message;}catch(Exception){status.Text="Operation failed. Refresh before retrying.";}}
 void Refresh(){reviews.ItemsSource=billing.ItemsSource=overheadReviews.ItemsSource=null;var a=WorkWindowFields.Day(start);var b=WorkWindowFields.Day(end);if(service.Can("Work.Review")){reviews.ItemsSource=service.ReadWorkReview(a,b,pending.IsChecked==true);overheadReviews.ItemsSource=service.ReadOverheadReview(a,b,pending.IsChecked==true);}if(service.Can("Billing.Classify"))billing.ItemsSource=service.ReadBillingWork(a,b,pending.IsChecked==true);status.Text="";}
 bool ConfirmOwner(bool applicable)=>applicable&&service.Can("Administration.Manage")&&service.ReadOwnerApprovalPolicy().Enabled&&MessageBox.Show(this,"If this is your own submission, explicitly invoke the single-owner exception using the justification just entered? Otherwise choose No for ordinary independent review.","Owner exception",MessageBoxButton.YesNo,MessageBoxImage.Warning)==MessageBoxResult.Yes;
 void Review(bool approved){if(reviews.SelectedItem is not WorkReviewRow r)throw new ArgumentException("Select a submission.");var n=WorkWindowFields.Note(this,approved?"Approve facts":"Reject facts");if(n==null)return;var outcome=service.ReviewWork(r.Id,approved,n,ConfirmOwner(approved));Refresh();status.Text=outcome;}
 void ReviewOverhead(bool approved){if(overheadReviews.SelectedItem is not OverheadReviewRow r)throw new ArgumentException("Select overhead submission.");var n=WorkWindowFields.Note(this,approved?"Approve overhead facts":"Reject overhead facts");if(n==null)return;var outcome=service.ReviewOverheadTime(r.Id,approved,n,ConfirmOwner(approved));Refresh();status.Text=outcome;}
 void Classify(bool billable){if(billing.SelectedItem is not WorkBillingRow r)throw new ArgumentException("Select approved work.");var n=WorkWindowFields.Note(this,"Classify billing");if(n==null)return;var outcome=service.ClassifyWork(r.Id,billable,n,ConfirmOwner(true));Refresh();status.Text=outcome;}
}
