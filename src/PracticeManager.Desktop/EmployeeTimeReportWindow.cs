using System.IO;
using System.Text;
using System.Security.Cryptography;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using PracticeManager.Core;
using PracticeManager.Persistence;
namespace PracticeManager.Desktop;
internal sealed class EmployeeTimeReportWindow:Window
{
 sealed record Choice(long? Id,string Label);
 readonly LocalPractice service;
 readonly ComboBox employee=new(){DisplayMemberPath="Label",MinWidth=220},project=new(){DisplayMemberPath="Label",MinWidth=180},state=new(){ItemsSource=new[]{"All","Approved","Submitted","Rejected","Invalidated"},SelectedIndex=0};
 readonly DatePicker start=new(){SelectedDate=DateTime.Today.AddDays(-30)},end=new(){SelectedDate=DateTime.Today};
 readonly DataGrid grid=WorkWindowFields.Grid();
 readonly TextBlock summary=new(){TextWrapping=TextWrapping.Wrap};
 public EmployeeTimeReportWindow(LocalPractice service)
 {
  this.service=service;Title="Employee time — operational report";Width=1150;Height=720;WindowStartupLocation=WindowStartupLocation.CenterOwner;
  var root=new DockPanel{Margin=new Thickness(16)};var head=new StackPanel();
  head.Children.Add(new TextBlock{Text="Submitted time facts, not payroll or attendance. Combine employee, project, work-date range and review status. All projects includes overhead; selecting a project excludes overhead. Historical rejected and invalidated time is separate from approved time. Export CSV saves fresh confidential results to a new file; use protected local storage.",TextWrapping=TextWrapping.Wrap});
  var filters=new WrapPanel();
  void Field(string label,Control control){var p=new StackPanel{Margin=new Thickness(4)};p.Children.Add(new TextBlock{Text=label});p.Children.Add(control);filters.Children.Add(p);}
  Field("Employee",employee);Field("Project",project);Field("From (inclusive)",start);Field("To (inclusive)",end);Field("Review status",state);
  WorkWindowFields.Button(filters,"Generate",Refresh);WorkWindowFields.Button(filters,"Export CSV",Export);WorkWindowFields.Button(filters,"Clear filters",Reset);
  head.Children.Add(filters);head.Children.Add(summary);DockPanel.SetDock(head,Dock.Top);root.Children.Add(head);
  WorkWindowFields.Columns(grid,("Employee no.","EmployeeNumber"),("Employee","Employee"),("Work date","WorkDate"),("Source","Source"),("Project","ProjectNumber"),("Task / category","TaskOrCategory"),("Hours","Hours"),("Minutes","Minutes"),("Status","Status"),("Record ID","SubmissionId"));
  root.Children.Add(grid);Content=root;
  employee.SelectionChanged+=(_,_)=>Clear();project.SelectionChanged+=(_,_)=>Clear();state.SelectionChanged+=(_,_)=>Clear();start.SelectedDateChanged+=(_,_)=>Clear();end.SelectedDateChanged+=(_,_)=>Clear();
  Closed+=(_,_)=>{grid.ItemsSource=null;employee.ItemsSource=null;project.ItemsSource=null;summary.Text="";};
  try{
   employee.ItemsSource=new[]{new Choice(null,"All employees")}.Concat(service.ReadEmployees().Select(e=>new Choice(e.Id,$"{e.Number} — {e.First} {e.Last}"+(e.Active?"":" (inactive)")))).ToArray();
   project.ItemsSource=new[]{new Choice(null,"All projects + overhead")}.Concat(service.SearchPendingProjects().Select(p=>new Choice(p.Id,$"{p.Number} — {p.State}"))).ToArray();
   Reset();
  }catch(UnauthorizedAccessException){employee.ItemsSource=null;project.ItemsSource=null;summary.Text="Access denied.";}
  catch(Exception){employee.ItemsSource=null;project.ItemsSource=null;summary.Text="Report filters unavailable.";}
 }
 void Clear(){grid.ItemsSource=null;summary.Text="Generate report for the selected filters.";}
 void Reset(){employee.SelectedIndex=0;project.SelectedIndex=0;state.SelectedIndex=0;start.SelectedDate=DateTime.Today.AddDays(-30);end.SelectedDate=DateTime.Today;Clear();}
 EmployeeTimeFilter CurrentFilter()
 {
  if(employee.SelectedItem is not Choice e||project.SelectedItem is not Choice p||state.SelectedItem is not string s)throw new ArgumentException("Select report filters.");
  return new(e.Id,p.Id,WorkWindowFields.Day(start),WorkWindowFields.Day(end),s);
 }
 void Export()
 {
  try{
   var filter=CurrentFilter();
   var picker=new SaveFileDialog{Title="Export fresh employee time CSV — protected local storage",FileName="CRC-employee-time.csv",DefaultExt=".csv",Filter="CSV file (*.csv)|*.csv",AddExtension=true,OverwritePrompt=true};
   if(picker.ShowDialog(this)!=true)return;
   if(CurrentFilter()!=filter)throw new ArgumentException("Report filters changed. Review and retry.");
   if(!string.Equals(Path.GetExtension(picker.FileName),".csv",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Keep the .csv extension.");
   _=LocalStoragePath.FilePath(picker.FileName,mustExist:false,mustBeNew:true,rejectSidecars:true);
   var bytes=Encoding.UTF8.GetBytes(service.PrepareEmployeeTimeReportCsv(filter));
   try{VerifiedDocumentFile.Save(picker.FileName,".csv",bytes,Convert.ToHexString(SHA256.HashData(bytes)));}
   finally{CryptographicOperations.ZeroMemory(bytes);}
   Clear();summary.Text="Fresh confidential CSV saved. Generate to display current report.";
  }catch(UnauthorizedAccessException){Clear();employee.ItemsSource=null;project.ItemsSource=null;summary.Text="Access denied.";}
  catch(ArgumentException ex){summary.Text=ex.Message;}
  catch(Exception){summary.Text="Export unavailable; no successful file publication assumed.";}
 }
 void Refresh()
 {
  Clear();
  try{
   var r=service.ReadEmployeeTimeReport(CurrentFilter());
   grid.ItemsSource=r.Rows;
   summary.Text=$"{r.Filter.Start:yyyy-MM-dd} – {r.Filter.End:yyyy-MM-dd} | {r.Rows.Count} records | Approved {r.ApprovedMinutes/60m:0.00} h (project {r.ApprovedProjectMinutes/60m:0.00}; overhead {r.ApprovedOverheadMinutes/60m:0.00}) | Pending {r.PendingMinutes/60m:0.00} h | Rejected {r.RejectedMinutes/60m:0.00} h | Invalidated {r.InvalidatedMinutes/60m:0.00} h";
  }catch(UnauthorizedAccessException){grid.ItemsSource=null;employee.ItemsSource=null;project.ItemsSource=null;summary.Text="Access denied.";}
  catch(ArgumentException ex){summary.Text=ex.Message;}
  catch(Exception){summary.Text="Time report unavailable.";}
 }
}
