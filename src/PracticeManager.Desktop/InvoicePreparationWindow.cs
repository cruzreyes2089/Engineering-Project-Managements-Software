using System.Globalization;
using System.IO;
using System.Text;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using PracticeManager.Core;
using PracticeManager.Persistence;
namespace PracticeManager.Desktop;
internal sealed class InvoicePreparationWindow:Window
{
 readonly LocalPractice service;readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap};
 readonly ComboBox projects=new(){DisplayMemberPath="Number"},employees=new(){DisplayMemberPath="Name"};
 readonly DataGrid candidates=WorkWindowFields.Grid(),drafts=WorkWindowFields.Grid(),rates=WorkWindowFields.Grid();
 readonly DatePicker issue=new(){SelectedDate=DateTime.Today},due=new(){SelectedDate=DateTime.Today.AddDays(30)};
 readonly TextBlock policyText=new(){TextWrapping=TextWrapping.Wrap},selectionText=new(){TextWrapping=TextWrapping.Wrap};
 readonly List<(Button Button,Func<bool> Allowed)> actions=new();
 BillingPolicy? loadedPolicy;long? loadedProject;bool refreshing,selecting;TabControl? invoiceTabs;TabItem? savedTab;
 public InvoicePreparationWindow(LocalPractice service)
 {
  this.service=service;Title="Invoice preparation — internal drafts";Width=1120;Height=700;WindowStartupLocation=WindowStartupLocation.CenterOwner;
  var root=new DockPanel{Margin=new Thickness(16)};var h=new StackPanel();h.Children.Add(new TextBlock{Text=Title,FontSize=24,FontWeight=FontWeights.SemiBold});h.Children.Add(new TextBlock{Text="Internal drafts only. Manager remains the accounting authority. A draft number is not an issued invoice; approval neither sends nor posts it.",TextWrapping=TextWrapping.Wrap});h.Children.Add(status);DockPanel.SetDock(h,Dock.Top);root.Children.Add(h);var tabs=new TabControl();
  if(service.Can("Billing.Invoices.View")&&service.Can("Projects.View")&&service.Can("Clients.View"))tabs.Items.Add(new TabItem{Header="Invoice drafts",Content=BuildInvoices()});
  if(service.Can("Billing.Rates")&&service.Can("Employees.View"))tabs.Items.Add(new TabItem{Header="Restricted employee rates",Content=BuildRates()});
  root.Children.Add(tabs);Content=root;
 }
 void Run(Action a){try{a();}catch(UnauthorizedAccessException){ClearInvoices();rates.ItemsSource=null;status.Text="Access denied.";}catch(ArgumentException e){status.Text=e.Message;}catch(Exception){ClearInvoices();rates.ItemsSource=null;status.Text="Operation failed. Refresh before retrying.";}}
 long Project()=>projects.SelectedItem is PendingProject p?p.Id:throw new ArgumentException("Select active project.");
 long Employee()=>employees.SelectedItem is EmployeeRecord e?e.Id:throw new ArgumentException("Select employee.");
 UIElement BuildInvoices()
 {
  var p=new DockPanel();var head=new StackPanel();
  var projectList=service.SearchPendingProjects().Where(x=>x.State=="Active").ToArray();
  projects.ItemsSource=projectList;projects.DisplayMemberPath="";
  var template=new DataTemplate(typeof(PendingProject));var element=new FrameworkElementFactory(typeof(TextBlock));
  element.SetBinding(TextBlock.TextProperty,new System.Windows.Data.MultiBinding{StringFormat="{0} — {1} ({2})",Bindings={new System.Windows.Data.Binding("Number"),new System.Windows.Data.Binding("Title"),new System.Windows.Data.Binding("ClientName")}});
  template.VisualTree=element;projects.ItemTemplate=template;
  head.Children.Add(new TextBlock{Text="1. Choose project"});head.Children.Add(projects);head.Children.Add(policyText);
  bool View()=>loadedProject.HasValue&&projects.SelectedItem is PendingProject project&&project.Id==loadedProject&&service.Can("Billing.Invoices.View")&&service.Can("Projects.View")&&service.Can("Clients.View");
  bool Edit()=>View()&&service.Can("Billing.Invoices.Edit");
  bool Approve()=>View()&&service.Can("Billing.Invoices.Approve");
  InvoiceDraftSummary? Draft()=>drafts.SelectedItem as InvoiceDraftSummary;
  bool Selected()=>View()&&Draft()!=null;
  bool Approved()=>Selected()&&Draft()?.State=="Approved"&&Draft()?.ReconciliationHold==false;
  // UI availability is advisory. Services still recheck every grant, state and hash.
  void Add(WrapPanel bar,string label,Action action,Func<bool> allowed)
  {
   var button=new Button{Content=label,Margin=new Thickness(2),IsEnabled=false};
   button.Click+=(_,_)=>Run(()=>{if(!allowed())throw new ArgumentException("Select an eligible record or refresh.");action();});
   actions.Add((button,allowed));bar.Children.Add(button);
  }
  var settings=new WrapPanel();
  Add(settings,"Record billing terms",Policy,()=>Edit()&&loadedPolicy==null);
  WorkWindowFields.Button(settings,"Refresh",()=>Run(RefreshInvoices));
  head.Children.Add(settings);DockPanel.SetDock(head,Dock.Top);p.Children.Add(head);
  candidates.SelectionMode=DataGridSelectionMode.Extended;
  WorkWindowFields.Columns(candidates,("Date","Date"),("Employee","Employee"),("Kind","Kind"),("Description","Description"),("Minutes","Minutes"),("Bill rate USD/h","BillingRate"),("Line USD","Amount"),("Status","Status"));
  WorkWindowFields.Columns(drafts,("Draft","Number"),("Date","InvoiceDate"),("Due","DueDate"),("Total USD","Total"),("State","State"),("Reconciliation hold","ReconciliationHold"));
  var tabs=new TabControl();invoiceTabs=tabs;
  var preparation=new DockPanel();var prepareHead=new StackPanel();
  prepareHead.Children.Add(new TextBlock{Text="2. Select eligible work or enter a fixed-fee line",FontWeight=FontWeights.SemiBold});
  var dates=new WrapPanel();dates.Children.Add(new TextBlock{Text="Draft date",Margin=new Thickness(4,8,4,0)});dates.Children.Add(issue);dates.Children.Add(new TextBlock{Text="Due date",Margin=new Thickness(4,8,4,0)});dates.Children.Add(due);prepareHead.Children.Add(dates);prepareHead.Children.Add(new TextBlock{Text="Review the proposed due date against the actual contract. The initial 30-day suggestion is not a verified contract term.",TextWrapping=TextWrapping.Wrap});
  var prepareBar=new WrapPanel();
  Add(prepareBar,"Select eligible work (max 200)",()=>{var eligible=candidates.Items.OfType<InvoiceCandidate>().Where(x=>x.Status=="Eligible"&&x.Amount.HasValue).ToArray();selecting=true;try{candidates.UnselectAll();foreach(var row in eligible.Take(200))candidates.SelectedItems.Add(row);}finally{selecting=false;UpdateActions();}if(eligible.Length>200)status.Text="First 200 eligible records selected. Remaining eligible records are not included; review before creating draft.";},()=>Edit()&&loadedPolicy!=null&&candidates.Items.OfType<InvoiceCandidate>().Any(x=>x.Status=="Eligible"));
  Add(prepareBar,"Create draft",Create,()=>Edit()&&loadedPolicy!=null&&(loadedPolicy.Mode!="Hourly"||candidates.SelectedItems.Count>0)&&candidates.SelectedItems.Count<=200&&candidates.SelectedItems.Cast<InvoiceCandidate>().All(x=>x.Status=="Eligible"&&x.Amount.HasValue));
  prepareHead.Children.Add(prepareBar);prepareHead.Children.Add(selectionText);DockPanel.SetDock(prepareHead,Dock.Top);preparation.Children.Add(prepareHead);preparation.Children.Add(candidates);
  tabs.Items.Add(new TabItem{Header="Prepare draft",Content=preparation});
  var review=new DockPanel();var reviewHead=new StackPanel();reviewHead.Children.Add(new TextBlock{Text="3. Review saved drafts",FontWeight=FontWeights.SemiBold});
  var reviewBar=new WrapPanel();
  Add(reviewBar,"View draft",ViewDraft,Selected);
  Add(reviewBar,"Approve draft",()=>Decide(true),()=>Approve()&&Draft()?.State=="Draft");
  Add(reviewBar,"Cancel unsent draft",()=>Decide(false),()=>Edit()&&Draft()?.State=="Draft");
  Add(reviewBar,"Export approved CSV",Export,()=>Approved()&&service.Can("Billing.Invoices.Export"));
  Add(reviewBar,"Save review DOCX",()=>{var d=SelectedDraft();ReviewDocumentSave.Save(this,service.PrepareInvoiceReviewDocx(Project(),d.Id,d.PayloadHash));},()=>Approved()&&service.Can("Billing.Invoices.Export"));
  var more=new Menu{VerticalAlignment=VerticalAlignment.Center};var advanced=new MenuItem{Header="Advanced controls"};more.Items.Add(advanced);
  void MenuAction(string label,Action action,Func<bool> allowed){var item=new MenuItem{Header=label};advanced.Items.Add(item);item.Click+=(_,_)=>Run(()=>{if(!allowed())throw new ArgumentException("Select an eligible draft.");action();});advanced.SubmenuOpened+=(_,_)=>{try{item.IsEnabled=allowed();}catch(Exception){item.IsEnabled=false;}};}
  MenuAction("Compare Manager candidate",()=>{var d=SelectedDraft();new InvoiceCandidateWindow(service,Project(),d){Owner=this}.ShowDialog();},()=>Approved()&&service.CanCompareInvoiceCandidate());
  MenuAction("Place reconciliation hold",Hold,()=>Edit()&&Approve()&&Approved());
  MenuAction("View hold history",HoldEvidence,Selected);
  MenuAction("Independently release hold",ReleaseHold,()=>Edit()&&Approve()&&Draft()?.State=="Approved"&&Draft()?.ReconciliationHold==true);
  MenuAction("Review document history",()=>{var d=SelectedDraft();var project=Project();new ReviewDocumentHistoryWindow(()=>service.ReadInvoiceReviewDocuments(project,d.Id),r=>service.RetrieveInvoiceReviewDocument(project,d.Id,r.Id,r.Sha256)){Owner=this}.ShowDialog();},Selected);
  reviewBar.Children.Add(more);reviewHead.Children.Add(reviewBar);DockPanel.SetDock(reviewHead,Dock.Top);review.Children.Add(reviewHead);review.Children.Add(drafts);
  savedTab=new TabItem{Header="Saved drafts",Content=review};tabs.Items.Add(savedTab);p.Children.Add(tabs);
  projects.SelectionChanged+=(_,_)=>Run(RefreshInvoices);
  issue.SelectedDateChanged+=(_,_)=>{if(!refreshing&&projects.SelectedItem!=null)Run(RefreshInvoices);};
  candidates.SelectionChanged+=(_,_)=>{if(!selecting)UpdateActions();};drafts.SelectionChanged+=(_,_)=>UpdateActions();
  if(projectList.Length==1)projects.SelectedIndex=0;else UpdateActions();
  return p;
 }
 InvoiceDraftSummary SelectedDraft()=>Selected();
 void UpdateActions()
 {
  foreach(var a in actions){try{a.Button.IsEnabled=a.Allowed();}catch(Exception){a.Button.IsEnabled=false;}}
  var selected=candidates.SelectedItems.OfType<InvoiceCandidate>().ToArray();
  selectionText.Text=loadedProject==null?"Choose a project to begin.":$"{selected.Length} selected work records · USD {selected.Where(x=>x.Status=="Eligible").Sum(x=>x.Amount??0m):0.00} eligible subtotal (before fixed-fee lines).";
 }
 void ClearInvoices(){loadedProject=null;loadedPolicy=null;policyText.Text="";candidates.ItemsSource=drafts.ItemsSource=null;UpdateActions();}
 void RefreshInvoices()
 {
  refreshing=true;
  try{
   ClearInvoices();var project=Project();var policy=service.ReadBillingPolicy(project);
   var saved=service.ReadInvoiceDrafts(project);var available=policy==null?Array.Empty<InvoiceCandidate>():service.ReadInvoiceCandidates(project,WorkWindowFields.Day(issue)).ToArray();
   policyText.Text=policy==null?"No reviewed billing terms. Record terms before preparing a draft.":$"{policy.Mode} · Fixed limit USD {policy.FixedLimit:0.00} · Total cap {(policy.TotalLimit.HasValue?policy.TotalLimit.Value.ToString("0.00",CultureInfo.InvariantCulture):"not set")} · Expenses {(policy.Expenses?"allowed":"not allowed")}";
   drafts.ItemsSource=saved;candidates.ItemsSource=available;loadedPolicy=policy;loadedProject=project;status.Text="";UpdateActions();
  }finally{refreshing=false;}
 }

 static TextBox Field(StackPanel panel,string label,string value=""){panel.Children.Add(new TextBlock{Text=label});var x=new TextBox{Text=value,MaxLength=1000};panel.Children.Add(x);return x;}
 static decimal Money(TextBox box)=>decimal.TryParse(box.Text,NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out var d)?d:throw new ArgumentException("Enter USD amount as 1200.00.");
 void Dialog(string title,StackPanel panel,Action save)
 {
  var w=new Window{Owner=this,Title=title,Width=540,Height=540,WindowStartupLocation=WindowStartupLocation.CenterOwner};var b=new Button{Content="Confirm",IsDefault=true};var cancel=new Button{Content="Cancel",IsCancel=true};var buttons=new WrapPanel();buttons.Children.Add(b);buttons.Children.Add(cancel);panel.Children.Add(buttons);w.Content=new ScrollViewer{Content=panel};b.Click+=(_,_)=>{try{save();w.DialogResult=true;}catch(ArgumentException e){MessageBox.Show(w,e.Message);}catch(UnauthorizedAccessException){MessageBox.Show(w,"Access denied.");}catch(Exception){MessageBox.Show(w,"Operation failed; verify history before retrying.");}};w.ShowDialog();
 }
 void Policy()
 {
  var project=Project();if(service.ReadBillingPolicy(project)!=null)throw new ArgumentException("Policy already recorded.");
  var p=new StackPanel{Margin=new Thickness(16)};p.Children.Add(new TextBlock{Text="Record actual reviewed contract terms. This does not amend the contract.",TextWrapping=TextWrapping.Wrap});var mode=new ComboBox{ItemsSource=new[]{"FixedFee","Hourly","Hybrid"},SelectedIndex=0};p.Children.Add(mode);var fixedLimit=Field(p,"Fixed component limit (USD)","0");var total=Field(p,"Total cap (blank only if contract has no cap)");var expense=new CheckBox{Content="Contract permits separately billed expenses"};p.Children.Add(expense);var note=Field(p,"Contract evidence");Dialog("Review billing policy",p,()=>service.RecordBillingPolicy(new(project,(string)mode.SelectedItem,Money(fixedLimit),string.IsNullOrWhiteSpace(total.Text)?null:Money(total),expense.IsChecked==true,note.Text)));RefreshInvoices();
 }
 void Create()
 {
  var project=Project();var date=WorkWindowFields.Day(issue);var deadline=WorkWindowFields.Day(due);var ids=candidates.SelectedItems.Cast<InvoiceCandidate>().Select(x=>x.SourceId).ToArray();var p=new StackPanel{Margin=new Thickness(16)};p.Children.Add(new TextBlock{Text=$"{ids.Length} selected work sources. Draft date: {date:yyyy-MM-dd}. Proposed due date: {deadline:yyyy-MM-dd}. Review before recording.",TextWrapping=TextWrapping.Wrap});TextBox? description=null,amount=null;if(loadedPolicy?.Mode!="Hourly"){description=Field(p,"Fixed-fee description (optional)");amount=Field(p,"Fixed-fee USD amount (optional)");}var key=Guid.NewGuid();long? created=null;Dialog("Create internal draft",p,()=>{IReadOnlyList<FixedFeeInput> fees=amount==null||string.IsNullOrWhiteSpace(amount.Text)?Array.Empty<FixedFeeInput>():new[]{new FixedFeeInput(description!.Text,Money(amount))};created=service.CreateInvoiceDraft(new(key,project,date,deadline,fees,ids));});if(created.HasValue){RefreshInvoices();drafts.SelectedItem=drafts.Items.OfType<InvoiceDraftSummary>().SingleOrDefault(x=>x.Id==created.Value);if(invoiceTabs!=null&&savedTab!=null)invoiceTabs.SelectedItem=savedTab;status.Text="Internal draft recorded. Review before approval."; }
 }
 InvoiceDraftSummary Selected()=>drafts.SelectedItem as InvoiceDraftSummary??throw new ArgumentException("Select draft.");
 void ViewDraft(){var d=Selected();var detail=service.ReadInvoiceDraft(Project(),d.Id);var g=WorkWindowFields.Grid();WorkWindowFields.Columns(g,("Kind","Kind"),("Description","Description"),("Minutes","Minutes"),("Rate","BillingRate"),("Amount","Amount"));g.ItemsSource=detail.Lines;new Window{Owner=this,Title=$"{d.Number} — {d.State}{(detail.Summary.ReconciliationHold?" — HOLD":"")} — {d.Total:C2}",Width=820,Height=440,Content=g,WindowStartupLocation=WindowStartupLocation.CenterOwner}.ShowDialog();}
 void Decide(bool yes){var d=Selected();var project=Project();var note=WorkWindowFields.Note(this,yes?"Approve internal draft (no posting)":"Cancel unsent draft");if(note==null)return;var confirm=yes&&service.Can("Administration.Manage")&&service.ReadOwnerApprovalPolicy().Enabled&&MessageBox.Show(this,"If you created this draft, explicitly invoke the single-owner exception using the justification just entered? Otherwise choose No for independent approval.","Owner exception",MessageBoxButton.YesNo,MessageBoxImage.Warning)==MessageBoxResult.Yes;var result=service.DecideInvoiceDraft(project,d.Id,d.PayloadHash,yes,note,confirm);RefreshInvoices();status.Text=result;}
 void Hold()
 {
  var d=Selected();var project=Project();
  if(d.State!="Approved"||d.ReconciliationHold)throw new ArgumentException("Select approved draft not already on hold.");
  if(MessageBox.Show(this,"Protect this approved draft from further exports while investigating a discrepancy. Existing external invoices, exported files, source allocations and contractual caps are unchanged. An independent reviewer may release only a verified correct, externally unissued draft. Continue?","Place reconciliation hold",MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes)return;
  var note=WorkWindowFields.Note(this,"Document discrepancy and required accounting investigation");if(note==null)return;
  service.HoldApprovedInvoice(project,d.Id,d.PayloadHash,note,true);RefreshInvoices();status.Text="Hold recorded; no accounting transaction created.";
 }
 void HoldEvidence()
 {
  var d=Selected();var history=service.ReadInvoiceHoldHistory(Project(),d.Id);var grid=WorkWindowFields.Grid();
  WorkWindowFields.Columns(grid,("Event","EventId"),("Kind","Kind"),("Prior","PriorEventId"),("Reason","Reason"),("Evidence reference","EvidenceReference"),("Actor","ActorId"),("Recorded UTC","RecordedUtc"),("Payload SHA256","PayloadHash"));grid.ItemsSource=history;
  new Window{Owner=this,Title="Immutable invoice hold history — "+d.Number,Width=1000,Height=460,Content=grid,WindowStartupLocation=WindowStartupLocation.CenterOwner}.ShowDialog();
 }
 void ReleaseHold()
 {
  var d=Selected();var project=Project();var hold=service.ReadApprovedInvoiceHold(project,d.Id)??throw new ArgumentException("Select a currently held draft.");
  var p=new StackPanel{Margin=new Thickness(16)};p.Children.Add(new TextBlock{Text="Independent reviewer only. Release restores internal preparation, not an issued invoice. Do not release an erroneous, issued, paid or uncertain draft. Review authoritative external accounting and off-system evidence; an absent API match alone is insufficient.",TextWrapping=TextWrapping.Wrap});
  var evidence=Field(p,"Independent accounting evidence reference");var reason=Field(p,"Reason the discrepancy is resolved");var correct=new CheckBox{Content="Original draft independently verified correct."};var unissued=new CheckBox{Content=new TextBlock{Text="Complete independent review confirms no external issuance, settlement or unresolved accounting action.",TextWrapping=TextWrapping.Wrap}};p.Children.Add(correct);p.Children.Add(unissued);
  Dialog("Independent release of hold #"+hold.EventId,p,()=>service.ReleaseApprovedInvoiceHold(project,d.Id,hold.EventId,d.PayloadHash,evidence.Text,reason.Text,correct.IsChecked==true,unissued.IsChecked==true));RefreshInvoices();
 }
 void Export()
 {
  var d=Selected();var picker=new SaveFileDialog{FileName=d.Number+".csv",Filter="CSV file (*.csv)|*.csv",DefaultExt=".csv",OverwritePrompt=true};if(picker.ShowDialog(this)!=true)return;
  if(!string.Equals(Path.GetExtension(picker.FileName),".csv",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Use .csv extension.");
  if(File.Exists(picker.FileName)||Directory.Exists(picker.FileName))throw new ArgumentException("Choose a new file name; existing files are never overwritten.");var data=service.ExportInvoiceDraftCsv(Project(),d.Id,d.PayloadHash);using var stream=new FileStream(picker.FileName,FileMode.CreateNew,FileAccess.Write,FileShare.None);using var writer=new StreamWriter(stream,new UTF8Encoding(true));writer.Write(data);writer.Flush();stream.Flush(true);status.Text="Approved internal draft export saved. Not an issued invoice or posting.";
 }
 UIElement BuildRates()
 {
  var p=new DockPanel();var head=new WrapPanel();employees.ItemsSource=service.ReadEmployees().Where(e=>e.Active).ToArray();head.Children.Add(new TextBlock{Text="Employee",Margin=new Thickness(4,8,4,0)});head.Children.Add(employees);WorkWindowFields.Button(head,"Refresh",()=>Run(RefreshRates));WorkWindowFields.Button(head,"Record effective rate",()=>Run(RecordRate));DockPanel.SetDock(head,Dock.Top);p.Children.Add(head);WorkWindowFields.Columns(rates,("Effective","EffectiveOn"),("Billing USD/h","BillingRate"),("Restricted cost USD/h","CostRate"));p.Children.Add(rates);employees.SelectionChanged+=(_,_)=>Run(RefreshRates);return p;
 }
 void RefreshRates(){rates.ItemsSource=null;rates.ItemsSource=service.ReadEmployeeRates(Employee());}
 void RecordRate(){var employee=Employee();var p=new StackPanel{Margin=new Thickness(16)};var date=new DatePicker{SelectedDate=DateTime.Today};p.Children.Add(date);var bill=Field(p,"Billing USD/hour");var cost=Field(p,"Cost USD/hour");Dialog("Record effective rates",p,()=>service.RecordEmployeeRate(new(employee,WorkWindowFields.Day(date),Money(bill),Money(cost))));RefreshRates();}
}
