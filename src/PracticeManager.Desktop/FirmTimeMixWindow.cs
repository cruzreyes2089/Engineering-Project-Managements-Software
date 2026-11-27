using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using PracticeManager.Core;
using PracticeManager.Persistence;
namespace PracticeManager.Desktop;
internal sealed class FirmTimeMixWindow:Window
{
 readonly LocalPractice service;readonly DataGrid grid=WorkWindowFields.Grid();
 readonly DatePicker start=new(){SelectedDate=new DateTime(DateTime.Today.Year,DateTime.Today.Month,1)},end=new(){SelectedDate=DateTime.Today};
 readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap};
 public FirmTimeMixWindow(LocalPractice service)
 {
  this.service=service;Title="Restricted firm time mix";Width=1220;Height=620;WindowStartupLocation=WindowStartupLocation.CenterOwner;
  var root=new DockPanel{Margin=new Thickness(16)};var p=new StackPanel();p.Children.Add(new TextBlock{Text=Title,FontSize=24,FontWeight=FontWeights.SemiBold});
  p.Children.Add(new TextBlock{Text="Approved project time plus approved overhead. Billable share is approved billable minutes / all approved minutes, not attendance, capacity or revenue. Pending and rejected submissions are separate. Unclassified approved time prevents a definitive share. No salary or hourly rate values.",TextWrapping=TextWrapping.Wrap});
  var bar=new WrapPanel();bar.Children.Add(start);bar.Children.Add(end);WorkWindowFields.Button(bar,"Generate",Refresh);p.Children.Add(bar);p.Children.Add(status);DockPanel.SetDock(p,Dock.Top);root.Children.Add(p);
  WorkWindowFields.Columns(grid,("Employee no.","EmployeeNumber"),("Employee","Employee"),("Active","Active"),("Billable min","BillableMinutes"),("Project nonbillable min","ProjectNonbillableMinutes"),("Unclassified min","UnclassifiedMinutes"),("Overhead min","OverheadMinutes"),("Approved min","ApprovedMinutes"),("Pending project min","PendingProjectMinutes"),("Pending overhead min","PendingOverheadMinutes"),("Rejected project min","RejectedProjectMinutes"),("Rejected overhead min","RejectedOverheadMinutes"));
  grid.Columns.Add(new DataGridTextColumn{Header="Billable share",Binding=new Binding("ApprovedBillableShare"){StringFormat="P1",TargetNullValue="Unavailable"}});
  root.Children.Add(grid);Content=root;start.SelectedDateChanged+=(_,_)=>Invalidate();end.SelectedDateChanged+=(_,_)=>Invalidate();Refresh();
 }
 void Invalidate(){grid.ItemsSource=null;status.Text="Generate report for selected period.";}
 void Refresh()
 {
  grid.ItemsSource=null;status.Text="";
  try{var r=service.ReadFirmTimeMix(WorkWindowFields.Day(start),WorkWindowFields.Day(end));grid.ItemsSource=r.Rows;
   status.Text=$"Approved {r.ApprovedMinutes/60m:0.00} h | Billable {r.ApprovedBillableMinutes/60m:0.00} h | Project nonbillable {r.ApprovedProjectNonbillableMinutes/60m:0.00} h | Overhead {r.ApprovedOverheadMinutes/60m:0.00} h | Unclassified {r.ApprovedUnclassifiedMinutes/60m:0.00} h\nPending {r.PendingMinutes/60m:0.00} h | Rejected {r.RejectedMinutes/60m:0.00} h | Approved billable share {(r.ApprovedBillableShare.HasValue?r.ApprovedBillableShare.Value.ToString("P1"):"Unavailable")}";
  }catch(UnauthorizedAccessException){status.Text="Access denied.";}catch(ArgumentException e){status.Text=e.Message;}catch(Exception){status.Text="Time report unavailable.";}
 }
}
