using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using PracticeManager.Persistence;
using PracticeManager.Core;
namespace PracticeManager.Desktop;
internal sealed class AttendanceCorrectionWindow:Window
{
 readonly LocalPractice service;
 readonly DataGrid rows=WorkWindowFields.Grid(),history=WorkWindowFields.Grid();
 readonly DatePicker start=new(){SelectedDate=DateTime.UtcNow.Date.AddDays(-30)},end=new(){SelectedDate=DateTime.UtcNow.Date};
 readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap};
 public AttendanceCorrectionWindow(LocalPractice service)
 {
  this.service=service;Title="Restricted attendance corrections — UTC";Width=1150;Height=720;WindowStartupLocation=WindowStartupLocation.CenterOwner;
  var root=new DockPanel{Margin=new Thickness(16)};var head=new StackPanel();
  head.Children.Add(new TextBlock{Text=Title,FontSize=24,FontWeight=FontWeights.SemiBold});
  head.Children.Add(new TextBlock{Text="Correct verified clock-event errors, including missed clock-out. Original clock evidence remains unchanged. No payroll, billable work or wage adjustment is created. Self corrections prohibited.",TextWrapping=TextWrapping.Wrap});
  var bar=new WrapPanel();bar.Children.Add(start);bar.Children.Add(end);WorkWindowFields.Button(bar,"Refresh",()=>Run(Refresh));WorkWindowFields.Button(bar,"Record missing session",()=>Run(Missing));WorkWindowFields.Button(bar,"Correct selected session",()=>Run(Correct));WorkWindowFields.Button(bar,"Void erroneous session",()=>Run(VoidSelected));WorkWindowFields.Button(bar,"Void evidence",()=>Run(ViewVoid));WorkWindowFields.Button(bar,"Correction history",()=>Run(History));head.Children.Add(bar);head.Children.Add(status);DockPanel.SetDock(head,Dock.Top);root.Children.Add(head);
  WorkWindowFields.Columns(rows,("Session","Id"),("Employee","Employee"),("Source","EntrySource"),("State","State"),("Original in UTC","OriginalIn"),("Original out UTC","OriginalOut"),("Effective in UTC","ClockIn"),("Effective out UTC","ClockOut"),("Correction","CorrectionId"),("Manual evidence","ManualReason"),("Original actor","OriginalActorId"),("Manual record UTC","ManualRecordedUtc"));
  WorkWindowFields.Columns(history,("Revision","Id"),("Session","SessionId"),("Corrected in UTC","ClockIn"),("Corrected out UTC","ClockOut"),("Evidence","Reason"),("Actor","ActorId"),("Recorded UTC","RecordedUtc"));
  var grid=new Grid();grid.RowDefinitions.Add(new RowDefinition());grid.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)});Grid.SetRow(rows,0);Grid.SetRow(history,1);grid.Children.Add(rows);grid.Children.Add(history);root.Children.Add(grid);Content=root;
  rows.SelectionChanged+=(_,_)=>history.ItemsSource=null;start.SelectedDateChanged+=(_,_)=>Clear();end.SelectedDateChanged+=(_,_)=>Clear();Run(Refresh);
 }
 void Clear(){rows.ItemsSource=history.ItemsSource=null;status.Text="Refresh selected UTC range.";}
 void Run(Action a){try{a();}catch(UnauthorizedAccessException){Clear();status.Text="Access denied.";}catch(ArgumentException ex){status.Text=ex.Message;}catch(Exception){Clear();status.Text="Attendance operation unavailable. Refresh before retrying." ;}}
 void Refresh(){Clear();rows.ItemsSource=service.ReadAttendanceReview(WorkWindowFields.Day(start),WorkWindowFields.Day(end));status.Text="Review raw and effective UTC timestamps.";}
 AttendanceReviewRow Selected()=>rows.SelectedItem as AttendanceReviewRow??throw new ArgumentException("Select attendance session.");
 void History(){history.ItemsSource=service.ReadAttendanceCorrections(Selected().Id);}
 const string Format="yyyy-MM-dd HH:mm:ss";
 static DateTimeOffset Parse(string s)=>DateTimeOffset.TryParseExact(s.Trim(),Format,CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal|DateTimeStyles.AdjustToUniversal,out var value)?value:throw new ArgumentException("Use UTC yyyy-MM-dd HH:mm:ss.");
 void Missing()
 {
  var key=Guid.NewGuid();var w=new Window{Owner=this,Title="Record verified missing attendance",Width=580,Height=490,WindowStartupLocation=WindowStartupLocation.CenterOwner};var p=new StackPanel{Margin=new Thickness(18)};
  p.Children.Add(new TextBlock{Text="Manual reconstructed interval, not observed clock events. Select the employee and enter verified actual UTC timestamps. No work or payroll is created.",TextWrapping=TextWrapping.Wrap});
  var employee=new ComboBox{ItemsSource=service.ReadEmployees(),DisplayMemberPath="Name"};p.Children.Add(employee);
  p.Children.Add(new TextBlock{Text="Clock-in UTC (yyyy-MM-dd HH:mm:ss)"});var a=new TextBox();p.Children.Add(a);
  p.Children.Add(new TextBlock{Text="Clock-out UTC (yyyy-MM-dd HH:mm:ss)"});var b=new TextBox();p.Children.Add(b);
  p.Children.Add(new TextBlock{Text="Evidence / reason"});var note=new TextBox{MaxLength=1000,Height=85,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap};p.Children.Add(note);var confirm=new CheckBox{Content="I confirm these are verified actual timestamps."};p.Children.Add(confirm);var save=new Button{Content="Record manual session"};p.Children.Add(save);w.Content=new ScrollViewer{Content=p};
  save.Click+=(_,_)=>{try{if(employee.SelectedItem is not EmployeeRecord e)throw new ArgumentException("Select employee.");service.RecordMissingAttendance(key,e.Id,Parse(a.Text),Parse(b.Text),note.Text,confirm.IsChecked==true);w.DialogResult=true;}catch(ArgumentException ex){MessageBox.Show(w,ex.Message);}catch(UnauthorizedAccessException){MessageBox.Show(w,"Access denied.");}catch(Exception){MessageBox.Show(w,"Operation unavailable. Check history before recreating.");}};
  if(w.ShowDialog()==true)Refresh();
 }
 void VoidSelected()
 {
  var r=Selected();if(r.State!="Closed")throw new ArgumentException("Select closed nonvoid attendance.");
  if(MessageBox.Show(this,"Void only a verified erroneous or duplicate attendance record. Never remove valid actual time or earned wages. Original interval and revisions remain in history. Continue?","Confirm attendance void",MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes)return;
  var note=WorkWindowFields.Note(this,"Document factual error");if(note==null)return;service.VoidAttendance(r.Id,r.CorrectionId,note,true);Refresh();
 }
 void ViewVoid()
 {
  var e=service.ReadAttendanceVoid(Selected().Id)??throw new ArgumentException("No void recorded.");
  MessageBox.Show(this,$"Session #{e.SessionId}\nActor: {e.ActorId}\nRecorded UTC: {e.RecordedUtc}\nReason: {e.Reason}","Attendance void evidence");
 }
 void Correct()
 {
  var r=Selected();if(r.State=="Voided")throw new ArgumentException("Voided attendance cannot be reinstated.");var w=new Window{Owner=this,Title="Record attendance correction",Width=580,Height=470,WindowStartupLocation=WindowStartupLocation.CenterOwner};var p=new StackPanel{Margin=new Thickness(18)};
  p.Children.Add(new TextBlock{Text=$"Session #{r.Id} — {r.Employee}. Enter verified actual UTC timestamps. The correction is an additional immutable revision.",TextWrapping=TextWrapping.Wrap});
  p.Children.Add(new TextBlock{Text="Actual clock-in UTC (yyyy-MM-dd HH:mm:ss)"});var a=new TextBox{Text=r.ClockIn.UtcDateTime.ToString(Format,CultureInfo.InvariantCulture)};p.Children.Add(a);
  p.Children.Add(new TextBlock{Text="Actual clock-out UTC (yyyy-MM-dd HH:mm:ss)"});var b=new TextBox{Text=r.ClockOut?.UtcDateTime.ToString(Format,CultureInfo.InvariantCulture)??""};p.Children.Add(b);
  p.Children.Add(new TextBlock{Text="Evidence / reason"});var note=new TextBox{MaxLength=1000,Height=85,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap};p.Children.Add(note);var confirm=new CheckBox{Content="I confirm these are verified actual timestamps."};p.Children.Add(confirm);var save=new Button{Content="Record immutable correction"};p.Children.Add(save);w.Content=new ScrollViewer{Content=p};
  save.Click+=(_,_)=>{try{service.CorrectAttendance(r.Id,r.CorrectionId,Parse(a.Text),Parse(b.Text),note.Text,confirm.IsChecked==true);w.DialogResult=true;}catch(ArgumentException ex){MessageBox.Show(w,ex.Message);}catch(UnauthorizedAccessException){MessageBox.Show(w,"Access denied.");}catch(Exception){MessageBox.Show(w,"Operation unavailable. Close and refresh before retrying.");}};
  if(w.ShowDialog()==true)Refresh();
 }
}
