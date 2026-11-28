using System.Globalization;
using System.IO;
using System.Text;
using System.Security.Cryptography;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using PracticeManager.Core;
using PracticeManager.Persistence;
namespace PracticeManager.Desktop;
internal sealed class InvoicePreparationWindow:Window
{
 readonly LocalPractice service;readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap};
 readonly ComboBox projects=new(){DisplayMemberPath="ReportLabel"},employees=new(){DisplayMemberPath="Name"};
 readonly DataGrid candidates=WorkWindowFields.Grid(),drafts=WorkWindowFields.Grid(),rates=WorkWindowFields.Grid();
 readonly DatePicker issue=new(){SelectedDate=DateTime.Today},due=new(){SelectedDate=DateTime.Today.AddDays(30)};
 readonly TextBlock policyText=new(){TextWrapping=TextWrapping.Wrap};
 readonly Dictionary<string,Button> actions=new();bool hasPolicy,canEdit,canApprove,canExport,canCompare;BillingPolicy? loadedPolicy;
 public InvoicePreparationWindow(LocalPractice service)
 {
  this.service=service;Title="Invoice drafts";Width=1120;Height=700;WindowStartupLocation=WindowStartupLocation.CenterOwner;
  var root=new DockPanel{Margin=new Thickness(16)};var h=new StackPanel();h.Children.Add(new TextBlock{Text=Title,FontSize=24,FontWeight=FontWeights.SemiBold});h.Children.Add(new TextBlock{Text="Prepare and review draft invoices here. Issue invoices and record payments in Manager.",TextWrapping=TextWrapping.Wrap});h.Children.Add(status);DockPanel.SetDock(h,Dock.Top);root.Children.Add(h);var tabs=new TabControl();
  if(service.Can("Billing.Invoices.View"))tabs.Items.Add(new TabItem{Header="Invoice drafts",Content=BuildInvoices()});
  if(service.Can("Billing.Rates"))tabs.Items.Add(new TabItem{Header="Restricted employee rates",Content=BuildRates()});
  root.Children.Add(tabs);Content=root;
 }
 void Run(Action a){try{a();}catch(UnauthorizedAccessException){hasPolicy=false;policyText.Text="";status.Text="Access denied.";candidates.ItemsSource=drafts.ItemsSource=rates.ItemsSource=null;}catch(ArgumentException e){status.Text=e.Message;}catch(Exception){hasPolicy=false;policyText.Text="";candidates.ItemsSource=drafts.ItemsSource=rates.ItemsSource=null;status.Text="Operation failed. Refresh before retrying.";}finally{UpdateActions();}}
 long Project()=>projects.SelectedItem is PendingProject p?p.Id:throw new ArgumentException("Select an authorized project.");
 long Employee()=>employees.SelectedItem is EmployeeRecord e?e.Id:throw new ArgumentException("Select employee.");
 UIElement BuildInvoices()
 {
  var p=new DockPanel();var head=new StackPanel();projects.ItemsSource=service.SearchPendingProjects().Where(x=>x.State is "Active" or "OperationalClosed").ToArray();canEdit=service.Can("Billing.Invoices.Edit");canApprove=service.Can("Billing.Invoices.Approve");canExport=service.Can("Billing.Invoices.Export");canCompare=service.CanCompareInvoiceCandidate();head.Children.Add(new TextBlock{Text="Project"});head.Children.Add(projects);head.Children.Add(policyText);var bar=new WrapPanel();bar.Children.Add(new TextBlock{Text="Draft date"});bar.Children.Add(issue);bar.Children.Add(new TextBlock{Text="Due date"});bar.Children.Add(due);
  void Add(WrapPanel panel,string key,string label,Action action){var b=new Button{Content=label,Margin=new Thickness(2),IsEnabled=false};b.Click+=(_,_)=>Run(action);panel.Children.Add(b);actions.Add(key,b);}
  Add(bar,"policy","Set billing terms",Policy);WorkWindowFields.Button(bar,"Refresh",()=>Run(RefreshInvoices));Add(bar,"create","New draft",Create);Add(bar,"view","View draft",ViewDraft);Add(bar,"approve","Approve",()=>Decide(true));Add(bar,"export","Export CSV",Export);head.Children.Add(bar);
  var extra=new WrapPanel();Add(extra,"cancel","Cancel unsent draft",()=>Decide(false));Add(extra,"hold","Place on hold",Hold);Add(extra,"history","Hold history",HoldEvidence);Add(extra,"release","Release hold",ReleaseHold);
  if(canCompare)Add(extra,"compare","Compare Manager candidate",()=>{var d=Selected();new InvoiceCandidateWindow(service,Project(),d){Owner=this}.ShowDialog();});
  Add(extra,"word","Save review copy (Word)",()=>{var d=Selected();var project=Project();ReviewDocumentSave.Save(this,"invoice-review.docx",()=>service.PrepareInvoiceReviewDocx(project,d.Id,d.PayloadHash));});
  Add(extra,"documents","Review copy history",()=>{var d=Selected();var project=Project();new ReviewDocumentHistoryWindow(()=>service.ReadInvoiceReviewDocuments(project,d.Id),r=>service.RetrieveInvoiceReviewDocument(project,d.Id,r.Id,r.Sha256)){Owner=this}.ShowDialog();});
  head.Children.Add(new Expander{Header="Other invoice actions",Content=extra,IsExpanded=false});
  DockPanel.SetDock(head,Dock.Top);p.Children.Add(head);
  candidates.SelectionMode=DataGridSelectionMode.Extended;WorkWindowFields.Columns(candidates,("Source","SourceId"),("Date","Date"),("Employee","Employee"),("Kind","Kind"),("Description","Description"),("Minutes","Minutes"),("Bill rate USD/h","BillingRate"),("Line USD","Amount"),("Status","Status"));candidates.Columns[0].Visibility=Visibility.Collapsed;
  WorkWindowFields.Columns(drafts,("Draft","Number"),("Date","InvoiceDate"),("Due","DueDate"),("Total USD","Total"),("State","State"),("Reconciliation hold","ReconciliationHold"));
  var split=new Grid();split.RowDefinitions.Add(new RowDefinition());split.RowDefinitions.Add(new RowDefinition());Grid.SetRow(candidates,0);Grid.SetRow(drafts,1);split.Children.Add(candidates);split.Children.Add(drafts);p.Children.Add(split);projects.SelectionChanged+=(_,_)=>Run(RefreshInvoices);drafts.SelectionChanged+=(_,_)=>UpdateActions();candidates.SelectionChanged+=(_,_)=>UpdateActions();issue.SelectedDateChanged+=(_,_)=>{candidates.ItemsSource=null;hasPolicy=false;UpdateActions();status.Text="Refresh for the new draft date.";};due.SelectedDateChanged+=(_,_)=>UpdateActions();UpdateActions();return p;
 }
 void UpdateActions(){if(actions.Count==0)return;var d=drafts.SelectedItem as InvoiceDraftSummary;var selected=projects.SelectedItem is PendingProject;var dated=issue.SelectedDate is DateTime a&&due.SelectedDate is DateTime b&&b>=a;void Set(string key,bool value){if(actions.TryGetValue(key,out var button))button.IsEnabled=value;}
  Set("policy",selected&&canEdit&&!hasPolicy&&policyText.Text=="No reviewed billing policy.");Set("create",selected&&canEdit&&hasPolicy&&dated&&loadedPolicy!=null&&(loadedPolicy.Mode!="Hourly"||candidates.SelectedItems.Count>0)&&candidates.SelectedItems.Cast<InvoiceCandidate>().All(x=>x.Status=="Eligible"));Set("view",d!=null);Set("approve",canApprove&&d?.State=="Draft");Set("cancel",canEdit&&d?.State=="Draft");Set("hold",canEdit&&canApprove&&d is {State:"Approved",ReconciliationHold:false});Set("release",canEdit&&canApprove&&d is {State:"Approved",ReconciliationHold:true});Set("history",d!=null);Set("export",canExport&&d is {State:"Approved",ReconciliationHold:false});Set("word",canExport&&d is {State:"Approved",ReconciliationHold:false});Set("documents",canExport&&d?.State=="Approved");Set("compare",canCompare&&d!=null);}
 void RefreshInvoices()
 {
  hasPolicy=false;loadedPolicy=null;policyText.Text="";candidates.ItemsSource=drafts.ItemsSource=null;UpdateActions();var project=Project();var policy=service.ReadBillingPolicy(project);
  policyText.Text=policy==null?"No reviewed billing policy.":$"{(policy.Mode=="FixedFee"?"Fixed fee":policy.Mode)} • fixed limit {policy.FixedLimit:C2} • total cap {(policy.TotalLimit.HasValue?policy.TotalLimit.Value.ToString("C2"):"not set")} • separate expenses {(policy.Expenses?"allowed":"not allowed")}";
  drafts.ItemsSource=service.ReadInvoiceDrafts(project);if(policy!=null){if(policy.Mode!="FixedFee"||policy.Expenses)candidates.ItemsSource=service.ReadInvoiceCandidates(project,WorkWindowFields.Day(issue));loadedPolicy=policy;hasPolicy=true;}UpdateActions();status.Text=policy?.Mode=="FixedFee"&&!policy.Expenses?"Use New draft to enter a fixed-fee milestone. Time and expenses are not charged separately.":"";
 }
 static TextBox Field(StackPanel panel,string label,string value=""){panel.Children.Add(new TextBlock{Text=label});var x=new TextBox{Text=value,MaxLength=1000};panel.Children.Add(x);return x;}
 static decimal Money(TextBox box)=>decimal.TryParse(box.Text,NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out var d)?d:throw new ArgumentException("Enter USD amount as 1200.00.");
 void Dialog(string title,StackPanel panel,Action save)
 {
  var w=new Window{Owner=this,Title=title,Width=540,Height=540,WindowStartupLocation=WindowStartupLocation.CenterOwner};var b=new Button{Content="Confirm"};panel.Children.Add(b);w.Content=new ScrollViewer{Content=panel};b.Click+=(_,_)=>{try{save();w.DialogResult=true;}catch(ArgumentException e){MessageBox.Show(w,e.Message);}catch(UnauthorizedAccessException){MessageBox.Show(w,"Access denied.");}catch(Exception){MessageBox.Show(w,"Operation failed; verify history before retrying.");}};w.ShowDialog();
 }
 void Policy()
 {
  var project=Project();if(service.ReadBillingPolicy(project)!=null)throw new ArgumentException("Policy already recorded.");
  var accepted=service.ReadAcceptedBillingFee(project);var acceptedText=accepted.ToString("0.00",CultureInfo.InvariantCulture);
  var p=new StackPanel{Margin=new Thickness(16)};p.Children.Add(new TextBlock{Text=$"Accepted proposal: {accepted:C2}. Confirm terms against the signed agreement. This does not amend it.",TextWrapping=TextWrapping.Wrap});
  p.Children.Add(new TextBlock{Text="Billing method"});var mode=new ComboBox{ItemsSource=new[]{new KeyValuePair<string,string>("FixedFee","Fixed fee"),new KeyValuePair<string,string>("Hourly","Hourly"),new KeyValuePair<string,string>("Hybrid","Hybrid")},DisplayMemberPath="Value",SelectedValuePath="Key",SelectedIndex=0};p.Children.Add(mode);
  var fixedLimit=Field(p,"Fixed-fee component (USD)",acceptedText);var total=Field(p,"Total contract cap (USD, if applicable)");var uncapped=new CheckBox{Content="Agreement has no separate total cap"};p.Children.Add(uncapped);
  var expense=new CheckBox{Content="Agreement permits separate expense charges"};p.Children.Add(expense);var note=Field(p,"Signed agreement reference / review note");
  void UpdateMode(){var key=(string)mode.SelectedValue;fixedLimit.IsReadOnly=key!="Hybrid";fixedLimit.Text=key=="Hourly"?"0":key=="FixedFee"?acceptedText:"";}mode.SelectionChanged+=(_,_)=>UpdateMode();UpdateMode();
  void UpdateCap(){total.IsEnabled=uncapped.IsChecked!=true;if(!total.IsEnabled)total.Clear();}uncapped.Checked+=(_,_)=>UpdateCap();uncapped.Unchecked+=(_,_)=>UpdateCap();
  Dialog("Set billing terms",p,()=>{var key=(string)mode.SelectedValue;decimal? cap=uncapped.IsChecked==true?null:Money(total);service.RecordBillingPolicy(new(project,key,Money(fixedLimit),cap,expense.IsChecked==true,note.Text));});RefreshInvoices();
 }
 void Create()
 {
  var project=Project();var date=WorkWindowFields.Day(issue);var deadline=WorkWindowFields.Day(due);var policy=service.ReadBillingPolicy(project)??throw new ArgumentException("Set billing terms first.");var ids=candidates.SelectedItems.Cast<InvoiceCandidate>().Select(x=>x.SourceId).ToArray();var p=new StackPanel{Margin=new Thickness(16)};p.Children.Add(new TextBlock{Text=$"{ids.Length} selected work sources. Optional fixed-fee line. Review selection before recording.",TextWrapping=TextWrapping.Wrap});var description=Field(p,"Fixed-fee description (optional)");var amount=Field(p,"Fixed-fee USD amount (optional)");if(policy.Mode=="Hourly"){description.IsEnabled=amount.IsEnabled=false;}var key=Guid.NewGuid();Dialog("Create internal draft",p,()=>{IReadOnlyList<FixedFeeInput> fees=policy.Mode=="Hourly"||string.IsNullOrWhiteSpace(amount.Text)?Array.Empty<FixedFeeInput>():new[]{new FixedFeeInput(description.Text,Money(amount))};service.CreateInvoiceDraft(new(key,project,date,deadline,fees,ids));});RefreshInvoices();
 }
 InvoiceDraftSummary Selected()=>drafts.SelectedItem as InvoiceDraftSummary??throw new ArgumentException("Select draft.");
 void ViewDraft(){var d=Selected();var detail=service.ReadInvoiceDraft(Project(),d.Id);var g=WorkWindowFields.Grid();WorkWindowFields.Columns(g,("Kind","Kind"),("Description","Description"),("Minutes","Minutes"),("Rate","BillingRate"),("Amount","Amount"));g.ItemsSource=detail.Lines;new Window{Owner=this,Title=$"{d.Number} — {d.State}{(detail.Summary.ReconciliationHold?" — HOLD":"")} — {d.Total:C2}",Width=820,Height=440,Content=g,WindowStartupLocation=WindowStartupLocation.CenterOwner}.ShowDialog();}
 void Decide(bool yes){var d=Selected();var project=Project();var own=yes&&service.InvoiceApprovalRequiresOwnerConfirmation(project,d.Id,d.PayloadHash);var note=WorkWindowFields.Note(this,own?"Owner self-approval justification (no posting)":yes?"Approve internal draft (no posting)":"Cancel unsent draft");if(note==null)return;if(own&&MessageBox.Show(this,"You created this draft. Explicitly approve it under the enabled single-owner exception? This is not external invoice issuance.","Owner exception",MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes)return;var result=service.DecideInvoiceDraft(project,d.Id,d.PayloadHash,yes,note,own);try{RefreshInvoices();status.Text=result;}catch(Exception){hasPolicy=false;loadedPolicy=null;policyText.Text="";candidates.ItemsSource=drafts.ItemsSource=null;status.Text=result+". Refresh failed; reload before another action.";}}
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
  var d=Selected();var project=Project();var picker=new SaveFileDialog{FileName=d.Number+".csv",Filter="CSV file (*.csv)|*.csv",DefaultExt=".csv",OverwritePrompt=true};if(picker.ShowDialog(this)!=true)return;
  if(!string.Equals(Path.GetExtension(picker.FileName),".csv",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Use .csv extension.");
  _=LocalStoragePath.FilePath(picker.FileName,mustExist:false,mustBeNew:true,rejectSidecars:true);if(Project()!=project)throw new ArgumentException("Project selection changed.");var data=service.ExportInvoiceDraftCsv(project,d.Id,d.PayloadHash);var bytes=Encoding.UTF8.GetBytes("\uFEFF"+data);try{VerifiedDocumentFile.Save(picker.FileName,".csv",bytes,Convert.ToHexString(SHA256.HashData(bytes)));}finally{CryptographicOperations.ZeroMemory(bytes);}status.Text="Approved internal draft export saved. Not an issued invoice or posting.";
 }
 UIElement BuildRates()
 {
  var p=new DockPanel();var head=new WrapPanel();employees.ItemsSource=service.ReadEmployees().Where(e=>e.Active).ToArray();head.Children.Add(employees);WorkWindowFields.Button(head,"Refresh",()=>Run(RefreshRates));WorkWindowFields.Button(head,"Record effective rate",()=>Run(RecordRate));DockPanel.SetDock(head,Dock.Top);p.Children.Add(head);WorkWindowFields.Columns(rates,("Effective","EffectiveOn"),("Billing USD/h","BillingRate"),("Restricted cost USD/h","CostRate"));p.Children.Add(rates);employees.SelectionChanged+=(_,_)=>Run(RefreshRates);return p;
 }
 void RefreshRates(){rates.ItemsSource=null;rates.ItemsSource=service.ReadEmployeeRates(Employee());}
 void RecordRate(){var employee=Employee();var p=new StackPanel{Margin=new Thickness(16)};var date=new DatePicker{SelectedDate=DateTime.Today};p.Children.Add(date);var bill=Field(p,"Billing USD/hour");var cost=Field(p,"Cost USD/hour");Dialog("Record effective rates",p,()=>service.RecordEmployeeRate(new(employee,WorkWindowFields.Day(date),Money(bill),Money(cost))));RefreshRates();}
}
