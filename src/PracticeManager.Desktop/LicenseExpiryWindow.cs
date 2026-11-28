using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using PracticeManager.Core;
using PracticeManager.Persistence;
namespace PracticeManager.Desktop;
internal sealed class LicenseExpiryWindow:Window
{
 readonly LocalPractice service;
 readonly DatePicker asOf=new(){SelectedDate=DateTime.Today};
 readonly ComboBox horizon=new(){ItemsSource=new[]{15,30,60,90},SelectedItem=30};
 readonly DataGrid grid=new(){AutoGenerateColumns=false,IsReadOnly=true,CanUserAddRows=false};
 readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap};
 public LicenseExpiryWindow(LocalPractice service)
 {
  this.service=service;Title="Employee license expiration review";Width=950;Height=600;WindowStartupLocation=WindowStartupLocation.CenterOwner;
  var root=new DockPanel{Margin=new Thickness(18)};var head=new StackPanel();
  head.Children.Add(new TextBlock{Text=Title,FontSize=24,FontWeight=FontWeights.SemiBold});
  head.Children.Add(new TextBlock{Text="Active employees, current recorded dates only. Expired and upcoming licenses are shown. No regulatory verification or automatic notice delivery.",TextWrapping=TextWrapping.Wrap});
  var bar=new WrapPanel();bar.Children.Add(new TextBlock{Text="As of:",VerticalAlignment=VerticalAlignment.Center});bar.Children.Add(asOf);
  bar.Children.Add(new TextBlock{Text="Upcoming days:",VerticalAlignment=VerticalAlignment.Center});bar.Children.Add(horizon);
  var refresh=new Button{Content="Refresh review"};refresh.Click+=(_,_)=>Refresh();bar.Children.Add(refresh);
  head.Children.Add(bar);head.Children.Add(status);DockPanel.SetDock(head,Dock.Top);root.Children.Add(head);
  foreach(var (label,property) in new[]{("Employee no.","EmployeeNumber"),("Employee","EmployeeName"),("Jurisdiction","Jurisdiction"),("License type","LicenseType"),("Expires","Expires"),("Days remaining","DaysRemaining"),("Status","Status")})
   grid.Columns.Add(new DataGridTextColumn{Header=label,Binding=new Binding(property),Width=DataGridLength.Auto});
  root.Children.Add(grid);Content=root;
  asOf.SelectedDateChanged+=(_,_)=>Clear();horizon.SelectionChanged+=(_,_)=>Clear();
  Loaded+=(_,_)=>Refresh();Closed+=(_,_)=>{grid.ItemsSource=null;status.Text="";};
 }
 void Clear(){grid.ItemsSource=null;status.Text="Refresh for selected dates.";}
 void Refresh()
 {
  Clear();
  try
  {
   if(asOf.SelectedDate is not DateTime date||horizon.SelectedItem is not int days)throw new ArgumentException("Select date and horizon.");
   var result=service.ReadEmployeeLicenseExpiry(DateOnly.FromDateTime(date),days);
   grid.ItemsSource=result.Items;
   status.Text=$"As of {result.AsOf:yyyy-MM-dd}: {result.Expired} expired, {result.DueToday} due today, {result.Upcoming} upcoming within {days} days. Counts are licenses, not employees.";
  }
  catch(UnauthorizedAccessException){status.Text="Access denied.";}
  catch(ArgumentException e){status.Text=e.Message;}
  catch(Exception){status.Text="License expiration review unavailable.";}
 }
}
