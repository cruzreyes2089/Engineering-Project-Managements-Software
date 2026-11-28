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
  var root=new DockPanel{Margin=new Thickness(16)};var head=new StackPanel();head.Children.Add(new TextBlock{Text=Title,FontSize=24,FontWeight=FontWeights.SemiBold});head.Children.Add(new TextBlock{Text="Enter your actual work and expenses. Use office time for non-project work. Clock in and out separately.",TextWrapping=TextWrapping.Wrap});var bar=new WrapPanel();bar.Children.Add(start);bar.Children.Add(end);WorkWindowFields.Button(bar,"Refresh",()=>Run(Refresh));head.Children.Add(bar);head.Children.Add(status);DockPanel.SetDock(head,Dock.Top);root.Children.Add(head);
  WorkWindowFields.Columns(entries,("Record ID","Id"),("Replaces","ReplacesId"),("Replacement","ReplacementId"),("Current closure","CurrentClosureId"),("Entry closure","ClosureId"),("Historical evidence","HistoricalReason"),("Project","ProjectNumber"),("Task","Task"),("Kind","Kind"),("Date","Date"),("Minutes","Minutes"),("Expense USD","Amount"),("Category","Category"),("Description","Description"),("Review","ReviewState"));
  WorkWindowFields.Columns(overhead,("Record ID","Id"),("Replaces","ReplacesId"),("Replacement","ReplacementId"),("Date","Date"),("Minutes","Minutes"),("Category","Category"),("Description","Description"),("Review","ReviewState"));
  WorkWindowFields.Columns(attendance,("Session ID","Id"),("Source","EntrySource"),("State","State"),("Recorded interval in UTC","ClockIn"),("Recorded interval out UTC","ClockOut"),("Correction ID","CorrectionId"));
  var tabs=new TabControl();
  DockPanel Page(DataGrid data,Action<WrapPanel> controls){var p=new DockPanel();var top=new WrapPanel();controls(top);DockPanel.SetDock(top,Dock.Top);p.Children.Add(top);p.Children.Add(data);return p;}
  tabs.Items.Add(new TabItem{Header="Project time & expenses",Content=Page(entries,top=>{
   WorkWindowFields.Button(top,"Add time",()=>Run(()=>Submit(false)));WorkWindowFields.Button(top,"Add expense",()=>Run(()=>Submit(true)));
   WorkWindowFields.Button(top,"Correct selected",()=>Run(ReplaceRejected));
   var extra=new WrapPanel();WorkWindowFields.Button(extra,"Late time",()=>Run(()=>Submit(false,null,true)));WorkWindowFields.Button(extra,"Late expense",()=>Run(()=>Submit(true,null,true)));
   top.Children.Add(new Expander{Header="Closed projects",Content=extra,IsExpanded=false});
  })});
  tabs.Items.Add(new TabItem{Header="Office time",Content=Page(overhead,top=>{WorkWindowFields.Button(top,"Add office time",()=>Run(()=>SubmitOverhead()));WorkWindowFields.Button(top,"Correct selected",()=>Run(ReplaceOverhead));})});
  tabs.Items.Add(new TabItem{Header="Attendance",Content=Page(attendance,top=>{
   WorkWindowFields.Button(top,"Clock in",()=>Run(()=>{var outcome=service.ClockIn();RefreshAfterSave(outcome);}));
   WorkWindowFields.Button(top,"Clock out",()=>Run(()=>{var a=service.ReadMyOpenAttendance();if(a==null)throw new ArgumentException("No open attendance session.");var outcome=service.ClockOut(a.Id);RefreshAfterSave(outcome);}));
  })});
  var details=new CheckBox{Content="Show record details",Margin=new Thickness(4)};bar.Children.Add(details);
  var technical=entries.Columns.Take(6).Concat(overhead.Columns.Take(3)).Concat(new[]{attendance.Columns[0],attendance.Columns[5]}).ToArray();
  void ShowDetails(bool show){foreach(var column in technical)column.Visibility=show?Visibility.Visible:Visibility.Collapsed;}
  ShowDetails(false);details.Checked+=(_,_)=>ShowDetails(true);details.Unchecked+=(_,_)=>ShowDetails(false);
  root.Children.Add(tabs);Content=root;start.SelectedDateChanged+=(_,_)=>ClearForFilters();end.SelectedDateChanged+=(_,_)=>ClearForFilters();Run(Refresh);
 }
 void Run(Action a){try{a();}catch(UnauthorizedAccessException){entries.ItemsSource=attendance.ItemsSource=overhead.ItemsSource=null;status.Text="Access denied or no active employee link.";}catch(ArgumentException e){status.Text=e.Message;}catch(Exception){status.Text="Operation failed. Refresh before retrying.";}}
 void ClearForFilters(){entries.ItemsSource=attendance.ItemsSource=overhead.ItemsSource=null;status.Text="Refresh to view the selected dates.";}
 void Refresh(){ClearForFilters();var a=WorkWindowFields.Day(start);var b=WorkWindowFields.Day(end);var work=service.ReadMySubmissions(a,b);var clocks=service.ReadMyAttendance(a,b);var office=service.ReadMyOverheadTime(a,b);var open=service.ReadMyOpenAttendance();entries.ItemsSource=work;attendance.ItemsSource=clocks;overhead.ItemsSource=office;status.Text=(open is null?"Not clocked in.":"Attendance session open.")+" Attendance dates use UTC.";}
 void RefreshAfterSave(string message){try{Refresh();status.Text=message+" "+status.Text;}catch(UnauthorizedAccessException){ClearForFilters();status.Text=message+" Access changed; history unavailable.";}catch(Exception){ClearForFilters();status.Text=message+" Refresh failed; reload before retrying.";} }
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
  var minutes=new TextBox{MaxLength=5,Text=original==null?"":WorkDuration.Format(original.Minutes)};p.Children.Add(new TextBlock{Text="Time (hours:minutes, e.g. 1:30)"});p.Children.Add(minutes);
  var note=new TextBox{MaxLength=1000,Height=80,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,Text=original?.Description??""};p.Children.Add(new TextBlock{Text="Description"});p.Children.Add(note);
  var confirm=new CheckBox{Content="I confirm these are corrected actual minutes."};if(original!=null)p.Children.Add(confirm);var save=new Button{Content=original==null?"Save office time":"Save correction"};p.Children.Add(save);w.Content=new ScrollViewer{Content=p};var key=Guid.NewGuid();
  save.Click+=(_,_)=>{try{if(category.SelectedItem is not string cat)throw new ArgumentException("Select category.");var m=WorkDuration.Parse(minutes.Text);if(original!=null&&confirm.IsChecked!=true)throw new ArgumentException("Confirm corrected actual facts.");var input=new OverheadTimeInput(key,WorkWindowFields.Day(date),m,cat,note.Text);if(original==null)service.SubmitOverheadTime(input);else if(original.ReviewState=="Invalidated")service.ReplaceInvalidatedOverheadTime(original.Id,input);else service.ReplaceRejectedOverheadTime(original.Id,input);w.DialogResult=true;}catch(ArgumentException ex){MessageBox.Show(w,ex.Message);}catch(UnauthorizedAccessException){MessageBox.Show(w,"Access denied.");}catch(Exception){MessageBox.Show(w,"Submission failed; check history before retrying.");}};if(w.ShowDialog()==true)RefreshAfterSave("Office time saved.");
 }
 void ReplaceRejected()
 {
  if(entries.SelectedItem is not OwnWorkSubmission x||(x.ReviewState!="Rejected"&&x.ReviewState!="Invalidated")||x.ReplacementId.HasValue)throw new ArgumentException("Select an unreplaced rejected or invalidated project record.");
  Submit(x.Kind=="Expense",x,x.CurrentClosureId.HasValue);
 }
 void Submit(bool expense,OwnWorkSubmission? original=null,bool historical=false)
 {
  var w=new Window{Owner=this,Title=historical?(original!=null?"Correct historical record — closed project":"Report historical actual work — closed project"):original!=null?"Replace rejected or invalidated record":expense?"Submit expense":"Submit time",Width=570,Height=historical?620:470,WindowStartupLocation=WindowStartupLocation.CenterOwner};var p=new StackPanel{Margin=new Thickness(16)};
  var date=new DatePicker{SelectedDate=original?.Date.ToDateTime(TimeOnly.MinValue)??DateTime.Today};p.Children.Add(new TextBlock{Text="Work / expense date"});if(historical&&original!=null)date.IsEnabled=false;p.Children.Add(date);
  var task=new ComboBox{DisplayMemberPath="WorkEntryLabel"};var taskStatus=new TextBlock{TextWrapping=TextWrapping.Wrap};
  if(original==null){
   p.Children.Add(new TextBlock{Text="Task for selected date"});p.Children.Add(task);p.Children.Add(taskStatus);
   void LoadTasks(){task.ItemsSource=null;task.IsEnabled=false;taskStatus.Text="";
    try{var day=WorkWindowFields.Day(date);if(day>DateOnly.FromDateTime(DateTime.Today))throw new ArgumentException("Select an actual work date, not a future date.");var rows=service.ReadMyWork(day,day).Where(x=>x.CanRecordActualOn(day,historical)).ToArray();task.ItemsSource=rows;task.IsEnabled=rows.Length>0;if(rows.Length==1)task.SelectedIndex=0;if(rows.Length==0)taskStatus.Text="No eligible started task for this date.";}
    catch(UnauthorizedAccessException){taskStatus.Text="Work access denied.";}catch(ArgumentException ex){taskStatus.Text=ex.Message;}catch(Exception){taskStatus.Text="Task list unavailable. Change the date or reopen this form.";}}
   date.SelectedDateChanged+=(_,_)=>LoadTasks();LoadTasks();
  }else p.Children.Add(new TextBlock{Text=$"Original #{original.Id}: {original.ProjectNumber} / {original.Task}. Original decision remains in history; corrected facts require new review.",TextWrapping=TextWrapping.Wrap});
  var amount=new TextBox{MaxLength=expense?20:5,Text=original==null?"":expense?original.Amount?.ToString("0.00",CultureInfo.InvariantCulture)??"":original.Minutes.HasValue?WorkDuration.Format(original.Minutes.Value):""};p.Children.Add(new TextBlock{Text=expense?"Expense USD":"Time (hours:minutes, e.g. 1:30)"});p.Children.Add(amount);
  var category=new ComboBox{ItemsSource=LocalPractice.ExpenseCategories,SelectedItem=original?.Category};if(expense){p.Children.Add(new TextBlock{Text="Expense category"});p.Children.Add(category);}
  var note=new TextBox{MaxLength=1000,Height=100,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,Text=original?.Description??""};p.Children.Add(new TextBlock{Text=expense?"Expense description and receipt evidence reference":"Work description"});p.Children.Add(note);var historicalReason=new TextBox{MaxLength=1000,Height=65,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap};if(historical){p.Children.Add(new TextBlock{Text="Reason for late reporting / evidence. Only actual work performed before closure is permitted; this does not authorize further work.",TextWrapping=TextWrapping.Wrap});p.Children.Add(historicalReason);}var confirm=new CheckBox{Content=historical?"I confirm the actual work or expense occurred before closure.":"I confirm these are corrected actual facts."};if(original!=null||historical)p.Children.Add(confirm);var save=new Button{Content=original==null?"Save":"Save correction"};p.Children.Add(save);w.Content=new ScrollViewer{Content=p};var key=Guid.NewGuid();
  save.Click+=(_,_)=>{try{var taskId=original?.TaskId??(task.SelectedItem as ProjectTask)?.Id??throw new ArgumentException("Select a task.");if(original!=null&&confirm.IsChecked!=true)throw new ArgumentException("Confirm corrected actual facts.");int? minutes=null;decimal? cost=null;if(expense){if(!decimal.TryParse(amount.Text,NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out var v))throw new ArgumentException("Enter USD amount.");cost=v;}else{minutes=WorkDuration.Parse(amount.Text);}var input=new WorkSubmissionInput(key,taskId,WorkWindowFields.Day(date),expense?"Expense":"Time",minutes,cost,expense?category.SelectedItem as string:null,note.Text);if(historical){if(original!=null){if(!original.CurrentClosureId.HasValue)throw new ArgumentException("Current closure required.");service.ReplaceClosedProjectWork(original.Id,input,original.CurrentClosureId.Value,original.ReviewState=="Invalidated",historicalReason.Text,confirm.IsChecked==true);}else{if(task.SelectedItem is not ProjectTask selected||!selected.ClosureId.HasValue)throw new ArgumentException("Select current closed task.");service.SubmitHistoricalWork(input,selected.ClosureId.Value,historicalReason.Text,confirm.IsChecked==true);}}else if(original==null)service.SubmitWork(input);else if(original.ReviewState=="Invalidated")service.ReplaceInvalidatedWork(original.Id,input);else service.ReplaceRejectedWork(original.Id,input);w.DialogResult=true;}catch(ArgumentException ex){MessageBox.Show(w,ex.Message);}catch(Exception){MessageBox.Show(w,"Submission failed. Do not recreate before checking history.");}};if(w.ShowDialog()==true)RefreshAfterSave(expense?"Expense saved.":"Time saved.");
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
  this.service=service;Title="Review time and expenses";Width=1080;Height=650;WindowStartupLocation=WindowStartupLocation.CenterOwner;
  var root=new DockPanel{Margin=new Thickness(16)};var head=new StackPanel();head.Children.Add(new TextBlock{Text=Title,FontSize=24,FontWeight=FontWeights.SemiBold});head.Children.Add(new TextBlock{Text="Approve actual time and expenses first, then decide which approved work is billable. Approval does not issue an invoice or payment.",TextWrapping=TextWrapping.Wrap});var filters=new WrapPanel();filters.Children.Add(start);filters.Children.Add(end);filters.Children.Add(pending);WorkWindowFields.Button(filters,"Refresh",()=>Run(Refresh));if(service.Can("Work.Review")&&service.Can("Work.Correct"))WorkWindowFields.Button(filters,"Attendance corrections (UTC)",()=>Run(()=>new AttendanceCorrectionWindow(service){Owner=this}.ShowDialog()));head.Children.Add(filters);head.Children.Add(status);DockPanel.SetDock(head,Dock.Top);root.Children.Add(head);
  var tabs=new TabControl();DockPanel Page(DataGrid g,params (string Label,Action Action)[] buttons){var p=new DockPanel();var top=new StackPanel();var bar=new WrapPanel();var advanced=new WrapPanel();foreach(var (label,action) in buttons)WorkWindowFields.Button(label is "Approve" or "Reject" or "Billable" or "Nonbillable"?bar:advanced,label,()=>Run(action));top.Children.Add(bar);if(advanced.Children.Count>0)top.Children.Add(new Expander{Header="Corrections and history",Content=advanced,IsExpanded=false});DockPanel.SetDock(top,Dock.Top);p.Children.Add(top);p.Children.Add(g);return p;}
  foreach(var g in new[]{reviews,billing})WorkWindowFields.Columns(g,("Employee","Employee"),("Project","ProjectNumber"),("Task","Task"),("Kind","Kind"),("Date","Date"),("Minutes","Minutes"),("Expense USD","Amount"),("Category","Category"),("Description","Description"));
  WorkWindowFields.Columns(reviews,("Record ID","Id"),("Replaces","ReplacesId"),("Replacement","ReplacementId"),("Current closure","CurrentClosureId"),("Entry closure","ClosureId"),("Historical evidence","HistoricalReason"),("Review","ReviewState"));WorkWindowFields.Columns(billing,("Classification","Classification"));
  WorkWindowFields.Columns(overheadReviews,("Record ID","Id"),("Replaces","ReplacesId"),("Replacement","ReplacementId"),("Employee","Employee"),("Date","Date"),("Minutes","Minutes"),("Category","Category"),("Description","Description"),("Review","ReviewState"));
  if(service.Can("Work.Review")){var actions=new List<(string Label,Action Action)>{("Approve",()=>Review(true)),("Reject",()=>Review(false))};if(service.Can("Work.Correct")){actions.Add(("Invalidate erroneous approved fact",InvalidateApproved));actions.Add(("Invalidation evidence",ViewInvalidation));if(service.Can("Billing.Invoices.View")&&service.Can("Billing.Invoices.Edit"))actions.Add(("Reconcile cancelled allocations",ReconcileAllocations));}tabs.Items.Add(new TabItem{Header="Project time & expenses",Content=Page(reviews,actions.ToArray())});var overheadActions=new List<(string Label,Action Action)>{("Approve",()=>ReviewOverhead(true)),("Reject",()=>ReviewOverhead(false))};if(service.Can("Work.Correct")){overheadActions.Add(("Invalidate erroneous approved overhead",InvalidateOverhead));overheadActions.Add(("Invalidation evidence",ViewOverheadInvalidation));}tabs.Items.Add(new TabItem{Header="Office time",Content=Page(overheadReviews,overheadActions.ToArray())});}
  if(service.Can("Billing.Classify"))tabs.Items.Add(new TabItem{Header="Billable work",Content=Page(billing,("Billable",()=>Classify(true)),("Nonbillable",()=>Classify(false)))});
  var details=new CheckBox{Content="Show record details",Margin=new Thickness(4)};filters.Children.Add(details);var technical=reviews.Columns.Skip(9).Take(6).Concat(overheadReviews.Columns.Take(3)).ToArray();void ShowDetails(bool show){foreach(var col in technical)col.Visibility=show?Visibility.Visible:Visibility.Collapsed;}ShowDetails(false);details.Checked+=(_,_)=>ShowDetails(true);details.Unchecked+=(_,_)=>ShowDetails(false);
  root.Children.Add(tabs);Content=root;start.SelectedDateChanged+=(_,_)=>ClearFilters();end.SelectedDateChanged+=(_,_)=>ClearFilters();pending.Checked+=(_,_)=>Run(Refresh);pending.Unchecked+=(_,_)=>Run(Refresh);Run(Refresh);
 }
 void Run(Action a){try{a();}catch(UnauthorizedAccessException){reviews.ItemsSource=billing.ItemsSource=overheadReviews.ItemsSource=null;status.Text="Access denied.";}catch(ArgumentException e){status.Text=e.Message;}catch(Exception){reviews.ItemsSource=billing.ItemsSource=overheadReviews.ItemsSource=null;status.Text="Operation failed. Refresh before retrying.";}}
 void ClearFilters(){reviews.ItemsSource=billing.ItemsSource=overheadReviews.ItemsSource=null;status.Text="Refresh for selected dates.";}
 void Refresh(){ClearFilters();var a=WorkWindowFields.Day(start);var b=WorkWindowFields.Day(end);if(service.Can("Work.Review")){reviews.ItemsSource=service.ReadWorkReview(a,b,pending.IsChecked==true);overheadReviews.ItemsSource=service.ReadOverheadReview(a,b,pending.IsChecked==true);}if(service.Can("Billing.Classify"))billing.ItemsSource=service.ReadBillingWork(a,b,pending.IsChecked==true);status.Text="";}
 bool ConfirmOwner(bool own)=>!own||MessageBox.Show(this,"You submitted this record. Explicitly proceed under the enabled single-owner exception using your recorded justification?","Owner exception",MessageBoxButton.YesNo,MessageBoxImage.Warning)==MessageBoxResult.Yes;
 void RefreshDecision(string outcome){try{Refresh();status.Text=outcome;}catch(Exception){ClearFilters();status.Text=outcome+". Refresh failed; reload before another decision.";}}
 void Review(bool approved){if(reviews.SelectedItem is not WorkReviewRow r)throw new ArgumentException("Select a submission.");var own=service.WorkReviewRequiresOwnerConfirmation(r.Id,approved);var n=WorkWindowFields.Note(this,own?"Owner self-review justification":approved?"Approve facts":"Reject facts");if(n==null||!ConfirmOwner(own))return;var outcome=service.ReviewWork(r.Id,approved,n,own);RefreshDecision(outcome);}
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
 void ReviewOverhead(bool approved){if(overheadReviews.SelectedItem is not OverheadReviewRow r)throw new ArgumentException("Select overhead submission.");var own=service.WorkReviewRequiresOwnerConfirmation(r.Id,approved,true);var n=WorkWindowFields.Note(this,own?"Owner office-time review justification":approved?"Approve overhead facts":"Reject overhead facts");if(n==null||!ConfirmOwner(own))return;var outcome=service.ReviewOverheadTime(r.Id,approved,n,own);RefreshDecision(outcome);}
 void Classify(bool billable){if(billing.SelectedItem is not WorkBillingRow r)throw new ArgumentException("Select approved work.");var own=service.WorkClassificationRequiresOwnerConfirmation(r.Id);var n=WorkWindowFields.Note(this,own?"Owner billing classification justification":"Classify billing");if(n==null||!ConfirmOwner(own))return;var outcome=service.ClassifyWork(r.Id,billable,n,own);RefreshDecision(outcome);}
}
