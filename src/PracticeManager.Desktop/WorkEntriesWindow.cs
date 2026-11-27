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
  var root=new DockPanel{Margin=new Thickness(16)};var head=new StackPanel();head.Children.Add(new TextBlock{Text=Title,FontSize=24,FontWeight=FontWeights.SemiBold});head.Children.Add(new TextBlock{Text="Attendance is not billable time. Submit actual project work against your own started task, or non-project overhead time separately. Submitted facts cannot be edited; use the applicable Replace rejected action to retain the correction link. No rates or billing classifications shown.",TextWrapping=TextWrapping.Wrap});var bar=new WrapPanel();bar.Children.Add(start);bar.Children.Add(end);WorkWindowFields.Button(bar,"Refresh",()=>Run(Refresh));WorkWindowFields.Button(bar,"Clock in",()=>Run(()=>{var outcome=service.ClockIn();Refresh();status.Text=outcome;}));WorkWindowFields.Button(bar,"Clock out",()=>Run(()=>{var a=service.ReadMyOpenAttendance();if(a==null)throw new ArgumentException("No open attendance session.");var outcome=service.ClockOut(a.Id);Refresh();status.Text=outcome;}));WorkWindowFields.Button(bar,"Submit time",()=>Run(()=>Submit(false)));WorkWindowFields.Button(bar,"Submit expense",()=>Run(()=>Submit(true)));WorkWindowFields.Button(bar,"Submit overhead time",()=>Run(()=>SubmitOverhead()));WorkWindowFields.Button(bar,"Replace rejected / invalidated overhead",()=>Run(ReplaceOverhead));WorkWindowFields.Button(bar,"Replace rejected / invalidated project record",()=>Run(ReplaceRejected));head.Children.Add(bar);head.Children.Add(status);DockPanel.SetDock(head,Dock.Top);root.Children.Add(head);
  WorkWindowFields.Columns(entries,("Record ID","Id"),("Replaces","ReplacesId"),("Replacement","ReplacementId"),("Project","ProjectNumber"),("Task","Task"),("Kind","Kind"),("Date","Date"),("Minutes","Minutes"),("Expense USD","Amount"),("Category","Category"),("Description","Description"),("Review","ReviewState"));
  WorkWindowFields.Columns(overhead,("Record ID","Id"),("Replaces","ReplacesId"),("Replacement","ReplacementId"),("Date","Date"),("Minutes","Minutes"),("Category","Category"),("Description","Description"),("Review","ReviewState"));
  WorkWindowFields.Columns(attendance,("Clock in UTC","ClockIn"),("Clock out UTC","ClockOut"));
  var tabs=new TabControl();tabs.Items.Add(new TabItem{Header="Project submissions",Content=entries});tabs.Items.Add(new TabItem{Header="Nonbillable overhead",Content=overhead});tabs.Items.Add(new TabItem{Header="Attendance (UTC range)",Content=attendance});root.Children.Add(tabs);Content=root;Run(Refresh);
 }
 void Run(Action a){try{a();}catch(UnauthorizedAccessException){entries.ItemsSource=attendance.ItemsSource=overhead.ItemsSource=null;status.Text="Access denied or no active employee link.";}catch(ArgumentException e){status.Text=e.Message;}catch(Exception){status.Text="Operation failed. Refresh before retrying.";}}
 void Refresh(){var a=WorkWindowFields.Day(start);var b=WorkWindowFields.Day(end);entries.ItemsSource=null;attendance.ItemsSource=null;entries.ItemsSource=service.ReadMySubmissions(a,b);attendance.ItemsSource=service.ReadMyAttendance(a,b);overhead.ItemsSource=service.ReadMyOverheadTime(a,b);status.Text=service.ReadMyOpenAttendance() is null?"Not clocked in.":"Attendance session open.";}
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
  var minutes=new TextBox{MaxLength=4,Text=original?.Minutes.ToString(CultureInfo.InvariantCulture)??""};p.Children.Add(new TextBlock{Text="Actual minutes"});p.Children.Add(minutes);
  var note=new TextBox{MaxLength=1000,Height=80,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,Text=original?.Description??""};p.Children.Add(new TextBlock{Text="Description"});p.Children.Add(note);
  var confirm=new CheckBox{Content="I confirm these are corrected actual minutes."};if(original!=null)p.Children.Add(confirm);var save=new Button{Content=original==null?"Submit immutable overhead time":"Submit linked replacement"};p.Children.Add(save);w.Content=new ScrollViewer{Content=p};var key=Guid.NewGuid();
  save.Click+=(_,_)=>{try{if(category.SelectedItem is not string cat||!int.TryParse(minutes.Text,NumberStyles.None,CultureInfo.InvariantCulture,out var m))throw new ArgumentException("Select category and enter actual minutes.");if(original!=null&&confirm.IsChecked!=true)throw new ArgumentException("Confirm corrected actual facts.");var input=new OverheadTimeInput(key,WorkWindowFields.Day(date),m,cat,note.Text);if(original==null)service.SubmitOverheadTime(input);else if(original.ReviewState=="Invalidated")service.ReplaceInvalidatedOverheadTime(original.Id,input);else service.ReplaceRejectedOverheadTime(original.Id,input);w.DialogResult=true;}catch(ArgumentException ex){MessageBox.Show(w,ex.Message);}catch(UnauthorizedAccessException){MessageBox.Show(w,"Access denied.");}catch(Exception){MessageBox.Show(w,"Submission failed; check history before retrying.");}};if(w.ShowDialog()==true)Refresh();
 }
 void ReplaceRejected()
 {
  if(entries.SelectedItem is not OwnWorkSubmission x||(x.ReviewState!="Rejected"&&x.ReviewState!="Invalidated")||x.ReplacementId.HasValue)throw new ArgumentException("Select an unreplaced rejected or invalidated project record.");
  Submit(x.Kind=="Expense",x);
 }
 void Submit(bool expense,OwnWorkSubmission? original=null)
 {
  var w=new Window{Owner=this,Title=original!=null?"Replace rejected or invalidated record":expense?"Submit expense":"Submit time",Width=570,Height=470,WindowStartupLocation=WindowStartupLocation.CenterOwner};var p=new StackPanel{Margin=new Thickness(16)};
  var task=new ComboBox{ItemsSource=service.ReadMyWork(WorkWindowFields.Day(start),WorkWindowFields.Day(end)).Where(x=>x.ProjectState=="Active"&&x.State is "InProgress" or "Completed" or "Discontinued").ToArray(),DisplayMemberPath="WorkEntryLabel"};p.Children.Add(new TextBlock{Text="Own started task — discontinued assignments: historical actual work only, through stop date.",TextWrapping=TextWrapping.Wrap});if(original==null)p.Children.Add(task);else{task.Visibility=Visibility.Collapsed;p.Children.Add(new TextBlock{Text=$"Original #{original.Id}: {original.ProjectNumber} / {original.Task}. Original decision remains in history; corrected facts require new review.",TextWrapping=TextWrapping.Wrap});}
  var date=new DatePicker{SelectedDate=original?.Date.ToDateTime(TimeOnly.MinValue)??DateTime.Today};p.Children.Add(new TextBlock{Text="Work / expense date"});p.Children.Add(date);
  var amount=new TextBox{MaxLength=20,Text=original==null?"":expense?original.Amount?.ToString("0.00",CultureInfo.InvariantCulture)??"":original.Minutes?.ToString(CultureInfo.InvariantCulture)??""};p.Children.Add(new TextBlock{Text=expense?"Expense USD":"Actual minutes"});p.Children.Add(amount);
  var category=new ComboBox{ItemsSource=LocalPractice.ExpenseCategories,SelectedItem=original?.Category};if(expense){p.Children.Add(new TextBlock{Text="Expense category"});p.Children.Add(category);}
  var note=new TextBox{MaxLength=1000,Height=100,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,Text=original?.Description??""};p.Children.Add(new TextBlock{Text=expense?"Expense description and receipt evidence reference":"Work description"});p.Children.Add(note);var confirm=new CheckBox{Content="I confirm these are corrected actual facts."};if(original!=null)p.Children.Add(confirm);var save=new Button{Content=original==null?"Submit immutable record":"Submit linked replacement"};p.Children.Add(save);w.Content=new ScrollViewer{Content=p};var key=Guid.NewGuid();
  save.Click+=(_,_)=>{try{var taskId=original?.TaskId??(task.SelectedItem as ProjectTask)?.Id??throw new ArgumentException("Select a task.");if(original!=null&&confirm.IsChecked!=true)throw new ArgumentException("Confirm corrected actual facts.");int? minutes=null;decimal? cost=null;if(expense){if(!decimal.TryParse(amount.Text,NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out var v))throw new ArgumentException("Enter USD amount.");cost=v;}else{if(!int.TryParse(amount.Text,NumberStyles.None,CultureInfo.InvariantCulture,out var m))throw new ArgumentException("Enter minutes.");minutes=m;}var input=new WorkSubmissionInput(key,taskId,WorkWindowFields.Day(date),expense?"Expense":"Time",minutes,cost,expense?category.SelectedItem as string:null,note.Text);if(original==null)service.SubmitWork(input);else if(original.ReviewState=="Invalidated")service.ReplaceInvalidatedWork(original.Id,input);else service.ReplaceRejectedWork(original.Id,input);w.DialogResult=true;}catch(ArgumentException ex){MessageBox.Show(w,ex.Message);}catch(Exception){MessageBox.Show(w,"Submission failed. Do not recreate before checking history.");}};if(w.ShowDialog()==true)Refresh();
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
  WorkWindowFields.Columns(reviews,("Record ID","Id"),("Replaces","ReplacesId"),("Replacement","ReplacementId"),("Review","ReviewState"));WorkWindowFields.Columns(billing,("Classification","Classification"));
  WorkWindowFields.Columns(overheadReviews,("Record ID","Id"),("Replaces","ReplacesId"),("Replacement","ReplacementId"),("Employee","Employee"),("Date","Date"),("Minutes","Minutes"),("Category","Category"),("Description","Description"),("Review","ReviewState"));
  if(service.Can("Work.Review")){var actions=new List<(string Label,Action Action)>{("Approve",()=>Review(true)),("Reject",()=>Review(false))};if(service.Can("Work.Correct")){actions.Add(("Invalidate erroneous approved fact",InvalidateApproved));actions.Add(("Invalidation evidence",ViewInvalidation));}tabs.Items.Add(new TabItem{Header="Fact review",Content=Page(reviews,actions.ToArray())});var overheadActions=new List<(string Label,Action Action)>{("Approve",()=>ReviewOverhead(true)),("Reject",()=>ReviewOverhead(false))};if(service.Can("Work.Correct")){overheadActions.Add(("Invalidate erroneous approved overhead",InvalidateOverhead));overheadActions.Add(("Invalidation evidence",ViewOverheadInvalidation));}tabs.Items.Add(new TabItem{Header="Overhead review",Content=Page(overheadReviews,overheadActions.ToArray())});}
  if(service.Can("Billing.Classify"))tabs.Items.Add(new TabItem{Header="Billing classification",Content=Page(billing,("Billable",()=>Classify(true)),("Nonbillable",()=>Classify(false)))});
  root.Children.Add(tabs);Content=root;Run(Refresh);
 }
 void Run(Action a){try{a();}catch(UnauthorizedAccessException){reviews.ItemsSource=billing.ItemsSource=overheadReviews.ItemsSource=null;status.Text="Access denied.";}catch(ArgumentException e){status.Text=e.Message;}catch(Exception){status.Text="Operation failed. Refresh before retrying.";}}
 void Refresh(){reviews.ItemsSource=billing.ItemsSource=overheadReviews.ItemsSource=null;var a=WorkWindowFields.Day(start);var b=WorkWindowFields.Day(end);if(service.Can("Work.Review")){reviews.ItemsSource=service.ReadWorkReview(a,b,pending.IsChecked==true);overheadReviews.ItemsSource=service.ReadOverheadReview(a,b,pending.IsChecked==true);}if(service.Can("Billing.Classify"))billing.ItemsSource=service.ReadBillingWork(a,b,pending.IsChecked==true);status.Text="";}
 bool ConfirmOwner(bool applicable)=>applicable&&service.Can("Administration.Manage")&&service.ReadOwnerApprovalPolicy().Enabled&&MessageBox.Show(this,"If this is your own submission, explicitly invoke the single-owner exception using the justification just entered? Otherwise choose No for ordinary independent review.","Owner exception",MessageBoxButton.YesNo,MessageBoxImage.Warning)==MessageBoxResult.Yes;
 void Review(bool approved){if(reviews.SelectedItem is not WorkReviewRow r)throw new ArgumentException("Select a submission.");var n=WorkWindowFields.Note(this,approved?"Approve facts":"Reject facts");if(n==null)return;var outcome=service.ReviewWork(r.Id,approved,n,ConfirmOwner(approved));Refresh();status.Text=outcome;}
 void InvalidateApproved()
 {
  if(reviews.SelectedItem is not WorkReviewRow r||r.ReviewState!="Approved")throw new ArgumentException("Select an approved fact (disable pending-only filter).");
  if(MessageBox.Show(this,"Only demonstrably erroneous or duplicate entries never allocated to an invoice may be invalidated. Do not invalidate actual earned wages or valid reimbursement. Original approval stays in history. Continue?","Confirm correction",MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes)return;
  var reason=WorkWindowFields.Note(this,"Document factual error");if(reason==null)return;
  service.InvalidateApprovedWork(r.Id,reason,true);Refresh();status.Text="Invalidation recorded. No payroll or accounting transaction created.";
 }
 void ViewInvalidation()
 {
  if(reviews.SelectedItem is not WorkReviewRow r)throw new ArgumentException("Select a submission.");
  var e=service.ReadWorkInvalidation(r.Id)??throw new ArgumentException("No invalidation recorded.");
  MessageBox.Show(this,$"Record #{e.SubmissionId}\nActor ID: {e.ActorId}\nUTC: {e.RecordedUtc}\nReason: {e.Reason}","Invalidation evidence");
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
