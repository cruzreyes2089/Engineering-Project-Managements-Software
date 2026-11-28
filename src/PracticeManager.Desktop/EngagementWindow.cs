using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using PracticeManager.Core;
using PracticeManager.Persistence;
namespace PracticeManager.Desktop;
internal sealed class EngagementWindow:Window
{
 readonly LocalPractice service;
 readonly DataGrid proposals=new(){AutoGenerateColumns=false,IsReadOnly=true,CanUserAddRows=false,SelectionMode=DataGridSelectionMode.Single};
 readonly DataGrid projects=new(){AutoGenerateColumns=false,IsReadOnly=true,CanUserAddRows=false,SelectionMode=DataGridSelectionMode.Single};
 readonly TextBox search=new(){MaxLength=150,ToolTip="Search proposals and projects"};
 readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap};
 readonly Button edit=new(){Content="Edit draft"},issue=new(){Content="Record as sent"},accept=new(){Content="Record acceptance"},reject=new(){Content="Record rejection"},snapshot=new(){Content="View issued record"};
 public EngagementWindow(LocalPractice service)
 {
  this.service=service;Title="Proposals and projects — development";Width=1060;Height=650;WindowStartupLocation=WindowStartupLocation.CenterOwner;
  var root=new DockPanel{Margin=new Thickness(16)};var header=new StackPanel();
  header.Children.Add(new TextBlock{Text="Proposals and projects",FontSize=24,FontWeight=FontWeights.SemiBold});
  header.Children.Add(new TextBlock{Text="Synthetic records only. Recording issue/acceptance is manual evidence, not sending a document, signing a contract, or authorization to start work.",TextWrapping=TextWrapping.Wrap});
  header.Children.Add(new TextBlock{Text="Search by client, title or number"});header.Children.Add(search);header.Children.Add(status);DockPanel.SetDock(header,Dock.Top);root.Children.Add(header);
  var tabs=new TabControl();
  void Col(DataGrid g,string label,string prop)=>g.Columns.Add(new DataGridTextColumn{Header=label,Binding=new Binding(prop),Width=DataGridLength.Auto});
  foreach(var (label,prop) in new[]{("Number","Number"),("Client","ClientName"),("Title","Title"),("Site","Site"),("Fee (USD)","Fee"),("Revision","Revision"),("Status","State")})Col(proposals,label,prop);
  foreach(var (label,prop) in new[]{("Project","Number"),("Client","ClientName"),("Title","Title"),("Site","Site"),("Status","State"),("Setup","SetupState"),("Conflict screening","ConflictStatus")})Col(projects,label,prop);
  var pp=new DockPanel();var buttons=new WrapPanel();
  var create=new Button{Content="New proposal",IsEnabled=service.Can("Engagements.Edit")&&service.Can("Engagements.View")&&service.Can("Clients.View")};
  var refresh=new Button{Content="Refresh"};create.Click+=(_,_)=>Run(()=>Edit(null));edit.Click+=(_,_)=>Run(()=>Edit(Selected()));issue.Click+=(_,_)=>Run(Issue);accept.Click+=(_,_)=>Run(()=>Decision(true));reject.Click+=(_,_)=>Run(()=>Decision(false));snapshot.Click+=(_,_)=>Run(()=>ShowSnapshot(Selected()));refresh.Click+=(_,_)=>Refresh();
  var compliance=new Button{Content="Conflict screening",IsEnabled=service.Can("Compliance.View")};compliance.Click+=(_,_)=>Run(()=>{var d=Selected();if(d.State!="Draft")throw new ArgumentException("Select draft.");new ConflictReviewWindow(service,d.Id,d.Revision){Owner=this}.ShowDialog();});
  var reviewDoc=new Button{Content="Save proposal review DOCX",IsEnabled=service.Can("Engagements.Export")};reviewDoc.Click+=(_,_)=>Run(()=>{var x=Selected();ReviewDocumentSave.Save(this,service.PrepareProposalReviewDocx(x.Id,x.Revision,x.SnapshotHash));});
  var history=new Button{Content="Review document history",IsEnabled=service.Can("Engagements.Export")};history.Click+=(_,_)=>Run(()=>{var x=Selected();new ReviewDocumentHistoryWindow(()=>service.ReadProposalReviewDocuments(x.Id),d=>service.RetrieveProposalReviewDocument(x.Id,d.Id,d.Sha256)){Owner=this}.ShowDialog();});
  foreach(var b in new[]{create,edit,compliance,reviewDoc,history,issue,accept,reject,snapshot,refresh}){b.Margin=new Thickness(2);buttons.Children.Add(b);}
  DockPanel.SetDock(buttons,Dock.Top);pp.Children.Add(buttons);pp.Children.Add(proposals);
  tabs.Items.Add(new TabItem{Header="Proposals",Content=pp});
  var projectPanel=new DockPanel();var openProject=new Button{Content="Open selected project",Margin=new Thickness(2),IsEnabled=false};
  
  openProject.Click+=(_,_)=>Run(()=>{if(projects.SelectedItem is PendingProject p){new ProjectWorkspaceWindow(service,p.Id){Owner=this}.ShowDialog();Refresh();}});
  var actions=new WrapPanel();actions.Children.Add(openProject);
  void UpdateProjectActions(){openProject.IsEnabled=projects.SelectedItem is PendingProject&&service.Can("Projects.View")&&service.Can("Clients.View");}
  projects.SelectionChanged+=(_,_)=>UpdateProjectActions();
  projects.MouseDoubleClick+=(_,e)=>{if(openProject.IsEnabled&&e.OriginalSource is DependencyObject origin){DependencyObject? source=origin;while(source!=null&&source is not DataGridRow){source=source is System.Windows.Media.Visual||source is System.Windows.Media.Media3D.Visual3D?System.Windows.Media.VisualTreeHelper.GetParent(source):null;}if(source is DataGridRow row&&row.Item==projects.SelectedItem)openProject.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));}};
  proposals.SelectionChanged+=(_,_)=>{var selected=proposals.SelectedItem as ProposalSummary;compliance.IsEnabled=selected?.State=="Draft"&&service.Can("Compliance.View");reviewDoc.IsEnabled=selected!=null&&service.Can("Engagements.Export");history.IsEnabled=selected!=null&&service.Can("Engagements.Export");};
  UpdateProjectActions();compliance.IsEnabled=reviewDoc.IsEnabled=history.IsEnabled=false;
  DockPanel.SetDock(actions,Dock.Top);projectPanel.Children.Add(actions);projectPanel.Children.Add(projects);tabs.Items.Add(new TabItem{Header="Projects",Content=projectPanel});
  root.Children.Add(tabs);Content=root;search.TextChanged+=(_,_)=>Refresh();proposals.SelectionChanged+=(_,_)=>UpdateButtons();Refresh();
 }
 ProposalSummary Selected()=>proposals.SelectedItem as ProposalSummary??throw new ArgumentException("Select a proposal.");
 void Run(Action action){try{action();}catch(UnauthorizedAccessException){status.Text="Access denied.";}catch(ArgumentException e){status.Text=e.Message;}catch(Exception){status.Text="Operation failed. Refresh before retrying.";}}
 void Refresh(){proposals.ItemsSource=null;projects.ItemsSource=null;status.Text="";
  Run(()=>{if(service.Can("Engagements.View"))proposals.ItemsSource=service.SearchProposals(search.Text);});
  Run(()=>{if(service.Can("Projects.View"))projects.ItemsSource=service.SearchPendingProjects(search.Text);});UpdateButtons();}
 void UpdateButtons(){var x=proposals.SelectedItem as ProposalSummary;edit.IsEnabled=x?.State=="Draft"&&service.Can("Engagements.Edit");issue.IsEnabled=x?.State=="Draft"&&service.Can("Engagements.Issue");accept.IsEnabled=x?.State=="Issued"&&service.Can("Engagements.Accept")&&service.Can("Projects.View");reject.IsEnabled=x?.State=="Issued"&&service.Can("Engagements.Accept");snapshot.IsEnabled=x!=null&&x.State!="Draft";}
 void Edit(ProposalSummary? x)
 {
  var w=new Window{Owner=this,Title=x==null?"New proposal draft":"Edit proposal draft",Width=620,Height=530,WindowStartupLocation=WindowStartupLocation.CenterOwner};
  var panel=new StackPanel{Margin=new Thickness(16)};
  panel.Children.Add(new TextBlock{Text="Client"});var client=new ComboBox{ItemsSource=service.SearchClients("").Where(c=>c.Status=="Active").ToArray(),DisplayMemberPath="DisplayName",SelectedValuePath="Id",IsEnabled=x==null};if(x!=null)client.SelectedValue=x.ClientId;panel.Children.Add(client);
  TextBox Field(string label,string value,int max,bool multiline=false){panel.Children.Add(new TextBlock{Text=label});var b=new TextBox{Text=value,MaxLength=max,AcceptsReturn=multiline,TextWrapping=TextWrapping.Wrap,Height=multiline?140:30,VerticalScrollBarVisibility=multiline?ScrollBarVisibility.Auto:ScrollBarVisibility.Disabled};panel.Children.Add(b);return b;}
  var title=Field("Project title",x?.Title??"",200);var site=Field("Project site",x?.Site??"",300);var scope=Field("Scope",x?.Scope??"",20000,true);var fee=Field("Fee (USD, no currency conversion)",x?.Fee.ToString("0.00",CultureInfo.InvariantCulture)??"",30);
  var save=new Button{Content="Save draft"};panel.Children.Add(save);w.Content=new ScrollViewer{Content=panel};
  save.Click+=(_,_)=>{try{var cid=x?.ClientId??(client.SelectedItem as Client)?.Id??throw new ArgumentException("Select an active client.");if(!decimal.TryParse(fee.Text,NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out var amount))throw new ArgumentException("Enter fee as 1200.00.");var input=new ProposalInput(cid,title.Text,site.Text,scope.Text,amount);if(x==null)service.CreateProposal(input);else service.EditProposal(x.Id,x.Revision,input);w.DialogResult=true;}catch(ArgumentException e){MessageBox.Show(w,e.Message);}catch(Exception){MessageBox.Show(w,"Save failed; no success assumed.");}};
  if(w.ShowDialog()==true)Refresh();
 }
 void Issue(){var x=Selected();if(MessageBox.Show(this,"Freeze this draft as issued? This only records the event; it does not send a proposal.","Record issue",MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes)return;service.RecordProposalIssued(x.Id,x.Revision);Refresh();}
 void ShowSnapshot(ProposalSummary x){var w=new Window{Owner=this,Title="Frozen proposal data — not a signed agreement",Width=700,Height=420,Content=new TextBox{Text=service.ReadIssuedProposalSnapshot(x.Id),IsReadOnly=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto},WindowStartupLocation=WindowStartupLocation.CenterOwner};w.ShowDialog();}
 void Decision(bool accepted){var x=Selected();var w=new Window{Owner=this,Title=accepted?"Record acceptance — project stays Pending":"Record rejection",Width=530,Height=260,WindowStartupLocation=WindowStartupLocation.CenterOwner};var p=new StackPanel{Margin=new Thickness(16)};p.Children.Add(new TextBlock{Text="Record the source of the client's decision. No signed document or authorization is established by this entry.",TextWrapping=TextWrapping.Wrap});var note=new TextBox{AcceptsReturn=true,MaxLength=1000,Height=100,TextWrapping=TextWrapping.Wrap};p.Children.Add(note);var b=new Button{Content="Confirm decision"};p.Children.Add(b);w.Content=p;b.Click+=(_,_)=>{try{var r=service.RecordProposalDecision(x.Id,x.SnapshotHash??"",accepted,note.Text);if(r.Outcome is "Accepted" or "AlreadyAccepted" or "Rejected"){w.DialogResult=true;}else MessageBox.Show(w,r.Outcome);}catch(ArgumentException e){MessageBox.Show(w,e.Message);}catch(Exception){MessageBox.Show(w,"Decision failed; refresh before retrying.");}};if(w.ShowDialog()==true)Refresh();}
}
