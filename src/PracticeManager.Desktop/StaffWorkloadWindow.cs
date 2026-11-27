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
 readonly TextBox weeks=new(){AcceptsReturn=true,MaxLength=100000,Height=62,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
 readonly TextBox closures=new(){AcceptsReturn=true,MaxLength=100000,Height=48,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
 readonly TextBox exceptions=new(){AcceptsReturn=true,MaxLength=100000,Height=62,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
 public StaffWorkloadWindow(LocalPractice service,DateOnly from,DateOnly through)
 {
  this.service=service;start.SelectedDate=from.ToDateTime(TimeOnly.MinValue);end.SelectedDate=through.ToDateTime(TimeOnly.MinValue);
  Title="Staff workload — planning scenario";Width=1160;Height=650;WindowStartupLocation=WindowStartupLocation.CenterOwner;
  var root=new DockPanel{Margin=new Thickness(18)};var head=new StackPanel();
  head.Children.Add(new TextBlock{Text=Title,FontSize=24,FontWeight=FontWeights.SemiBold});
  head.Children.Add(new TextBlock{Text="Original open-task estimates spread evenly across scheduled weekdays, not remaining effort or actual time. Held work is separate. Enter optional common capacity or individual calendars below. Calendar exceptions reduce capacity, not task estimates. Weekend-only estimates remain unallocated full task estimates. No authorization inferred.",TextWrapping=TextWrapping.Wrap});
  var bar=new WrapPanel();bar.Children.Add(new TextBlock{Text="From"});bar.Children.Add(start);bar.Children.Add(new TextBlock{Text="Through"});bar.Children.Add(end);
  bar.Children.Add(new TextBlock{Text="Scenario minutes/weekday (optional)"});bar.Children.Add(capacity);
  var refresh=new Button{Content="Refresh workload"};refresh.Click+=(_,_)=>Refresh();bar.Children.Add(refresh);head.Children.Add(bar);
  var calendarPanel=new StackPanel{Margin=new Thickness(0,4,0,4)};
  calendarPanel.Children.Add(new TextBlock{Text="Temporary scenario only; no employee policy or leave record is saved. Use exact employee numbers from the report. Individual weeks override common capacity. Firm closures take precedence; exceptions must be inside the review range.",TextWrapping=TextWrapping.Wrap});
  calendarPanel.Children.Add(new TextBlock{Text="Individual week: employee number,Mon,Tue,Wed,Thu,Fri,Sat,Sun (minutes; one row each). Example: EMP-000001,480,480,240,480,0,0,0",TextWrapping=TextWrapping.Wrap});
  calendarPanel.Children.Add(weeks);
  calendarPanel.Children.Add(new TextBlock{Text="Firm closures: yyyy-MM-dd (one date per line)"});calendarPanel.Children.Add(closures);
  calendarPanel.Children.Add(new TextBlock{Text="Individual date override: employee number,yyyy-MM-dd,minutes (0 = unavailable; one row each)"});calendarPanel.Children.Add(exceptions);
  head.Children.Add(new Expander{Header="Individual calendars and date exceptions (optional)",Content=calendarPanel,IsExpanded=false});
  head.Children.Add(status);
  DockPanel.SetDock(head,Dock.Top);root.Children.Add(head);
  foreach(var (label,prop) in new[]{("Employee no.","EmployeeNumber"),("Employee","EmployeeName"),("Active","Active"),("Eligible tasks","EligibleTasks"),("Held tasks","HeldTasks"),("Eligible hours","EligibleHours"),("Held hours","HeldHours"),("Weekend-only full hours","WeekendOnlyEstimateHours"),("Capacity basis","CapacitySource"),("Scenario capacity h","ScenarioCapacityHours"),("Over-capacity weekdays","OverCapacityWeekdays"),("Hours on unavailable days","UnavailableAllocationHours"),("Hours above capacity","OverCapacityHours")})
  {
   var binding=new Binding(prop);
   if(prop.EndsWith("Hours",StringComparison.Ordinal))binding.StringFormat="0.00";
   grid.Columns.Add(new DataGridTextColumn{Header=label,Binding=binding,Width=DataGridLength.Auto});
  }
  root.Children.Add(grid);Content=root;
  start.SelectedDateChanged+=(_,_)=>Clear();end.SelectedDateChanged+=(_,_)=>Clear();capacity.TextChanged+=(_,_)=>Clear();weeks.TextChanged+=(_,_)=>Clear();closures.TextChanged+=(_,_)=>Clear();exceptions.TextChanged+=(_,_)=>Clear();
  Loaded+=(_,_)=>Refresh();Closed+=(_,_)=>{grid.ItemsSource=null;weeks.Clear();closures.Clear();exceptions.Clear();capacity.Clear();status.Text="";};
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
   var calendar=StaffCapacityCalendar.Parse(weeks.Text,closures.Text,exceptions.Text);
   var result=service.ReadStaffWorkload(DateOnly.FromDateTime(a),DateOnly.FromDateTime(b),minutes,calendar);
   grid.ItemsSource=result.Rows;
   status.Text=$"{result.Weekdays} weekdays; {result.Rows.Count} employees. Eligible estimate: {result.Rows.Sum(r=>r.EligibleMinutes)/60m:0.00} h; held estimate: {result.Rows.Sum(r=>r.HeldMinutes)/60m:0.00} h. "+"Capacity compares eligible allocations only; unspecified or inactive employees have blank capacity. Weekend capacity does not redistribute weekday demand.";
  }
  catch(UnauthorizedAccessException){status.Text="Access denied.";}
  catch(ArgumentException ex){status.Text=ex.Message;}
  catch(Exception){status.Text="Workload unavailable.";}
 }
}
