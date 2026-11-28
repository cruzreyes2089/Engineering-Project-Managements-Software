using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using PracticeManager.Core;
using PracticeManager.Persistence;
namespace PracticeManager.Desktop;
internal sealed class FirmOverheadWindow:Window
{
 readonly LocalPractice service;readonly DateOnly start,end;
 readonly TextBox burden=new(){MaxLength=24},other=new(){MaxLength=24};
 readonly TextBlock result=new(){TextWrapping=TextWrapping.Wrap};
 public FirmOverheadWindow(LocalPractice service,DateOnly start,DateOnly end)
 {
  this.service=service;this.start=start;this.end=end;Title="Restricted firm overhead allocation review";Width=850;Height=650;WindowStartupLocation=WindowStartupLocation.CenterOwner;
  var p=new StackPanel{Margin=new Thickness(18)};
  void Text(string value)=>p.Children.Add(new TextBlock{Text=value,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,5,0,5)});
  Text($"{start:yyyy-MM-dd} through {end:yyyy-MM-dd}. Approved recorded project and overhead time only, valued at effective work-date cost rates.");
  Text("Additional labor burden (%) not already in the recorded cost rates; applied to project and overhead labor.");p.Children.Add(burden);
  Text("Other indirect costs (USD) for this exact period, excluding all labor or direct project costs already represented.");p.Children.Add(other);
  Text("Enter both assumptions, including explicit zero if reviewed. Obtain period costs separately from accounting; do not include direct project expenses or payroll again. Allocation denominator is all approved project hours, including nonbillable and unclassified.");
  var btn=new Button{Content="Calculate from current approved records"};btn.Click+=(_,_)=>Calculate();p.Children.Add(btn);p.Children.Add(result);
  Content=new ScrollViewer{Content=p};burden.TextChanged+=(_,_)=>result.Text="";other.TextChanged+=(_,_)=>result.Text="";
  Closed+=(_,_)=>{burden.Clear();other.Clear();result.Text="";};
 }
 static decimal Amount(TextBox field)
 {
  if(!decimal.TryParse(field.Text,NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out var value))throw new ArgumentException("Enter both explicit nonnegative decimal amounts.");
  return value;
 }
 static string Money(decimal? value)=>value.HasValue?value.Value.ToString("C2",CultureInfo.GetCultureInfo("en-US")):"Unavailable";
 void Calculate()
 {
  result.Text="";
  try
  {
   var source=service.ReadFirmLaborCost(start,end);var r=OverheadPlanning.Firm(source,new(Amount(burden),Amount(other)));
   result.Text=$"Approved project hours: {r.ProjectMinutes/60m:0.00}\nApproved overhead hours: {r.OverheadMinutes/60m:0.00}\nApproved billable hours: {r.BillableMinutes/60m:0.00}\nUnclassified project hours: {r.UnclassifiedMinutes/60m:0.00}\nMissing rate records: {r.MissingRateRecords}\nKnown partial project labor: {Money(r.KnownProjectLabor)}\nKnown partial overhead labor: {Money(r.KnownOverheadLabor)}\nBurdened project labor: {Money(r.BurdenedProjectLabor)}\nBurdened overhead labor: {Money(r.BurdenedOverheadLabor)}\nModeled indirect pool: {Money(r.IndirectPool)}\nIndirect cost per project hour: {(r.AllocationPerProjectHour is decimal rate?rate.ToString("0.000000",CultureInfo.InvariantCulture):"Unavailable")} USD/h\nTotal modeled labor + indirect costs: {Money(r.TotalModeledCost)}\nModeled recovery per approved billable hour: {Money(r.RecoveryPerBillableHour)}\n\nThis is a period cost scenario, not actual accounting profit, a billing-rate recommendation, or a forward staffing forecast. Direct project expenses are excluded; zero billable or unclassified time prevents a billable recovery result. Copying the displayed rounded rate is an approximation, not an automatic allocation posting.";
  }
  catch(UnauthorizedAccessException){result.Text="Access denied.";}
  catch(ArgumentException ex){result.Text=ex.Message;}
  catch(Exception){result.Text="Overhead scenario unavailable.";}
 }
}
