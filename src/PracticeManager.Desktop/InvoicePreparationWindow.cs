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
 readonly TextBlock policyText=new(){TextWrapping=TextWrapping.Wrap};
 public InvoicePreparationWindow(LocalPractice service)
 {
  this.service=service;Title="Operational invoice preparation — no posting";Width=1120;Height=700;WindowStartupLocation=WindowStartupLocation.CenterOwner;
  var root=new DockPanel{Margin=new Thickness(16)};var h=new StackPanel();h.Children.Add(new TextBlock{Text=Title,FontSize=24,FontWeight=FontWeights.SemiBold});h.Children.Add(new TextBlock{Text="Internal drafts only. Manager remains the accounting authority. A draft number is not an issued invoice; approval neither sends nor posts it.",TextWrapping=TextWrapping.Wrap});h.Children.Add(status);DockPanel.SetDock(h,Dock.Top);root.Children.Add(h);var tabs=new TabControl();
  if(service.Can("Billing.Invoices.View"))tabs.Items.Add(new TabItem{Header="Invoice drafts",Content=BuildInvoices()});
  if(service.Can("Billing.Rates"))tabs.Items.Add(new TabItem{Header="Restricted employee rates",Content=BuildRates()});
  root.Children.Add(tabs);Content=root;
 }
 void Run(Action a){try{a();}catch(UnauthorizedAccessException){status.Text="Access denied.";candidates.ItemsSource=drafts.ItemsSource=rates.ItemsSource=null;}catch(ArgumentException e){status.Text=e.Message;}catch(Exception){status.Text="Operation failed. Refresh before retrying.";}}
 long Project()=>projects.SelectedItem is PendingProject p?p.Id:throw new ArgumentException("Select active project.");
 long Employee()=>employees.SelectedItem is EmployeeRecord e?e.Id:throw new ArgumentException("Select employee.");
 UIElement BuildInvoices()
 {
  var p=new DockPanel();var head=new StackPanel();projects.ItemsSource=service.SearchPendingProjects().Where(x=>x.State=="Active").ToArray();head.Children.Add(projects);head.Children.Add(policyText);var bar=new WrapPanel();bar.Children.Add(new TextBlock{Text="Draft date"});bar.Children.Add(issue);bar.Children.Add(new TextBlock{Text="Due date"});bar.Children.Add(due);
  WorkWindowFields.Button(bar,"Record policy",()=>Run(Policy));WorkWindowFields.Button(bar,"Refresh",()=>Run(RefreshInvoices));WorkWindowFields.Button(bar,"Create draft",()=>Run(Create));WorkWindowFields.Button(bar,"View selected draft",()=>Run(ViewDraft));if(service.CanCompareInvoiceCandidate())WorkWindowFields.Button(bar,"Compare Manager candidate",()=>Run(()=>{var d=Selected();new InvoiceCandidateWindow(service,Project(),d){Owner=this}.ShowDialog();}));WorkWindowFields.Button(bar,"Approve draft",()=>Run(()=>Decide(true)));WorkWindowFields.Button(bar,"Cancel unsent draft",()=>Run(()=>Decide(false)));WorkWindowFields.Button(bar,"Export approved CSV",()=>Run(Export));WorkWindowFields.Button(bar,"Save review DOCX",()=>Run(()=>{var d=Selected();ReviewDocumentSave.Save(this,service.PrepareInvoiceReviewDocx(Project(),d.Id,d.PayloadHash));}));WorkWindowFields.Button(bar,"Review document history",()=>Run(()=>{var d=Selected();var p=Project();new ReviewDocumentHistoryWindow(()=>service.ReadInvoiceReviewDocuments(p,d.Id),r=>service.RetrieveInvoiceReviewDocument(p,d.Id,r.Id,r.Sha256)){Owner=this}.ShowDialog();}));head.Children.Add(bar);DockPanel.SetDock(head,Dock.Top);p.Children.Add(head);
  candidates.SelectionMode=DataGridSelectionMode.Extended;WorkWindowFields.Columns(candidates,("Source","SourceId"),("Date","Date"),("Employee","Employee"),("Kind","Kind"),("Description","Description"),("Minutes","Minutes"),("Bill rate USD/h","BillingRate"),("Line USD","Amount"),("Status","Status"));
  WorkWindowFields.Columns(drafts,("Draft","Number"),("Date","InvoiceDate"),("Due","DueDate"),("Total USD","Total"),("State","State"));
  var split=new Grid();split.RowDefinitions.Add(new RowDefinition());split.RowDefinitions.Add(new RowDefinition());Grid.SetRow(candidates,0);Grid.SetRow(drafts,1);split.Children.Add(candidates);split.Children.Add(drafts);p.Children.Add(split);projects.SelectionChanged+=(_,_)=>Run(RefreshInvoices);return p;
 }
 void RefreshInvoices()
 {
  candidates.ItemsSource=drafts.ItemsSource=null;var project=Project();var policy=service.ReadBillingPolicy(project);
  policyText.Text=policy==null?"No reviewed billing policy.":$"{policy.Mode} • fixed limit {policy.FixedLimit:C2} • total cap {(policy.TotalLimit.HasValue?policy.TotalLimit.Value.ToString("C2"):"not set")} • expenses {(policy.Expenses?"allowed":"not allowed")}";
  drafts.ItemsSource=service.ReadInvoiceDrafts(project);if(policy!=null)candidates.ItemsSource=service.ReadInvoiceCandidates(project,WorkWindowFields.Day(issue));status.Text="";
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
  var p=new StackPanel{Margin=new Thickness(16)};p.Children.Add(new TextBlock{Text="Record actual reviewed contract terms. This does not amend the contract.",TextWrapping=TextWrapping.Wrap});var mode=new ComboBox{ItemsSource=new[]{"FixedFee","Hourly","Hybrid"},SelectedIndex=0};p.Children.Add(mode);var fixedLimit=Field(p,"Fixed component limit (USD)","0");var total=Field(p,"Total cap (blank only if contract has no cap)");var expense=new CheckBox{Content="Contract permits separately billed expenses"};p.Children.Add(expense);var note=Field(p,"Contract evidence");Dialog("Review billing policy",p,()=>service.RecordBillingPolicy(new(project,(string)mode.SelectedItem,Money(fixedLimit),string.IsNullOrWhiteSpace(total.Text)?null:Money(total),expense.IsChecked==true,note.Text)));RefreshInvoices();
 }
 void Create()
 {
  var project=Project();var date=WorkWindowFields.Day(issue);var deadline=WorkWindowFields.Day(due);var ids=candidates.SelectedItems.Cast<InvoiceCandidate>().Select(x=>x.SourceId).ToArray();var p=new StackPanel{Margin=new Thickness(16)};p.Children.Add(new TextBlock{Text=$"{ids.Length} selected work sources. Optional fixed-fee line. Review selection before recording.",TextWrapping=TextWrapping.Wrap});var description=Field(p,"Fixed-fee description (optional)");var amount=Field(p,"Fixed-fee USD amount (optional)");var key=Guid.NewGuid();Dialog("Create internal draft",p,()=>{IReadOnlyList<FixedFeeInput> fees=string.IsNullOrWhiteSpace(amount.Text)?Array.Empty<FixedFeeInput>():new[]{new FixedFeeInput(description.Text,Money(amount))};service.CreateInvoiceDraft(new(key,project,date,deadline,fees,ids));});RefreshInvoices();
 }
 InvoiceDraftSummary Selected()=>drafts.SelectedItem as InvoiceDraftSummary??throw new ArgumentException("Select draft.");
 void ViewDraft(){var d=Selected();var detail=service.ReadInvoiceDraft(Project(),d.Id);var g=WorkWindowFields.Grid();WorkWindowFields.Columns(g,("Kind","Kind"),("Description","Description"),("Minutes","Minutes"),("Rate","BillingRate"),("Amount","Amount"));g.ItemsSource=detail.Lines;new Window{Owner=this,Title=$"{d.Number} — {d.State} — {d.Total:C2}",Width=820,Height=440,Content=g,WindowStartupLocation=WindowStartupLocation.CenterOwner}.ShowDialog();}
 void Decide(bool yes){var d=Selected();var project=Project();var note=WorkWindowFields.Note(this,yes?"Approve internal draft (no posting)":"Cancel unsent draft");if(note==null)return;var confirm=yes&&service.Can("Administration.Manage")&&service.ReadOwnerApprovalPolicy().Enabled&&MessageBox.Show(this,"If you created this draft, explicitly invoke the single-owner exception using the justification just entered? Otherwise choose No for independent approval.","Owner exception",MessageBoxButton.YesNo,MessageBoxImage.Warning)==MessageBoxResult.Yes;var result=service.DecideInvoiceDraft(project,d.Id,d.PayloadHash,yes,note,confirm);RefreshInvoices();status.Text=result;}
 void Export()
 {
  var d=Selected();var picker=new SaveFileDialog{FileName=d.Number+".csv",Filter="CSV file (*.csv)|*.csv",DefaultExt=".csv",OverwritePrompt=true};if(picker.ShowDialog(this)!=true)return;
  if(!string.Equals(Path.GetExtension(picker.FileName),".csv",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Use .csv extension.");
  var data=service.ExportInvoiceDraftCsv(Project(),d.Id,d.PayloadHash);using var stream=new FileStream(picker.FileName,FileMode.CreateNew,FileAccess.Write,FileShare.None);using var writer=new StreamWriter(stream,new UTF8Encoding(true));writer.Write(data);writer.Flush();stream.Flush(true);status.Text="Approved internal draft export saved. Not an issued invoice or posting.";
 }
 UIElement BuildRates()
 {
  var p=new DockPanel();var head=new WrapPanel();employees.ItemsSource=service.ReadEmployees().Where(e=>e.Active).ToArray();head.Children.Add(employees);WorkWindowFields.Button(head,"Refresh",()=>Run(RefreshRates));WorkWindowFields.Button(head,"Record effective rate",()=>Run(RecordRate));DockPanel.SetDock(head,Dock.Top);p.Children.Add(head);WorkWindowFields.Columns(rates,("Effective","EffectiveOn"),("Billing USD/h","BillingRate"),("Restricted cost USD/h","CostRate"));p.Children.Add(rates);employees.SelectionChanged+=(_,_)=>Run(RefreshRates);return p;
 }
 void RefreshRates(){rates.ItemsSource=null;rates.ItemsSource=service.ReadEmployeeRates(Employee());}
 void RecordRate(){var employee=Employee();var p=new StackPanel{Margin=new Thickness(16)};var date=new DatePicker{SelectedDate=DateTime.Today};p.Children.Add(date);var bill=Field(p,"Billing USD/hour");var cost=Field(p,"Cost USD/hour");Dialog("Record effective rates",p,()=>service.RecordEmployeeRate(new(employee,WorkWindowFields.Day(date),Money(bill),Money(cost))));RefreshRates();}
}
