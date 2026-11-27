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
 readonly DataGrid grid=new(){AutoGenerateColumns=false,IsReadOnly=true,CanUserAddRows=false,SelectionMode=DataGridSelectionMode.Single};
 readonly DataGrid daily=new(){AutoGenerateColumns=false,IsReadOnly=true,CanUserAddRows=false};
 readonly CheckBox issuesOnly=new(){Content="Only days over capacity or with demand on unavailable days",IsChecked=false};
 readonly TextBlock detailHeading=new(){TextWrapping=TextWrapping.Wrap};
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
  var body=new Grid();
  body.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)});
  body.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
  body.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)});
  Grid.SetRow(grid,0);body.Children.Add(grid);
  var detailBar=new StackPanel{Margin=new Thickness(0,8,0,4)};
  detailBar.Children.Add(detailHeading);detailBar.Children.Add(issuesOnly);
  Grid.SetRow(detailBar,1);body.Children.Add(detailBar);
  foreach(var (label,property) in new[]{("Date","Date"),("Eligible hours","EligibleHours"),("Held hours","HeldHours"),("Capacity hours","CapacityHours"),("Excess hours","ExcessHours"),("Capacity basis","CapacityBasis"),("Status","Status")})
  {
   var binding=new Binding(property);
   if(property.EndsWith("Hours",StringComparison.Ordinal))binding.StringFormat="0.00";
   if(property=="Date")binding.StringFormat="yyyy-MM-dd";
   daily.Columns.Add(new DataGridTextColumn{Header=label,Binding=binding,Width=DataGridLength.Auto});
  }
  Grid.SetRow(daily,2);body.Children.Add(daily);root.Children.Add(body);Content=root;
  grid.SelectionChanged+=(_,_)=>ShowDays();
  issuesOnly.Checked+=(_,_)=>ShowDays();issuesOnly.Unchecked+=(_,_)=>ShowDays();
  ShowDays();
  start.SelectedDateChanged+=(_,_)=>Clear();end.SelectedDateChanged+=(_,_)=>Clear();capacity.TextChanged+=(_,_)=>Clear();weeks.TextChanged+=(_,_)=>Clear();closures.TextChanged+=(_,_)=>Clear();exceptions.TextChanged+=(_,_)=>Clear();
  Loaded+=(_,_)=>Refresh();Closed+=(_,_)=>{grid.ItemsSource=null;daily.ItemsSource=null;detailHeading.Text="";weeks.Clear();closures.Clear();exceptions.Clear();capacity.Clear();status.Text="";};
 }
 void ShowDays()
 {
  daily.ItemsSource=null;
  if(grid.SelectedItem is not StaffWorkloadRow employee){detailHeading.Text="Select an employee to review daily allocations.";return;}
  var days=employee.Days;
  if(issuesOnly.IsChecked==true)days=days.Where(d=>d.ExcessMinutes is >0).ToArray();
  daily.ItemsSource=days;
  detailHeading.Text=$"{employee.EmployeeNumber} — {employee.EmployeeName}: {days.Count} displayed days. Unknown capacity is not zero. Held and weekend-only estimates are not included in daily excess.";
 }
 void Clear(){grid.ItemsSource=null;daily.ItemsSource=null;detailHeading.Text="Select an employee after refreshing.";status.Text="Refresh for the selected scenario.";}
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
