using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using PracticeManager.Core;
using PracticeManager.Persistence;
namespace PracticeManager.Desktop;
internal sealed class ProjectCostScenarioWindow:Window
{
 readonly LocalPractice service;readonly long project;
 readonly TextBox revenue=new(){MaxLength=24},rate=new(){MaxLength=24},future=new(){MaxLength=24};
 readonly TextBlock result=new(){TextWrapping=TextWrapping.Wrap};
 public ProjectCostScenarioWindow(LocalPractice service,long project)
 {
  this.service=service;this.project=project;Title="Restricted project direct-cost scenario";Width=820;Height=660;WindowStartupLocation=WindowStartupLocation.CenterOwner;
  var body=new StackPanel{Margin=new Thickness(18)};
  void Text(string value)=>body.Children.Add(new TextBlock{Text=value,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,4,0,4)});
  Text("Read-only USD planning scenario, not accounting profit. Requires current saved effort forecast. Pending time and expenses assumed accepted. Effective-dated recorded cost rates estimate submitted labor; future unsubmitted labor uses your explicit blended cost rate.");
  Text("Expected total project revenue excluding tax (not invoice draft value)");body.Children.Add(revenue);
  Text("Remaining unsubmitted labor cost per hour (include burden if intended)");body.Children.Add(rate);
  Text("Additional future direct costs not already submitted (consultants, expenses, etc.)");body.Children.Add(future);
  Text("Enter all three assumptions explicitly; zero is permitted if assessed. No firm overhead, taxes, or posted revenue is inferred.");
  var button=new Button{Content="Calculate current scenario"};button.Click+=(_,_)=>Calculate();body.Children.Add(button);body.Children.Add(result);
  Content=new ScrollViewer{Content=body};revenue.TextChanged+=(_,_)=>result.Text="";rate.TextChanged+=(_,_)=>result.Text="";future.TextChanged+=(_,_)=>result.Text="";
  Closed+=(_,_)=>{revenue.Clear();rate.Clear();future.Clear();result.Text="";};
 }
 static decimal Amount(TextBox field)
 {
  if(!decimal.TryParse(field.Text,NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out var value))throw new ArgumentException("Enter explicit nonnegative USD amounts with a decimal point.");
  return value;
 }
 static string Money(decimal? value)=>value.HasValue?value.Value.ToString("C2",CultureInfo.GetCultureInfo("en-US")):"Incomplete";
 void Calculate()
 {
  result.Text="";
  try
  {
   var r=service.ReadProjectCostScenario(project,new(Amount(revenue),Amount(rate),Amount(future)));var e=r.Evidence;
   result.Text=$"{r.ProjectNumber} • Forecast {r.ForecastMinutes/60m:0.00} h\nApproved time {e.ApprovedMinutes/60m:0.00} h; known labor cost {Money(e.ApprovedKnownLabor)}; missing rates {e.ApprovedMissingRates}\nPending time {e.PendingMinutes/60m:0.00} h; known labor cost {Money(e.PendingKnownLabor)}; missing rates {e.PendingMissingRates}\nRemaining labor {r.RemainingMinutes/60m:0.00} h; estimated cost {Money(r.RemainingLabor)}\nApproved direct expenses {Money(e.ApprovedExpenses)}\nPending direct expenses {Money(e.PendingExpenses)}\nFuture unsubmitted direct cost {Money(r.Assumptions.FutureUnsubmittedDirectCost)}\n\nEstimated direct cost at completion: {Money(r.EstimatedDirectCost)}\nProjected direct contribution: {Money(r.ProjectedDirectContribution)}\nDirect contribution margin: {(r.ProjectedDirectContributionPercent is decimal pct?$"{pct:0.00}%":"Unavailable")}\n\nScenario only — not net profit, cash flow, posted revenue or actual accounting. Recalculate after any change.";
  }
  catch(UnauthorizedAccessException){result.Text="Access denied.";}
  catch(ArgumentException ex){result.Text=ex.Message;}
  catch(Exception){result.Text="Cost scenario unavailable.";}
 }
}
