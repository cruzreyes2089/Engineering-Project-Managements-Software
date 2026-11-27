using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using PracticeManager.Core;
using PracticeManager.Persistence;
namespace PracticeManager.Desktop;
internal sealed class StaffWorkloadWindow:Window
{
 readonly LocalPractice service;
 readonly DatePicker start=new(),end=new();
 readonly TextBox capacity=new(){Width=90,MaxLength=4};
 readonly DataGrid grid=new(){AutoGenerateColumns=false,IsReadOnly=true,CanUserAddRows=false};
 readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap};
 public StaffWorkloadWindow(LocalPractice service,DateOnly from,DateOnly through)
 {
  this.service=service;start.SelectedDate=from.ToDateTime(TimeOnly.MinValue);end.SelectedDate=through.ToDateTime(TimeOnly.MinValue);
  Title="Staff workload — planning scenario";Width=1160;Height=650;WindowStartupLocation=WindowStartupLocation.CenterOwner;
  var root=new DockPanel{Margin=new Thickness(18)};var head=new StackPanel();
  head.Children.Add(new TextBlock{Text=Title,FontSize=24,FontWeight=FontWeights.SemiBold});
  head.Children.Add(new TextBlock{Text="Original open-task estimates spread evenly across scheduled weekdays, not remaining effort or actual time. Held work is separate. Optional common capacity in minutes per weekday is illustrative; excludes holidays, leave and overhead. Weekend-only estimates are unallocated full task estimates. No authorization inferred.",TextWrapping=TextWrapping.Wrap});
  var bar=new WrapPanel();bar.Children.Add(new TextBlock{Text="From"});bar.Children.Add(start);bar.Children.Add(new TextBlock{Text="Through"});bar.Children.Add(end);
  bar.Children.Add(new TextBlock{Text="Scenario minutes/weekday (optional)"});bar.Children.Add(capacity);
  var refresh=new Button{Content="Refresh workload"};refresh.Click+=(_,_)=>Refresh();bar.Children.Add(refresh);head.Children.Add(bar);head.Children.Add(status);
  DockPanel.SetDock(head,Dock.Top);root.Children.Add(head);
  foreach(var (label,prop) in new[]{("Employee no.","EmployeeNumber"),("Employee","EmployeeName"),("Active","Active"),("Eligible tasks","EligibleTasks"),("Held tasks","HeldTasks"),("Eligible hours","EligibleHours"),("Held hours","HeldHours"),("Weekend-only full hours","WeekendOnlyEstimateHours"),("Scenario capacity h","ScenarioCapacityHours"),("Over-capacity weekdays","OverCapacityWeekdays")})
  {
   var binding=new Binding(prop);
   if(prop.EndsWith("Hours",StringComparison.Ordinal))binding.StringFormat="0.00";
   grid.Columns.Add(new DataGridTextColumn{Header=label,Binding=binding,Width=DataGridLength.Auto});
  }
  root.Children.Add(grid);Content=root;
  start.SelectedDateChanged+=(_,_)=>Clear();end.SelectedDateChanged+=(_,_)=>Clear();capacity.TextChanged+=(_,_)=>Clear();
  Loaded+=(_,_)=>Refresh();Closed+=(_,_)=>{grid.ItemsSource=null;status.Text="";};
 }
 void Clear(){grid.ItemsSource=null;status.Text="Refresh for the selected scenario.";}
 void Refresh()
 {
  Clear();
  try
  {
   if(start.SelectedDate is not DateTime a||end.SelectedDate is not DateTime b)throw new ArgumentException("Select both dates.");
   int? minutes=null;
   if(!string.IsNullOrWhiteSpace(capacity.Text))
   {
    if(!int.TryParse(capacity.Text,NumberStyles.None,CultureInfo.InvariantCulture,out var n)||n is <1 or >1440)
     throw new ArgumentException("Enter 1–1440 minutes per weekday or leave capacity blank.");
    minutes=n;
   }
   var result=service.ReadStaffWorkload(DateOnly.FromDateTime(a),DateOnly.FromDateTime(b),minutes);
   grid.ItemsSource=result.Rows;
   status.Text=$"{result.Weekdays} weekdays; {result.Rows.Count} employees. Eligible estimate: {result.Rows.Sum(r=>r.EligibleMinutes)/60m:0.00} h; held estimate: {result.Rows.Sum(r=>r.HeldMinutes)/60m:0.00} h. "+(minutes.HasValue?"Capacity compares eligible allocations only; blank for inactive staff.":"No capacity assumption applied.");
  }
  catch(UnauthorizedAccessException){status.Text="Access denied.";}
  catch(ArgumentException ex){status.Text=ex.Message;}
  catch(Exception){status.Text="Workload unavailable.";}
 }
}
