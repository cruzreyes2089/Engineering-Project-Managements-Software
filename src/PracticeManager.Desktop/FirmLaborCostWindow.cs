using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using PracticeManager.Persistence;
namespace PracticeManager.Desktop;
internal sealed class FirmLaborCostWindow:Window
{
 public FirmLaborCostWindow(LocalPractice service,DateOnly start,DateOnly end)
 {
  Title="Restricted estimated labor cost";Width=920;Height=570;WindowStartupLocation=WindowStartupLocation.CenterOwner;
  var root=new DockPanel{Margin=new Thickness(16)};var p=new StackPanel();p.Children.Add(new TextBlock{Text=Title,FontSize=24,FontWeight=FontWeights.SemiBold});
  p.Children.Add(new TextBlock{Text="Approved time only, valued at the recorded cost rate effective on each work date. Estimated cost is not payroll, compensation due, posted expense or profit. Incomplete rates are never silently treated as zero.",TextWrapping=TextWrapping.Wrap});
  var review=new Button{Content="Overhead allocation scenario"};review.Click+=(_,_)=>new FirmOverheadWindow(service,start,end){Owner=this}.ShowDialog();p.Children.Add(review);
  var status=new TextBlock{TextWrapping=TextWrapping.Wrap};p.Children.Add(status);DockPanel.SetDock(p,Dock.Top);root.Children.Add(p);
  var grid=WorkWindowFields.Grid();WorkWindowFields.Columns(grid,("Category","Category"),("Approved minutes","ApprovedMinutes"),("Missing-rate minutes","MissingRateMinutes"),("Missing-rate records","MissingRateRecords"));
  grid.Columns.Add(new DataGridTextColumn{Header="Known partial cost USD",Binding=new Binding("KnownCost"){StringFormat="0.00"}});
  grid.Columns.Add(new DataGridTextColumn{Header="Complete cost USD",Binding=new Binding("CompleteCost"){StringFormat="0.00",TargetNullValue="Incomplete"}});
  root.Children.Add(grid);Content=root;
  try{var r=service.ReadFirmLaborCost(start,end);grid.ItemsSource=r.Rows;status.Text=$"{start:yyyy-MM-dd} through {end:yyyy-MM-dd}\nApproved {r.ApprovedMinutes/60m:0.00} h | Missing-rate {r.MissingRateMinutes/60m:0.00} h | Known partial cost USD {r.KnownCost:0.00} | Complete estimated cost {(r.CompleteCost.HasValue?r.CompleteCost.Value.ToString("0.00"):"Incomplete")}";}
  catch(UnauthorizedAccessException){status.Text="Access denied.";}catch(ArgumentException e){status.Text=e.Message;}catch(Exception){status.Text="Cost report unavailable.";}
 }
}
