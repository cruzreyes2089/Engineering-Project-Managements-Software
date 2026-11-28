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
  this.service=service;Title="Reports — operational preview";Width=830;Height=650;WindowStartupLocation=WindowStartupLocation.CenterOwner;
  var root=new DockPanel{Margin=new Thickness(18)};var head=new StackPanel();
  head.Children.Add(new TextBlock{Text="Reports",FontSize=24,FontWeight=FontWeights.SemiBold});
  head.Children.Add(new TextBlock{Text="Project performance and internal billing records. Posted revenue, cash, receivables and profit remain in Manager.",TextWrapping=TextWrapping.Wrap});
  var firm=new WrapPanel();if(service.CanReadFirmTimeMix())WorkWindowFields.Button(firm,"Firm time summary",()=>Run(()=>new FirmTimeMixWindow(service){Owner=this}.ShowDialog()));
  head.Children.Add(firm);head.Children.Add(new TextBlock{Text="Project"});
  var list=service.SearchPendingProjects();
  projects.DisplayMemberPath="";var template=new DataTemplate(typeof(PendingProject));var element=new FrameworkElementFactory(typeof(TextBlock));
  element.SetBinding(TextBlock.TextProperty,new System.Windows.Data.MultiBinding{StringFormat="{0} — {1} ({2})",Bindings={new System.Windows.Data.Binding("Number"),new System.Windows.Data.Binding("Title"),new System.Windows.Data.Binding("ClientName")}});
  template.VisualTree=element;projects.ItemTemplate=template;projects.ItemsSource=list;head.Children.Add(projects);
  var filters=new WrapPanel();filters.Children.Add(new TextBlock{Text="From",Margin=new Thickness(4,8,4,0)});filters.Children.Add(start);filters.Children.Add(new TextBlock{Text="Through",Margin=new Thickness(4,8,4,0)});filters.Children.Add(end);head.Children.Add(filters);
  var generate=new Button{Content="Generate report",Margin=new Thickness(2),IsEnabled=false};filters.Children.Add(generate);
  var lifecycle=new Button{Content="Close / reopen project",Margin=new Thickness(2),IsEnabled=false};head.Children.Add(status);DockPanel.SetDock(head,Dock.Top);root.Children.Add(head);
  var tabs=new TabControl();TabItem? closeoutTab=null,financialTab=null,operationsTab=null;
  if(service.Can("Reports.Operations"))
  {
   operationsTab=new TabItem{Header="Project performance",Content=ReportPage(ops)};tabs.Items.Add(operationsTab);
   var page=new DockPanel();if(service.Can("Projects.Closeout")){DockPanel.SetDock(lifecycle,Dock.Top);page.Children.Add(lifecycle);}
   page.Children.Add(ReportPage(closeout));closeoutTab=new TabItem{Header="Closeout review",Content=page};tabs.Items.Add(closeoutTab);
  }
  if(CanFinance()){financialTab=new TabItem{Header="Financial preview",Content=ReportPage(finance)};tabs.Items.Add(financialTab);}
  bool CloseoutSelected()=>closeoutTab!=null&&ReferenceEquals(tabs.SelectedItem,closeoutTab);
  void Reset(){ops.Text=finance.Text=closeout.Text="";status.Text="";bool selected=projects.SelectedItem is PendingProject;start.IsEnabled=end.IsEnabled=!CloseoutSelected();generate.IsEnabled=selected&&tabs.SelectedItem!=null;lifecycle.IsEnabled=selected&&CloseoutSelected()&&service.Can("Projects.Closeout")&&service.Can("Reports.Operations");status.Text=!selected?"Choose a project.":CloseoutSelected()?"Closeout review covers all dates. Select Generate report.":"Choose dates and generate the selected report.";}
  generate.Click+=(_,_)=>{if(CloseoutSelected())RefreshCloseout();else if(financialTab!=null&&ReferenceEquals(tabs.SelectedItem,financialTab))Refresh(false,true);else if(operationsTab!=null&&ReferenceEquals(tabs.SelectedItem,operationsTab))Refresh(true,false);};
  lifecycle.Click+=(_,_)=>Run(()=>{if(projects.SelectedItem is not PendingProject p)throw new ArgumentException("Select project.");new ProjectLifecycleWindow(service,p.Id){Owner=this}.ShowDialog();RefreshCloseout();});
  root.Children.Add(tabs);Content=root;projects.SelectionChanged+=(_,_)=>Run(Reset);tabs.SelectionChanged+=(_,_)=>Run(Reset);start.SelectedDateChanged+=(_,_)=>Run(Reset);end.SelectedDateChanged+=(_,_)=>Run(Reset);
  if(list.Count==1)projects.SelectedIndex=0;Run(Reset);
 }
 bool CanFinance()=>service.Can("Reports.Financial")&&service.Can("Billing.Invoices.View")&&service.Can("Billing.Rates");
 static ScrollViewer ReportPage(TextBlock content){content.Margin=new Thickness(16);content.FontSize=15;content.LineHeight=23;return new ScrollViewer{Content=content,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};}
 void Run(Action action){try{action();}catch(UnauthorizedAccessException){ops.Text=finance.Text=closeout.Text="";status.Text="Access denied.";}catch(ArgumentException e){status.Text=e.Message;}catch(Exception){ops.Text=finance.Text=closeout.Text="";status.Text="Report unavailable.";}}

 void RefreshCloseout()
 {
  closeout.Text="";status.Text="";
  try{
   if(projects.SelectedItem is not PendingProject p)throw new ArgumentException("Select project.");
   var r=service.ReadProjectCloseoutReview(p.Id);
   closeout.Text=$"{r.ProjectNumber} • All dates (ignores report date filters)\n\nProject state: {r.ProjectState}\nSetup state: {r.SetupState}\nConflict screening: {r.ConflictStatus}\n\nPhases: {r.Phases}\nOutstanding tasks (excluding cancelled/discontinued): {r.OpenTasks}\nOpen QA items: {r.OpenQualityItems}\nScope proposals awaiting decision: {r.PendingScopeDecisions}\nTime submissions awaiting review: {r.PendingTimeSubmissions}\nExpense submissions awaiting review: {r.PendingExpenseSubmissions}\n\nRead-only operational snapshot. Zero outstanding counts do not authorize closure, final delivery or billing. Financial and contractual closeout require separate review.";
   status.Text="Operational closeout snapshot generated.";
  }catch(UnauthorizedAccessException){ops.Text=finance.Text=closeout.Text="";status.Text="Access denied.";}catch(ArgumentException e){status.Text=e.Message;}catch(Exception){status.Text="Closeout review unavailable.";}
 }
 void Refresh(bool operations,bool financial)
 {
  ops.Text=finance.Text=closeout.Text="";status.Text="";
  try{
   if(projects.SelectedItem is not PendingProject p)throw new ArgumentException("Select project.");
   var a=WorkWindowFields.Day(start);var b=WorkWindowFields.Day(end);
   if(operations){var r=service.ReadProjectOperations(p.Id,a,b);ops.Text=$"{r.ProjectNumber} • {a:yyyy-MM-dd} through {b:yyyy-MM-dd}\n\nPhases overlapping range: {r.Phases}\nPlanned tasks: {r.PlannedTasks}\nIn progress: {r.StartedTasks}\nCompleted: {r.CompletedTasks}\nCancelled (historical): {r.CancelledTasks}\nDiscontinued (historical): {r.DiscontinuedTasks}\nTotal estimate of noncancelled/nondiscontinued overlapping tasks: {r.EstimatedMinutes/60m:0.00} h (not prorated)\n\nSubmitted: {r.SubmittedMinutes/60m:0.00} h\nApproved: {r.ApprovedMinutes/60m:0.00} h\nPending review: {r.PendingMinutes/60m:0.00} h\nRejected: {r.RejectedMinutes/60m:0.00} h\nInvalidated erroneous submissions: {r.InvalidatedMinutes/60m:0.00} h\nExpense submissions (all): {r.ExpenseSubmissions}\nInvalidated expenses: {r.InvalidatedExpenseSubmissions}\nCurrent open QA items (all dates): {r.OpenQualityItems}";}
   if(financial){var r=service.ReadProjectFinancialReport(p.Id,a,b);finance.Text=$"{r.ProjectNumber} • {a:yyyy-MM-dd} through {b:yyyy-MM-dd}\n\nApproved time: {r.ApprovedMinutes/60m:0.00} h\nBillable: {r.BillableMinutes/60m:0.00} h\nNonbillable: {r.NonbillableMinutes/60m:0.00} h\nUnclassified: {r.UnclassifiedMinutes/60m:0.00} h\nEstimated labor cost: {(r.EstimatedLaborCost.HasValue?r.EstimatedLaborCost.Value.ToString("C2"):"Incomplete")}\nMissing cost rates: {r.MissingCostRates}\nApproved expense cost: {r.ApprovedExpenseCost:C2}\nTotal operational cost: {(r.TotalOperationalCost.HasValue?r.TotalOperationalCost.Value.ToString("C2"):"Incomplete")}\n\nUnapproved internal draft value: {r.DraftValue:C2}\nApproved internal draft value: {r.ApprovedInternalDraftValue:C2}\nOf approved drafts, on reconciliation hold: {r.HeldApprovedDraftCount} / {r.HeldApprovedDraftValue:C2} (included above)\nCancelled draft value: {r.CancelledDraftValue:C2}\n\nNo posted revenue, profit or accounts receivable inferred.";}
   status.Text="Selected report generated.";
  }catch(UnauthorizedAccessException){ops.Text=finance.Text="";status.Text="Access denied.";}catch(ArgumentException e){status.Text=e.Message;}catch(Exception){ops.Text=finance.Text="";status.Text="Report unavailable.";}
 }
}
