using System.Windows;
using System.Windows.Controls;
using PracticeManager.Core;
using PracticeManager.Persistence;
namespace PracticeManager.Desktop;
internal sealed class ProjectReportsWindow:Window
{
 readonly LocalPractice service;readonly ComboBox projects=new(){DisplayMemberPath="Number"};
 readonly DatePicker start=new(){SelectedDate=DateTime.Today.AddDays(-30)},end=new(){SelectedDate=DateTime.Today};
 readonly TextBlock ops=new(){TextWrapping=TextWrapping.Wrap},finance=new(){TextWrapping=TextWrapping.Wrap},closeout=new(){TextWrapping=TextWrapping.Wrap},status=new(){TextWrapping=TextWrapping.Wrap};
 public ProjectReportsWindow(LocalPractice service)
 {
  this.service=service;Title="Project reports — operational preview";Width=830;Height=650;WindowStartupLocation=WindowStartupLocation.CenterOwner;
  var root=new DockPanel{Margin=new Thickness(18)};var head=new StackPanel();head.Children.Add(new TextBlock{Text=Title,FontSize=24,FontWeight=FontWeights.SemiBold});head.Children.Add(new TextBlock{Text="Operational records, not accounting statements. Manager remains authoritative for posted revenue, cash, receivables and profit.",TextWrapping=TextWrapping.Wrap});projects.ItemsSource=service.SearchPendingProjects();head.Children.Add(projects);var filters=new WrapPanel();filters.Children.Add(start);filters.Children.Add(end);WorkWindowFields.Button(filters,"Generate",Refresh);if(service.CanReadFirmTimeMix())WorkWindowFields.Button(filters,"Firm time mix",()=>new FirmTimeMixWindow(service){Owner=this}.ShowDialog());head.Children.Add(filters);head.Children.Add(status);DockPanel.SetDock(head,Dock.Top);root.Children.Add(head);var tabs=new TabControl();
  if(service.Can("Reports.Operations")){
   tabs.Items.Add(new TabItem{Header="Operations",Content=new ScrollViewer{Content=ops}});
   var closeoutTab=new TabItem{Header="Closeout review (all dates)",Content=new ScrollViewer{Content=closeout}};tabs.Items.Add(closeoutTab);
   WorkWindowFields.Button(filters,"Review closeout (all dates)",()=>{tabs.SelectedItem=closeoutTab;RefreshCloseout();});
  }
  if(service.Can("Reports.Financial"))tabs.Items.Add(new TabItem{Header="Restricted financial preview",Content=new ScrollViewer{Content=finance}});
  root.Children.Add(tabs);Content=root;projects.SelectionChanged+=(_,_)=>{ops.Text=finance.Text=closeout.Text="";status.Text="Generate report for selected project.";};
 }
 void RefreshCloseout()
 {
  closeout.Text="";status.Text="";
  try{
   if(projects.SelectedItem is not PendingProject p)throw new ArgumentException("Select project.");
   var r=service.ReadProjectCloseoutReview(p.Id);
   closeout.Text=$"{r.ProjectNumber} • All dates (ignores report date filters)\n\nProject state: {r.ProjectState}\nSetup state: {r.SetupState}\nConflict screening: {r.ConflictStatus}\n\nPhases: {r.Phases}\nTasks not completed: {r.OpenTasks}\nOpen QA items: {r.OpenQualityItems}\nScope proposals awaiting decision: {r.PendingScopeDecisions}\nTime submissions awaiting review: {r.PendingTimeSubmissions}\nExpense submissions awaiting review: {r.PendingExpenseSubmissions}\n\nRead-only operational snapshot. Zero outstanding counts do not authorize closure, final delivery or billing. Financial and contractual closeout require separate review.";
   status.Text="Operational closeout snapshot generated.";
  }catch(UnauthorizedAccessException){ops.Text=finance.Text=closeout.Text="";status.Text="Access denied.";}catch(ArgumentException e){status.Text=e.Message;}catch(Exception){status.Text="Closeout review unavailable.";}
 }
 void Refresh()
 {
  ops.Text=finance.Text=closeout.Text="";status.Text="";
  try{
   if(projects.SelectedItem is not PendingProject p)throw new ArgumentException("Select project.");
   var a=WorkWindowFields.Day(start);var b=WorkWindowFields.Day(end);
   if(service.Can("Reports.Operations")){var r=service.ReadProjectOperations(p.Id,a,b);ops.Text=$"{r.ProjectNumber} • {a:yyyy-MM-dd} through {b:yyyy-MM-dd}\n\nPhases overlapping range: {r.Phases}\nPlanned tasks: {r.PlannedTasks}\nIn progress: {r.StartedTasks}\nCompleted: {r.CompletedTasks}\nTotal estimate of overlapping tasks: {r.EstimatedMinutes/60m:0.00} h (not prorated)\n\nSubmitted: {r.SubmittedMinutes/60m:0.00} h\nApproved: {r.ApprovedMinutes/60m:0.00} h\nPending review: {r.PendingMinutes/60m:0.00} h\nRejected: {r.RejectedMinutes/60m:0.00} h\nExpense submissions: {r.ExpenseSubmissions}\nCurrent open QA items (all dates): {r.OpenQualityItems}";}
   if(service.Can("Reports.Financial")){var r=service.ReadProjectFinancialReport(p.Id,a,b);finance.Text=$"{r.ProjectNumber} • {a:yyyy-MM-dd} through {b:yyyy-MM-dd}\n\nApproved time: {r.ApprovedMinutes/60m:0.00} h\nBillable: {r.BillableMinutes/60m:0.00} h\nNonbillable: {r.NonbillableMinutes/60m:0.00} h\nUnclassified: {r.UnclassifiedMinutes/60m:0.00} h\nEstimated labor cost: {(r.EstimatedLaborCost.HasValue?r.EstimatedLaborCost.Value.ToString("C2"):"Incomplete")}\nMissing cost rates: {r.MissingCostRates}\nApproved expense cost: {r.ApprovedExpenseCost:C2}\nTotal operational cost: {(r.TotalOperationalCost.HasValue?r.TotalOperationalCost.Value.ToString("C2"):"Incomplete")}\n\nUnapproved internal draft value: {r.DraftValue:C2}\nApproved internal draft value: {r.ApprovedInternalDraftValue:C2}\nCancelled draft value: {r.CancelledDraftValue:C2}\n\nNo posted revenue, profit or accounts receivable inferred.";}
  }catch(UnauthorizedAccessException){ops.Text=finance.Text="";status.Text="Access denied.";}catch(ArgumentException e){status.Text=e.Message;}catch(Exception){ops.Text=finance.Text="";status.Text="Report unavailable.";}
 }
}
