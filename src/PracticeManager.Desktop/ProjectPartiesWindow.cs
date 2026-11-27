using System.Windows;
using System.Windows.Controls;
using PracticeManager.Core;
using PracticeManager.Persistence;
namespace PracticeManager.Desktop;
internal sealed class ProjectPartiesWindow:Window
{
 readonly LocalPractice service;readonly long project;readonly DataGrid grid=WorkWindowFields.Grid();readonly CheckBox history=new(){Content="Include retired parties"};readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap};
 public ProjectPartiesWindow(LocalPractice service,long project)
 {
  this.service=service;this.project=project;Title="Project parties and contacts";Width=1020;Height=630;WindowStartupLocation=WindowStartupLocation.CenterOwner;
  var root=new DockPanel{Margin=new Thickness(16)};var p=new StackPanel();p.Children.Add(new TextBlock{Text=Title,FontSize=24,FontWeight=FontWeights.SemiBold});p.Children.Add(new TextBlock{Text="Register known project participants using active CRM records. Names are historical snapshots. Changes require contact checklist review; registration is not conflict clearance, a declaration, authority to act or a signed agreement.",TextWrapping=TextWrapping.Wrap});
  var bar=new WrapPanel();bar.Children.Add(history);WorkWindowFields.Button(bar,"Refresh",()=>Run(Refresh));if(service.Can("Projects.Setup")){WorkWindowFields.Button(bar,"Register party",()=>Run(Register));WorkWindowFields.Button(bar,"Retire selected",()=>Run(Retire));}if(service.Can("Compliance.View")&&service.Can("Documents.View")&&service.Can("Documents.Restricted"))WorkWindowFields.Button(bar,"Declarations",()=>Run(()=>{if(grid.SelectedItem is not ProjectParty selected)throw new ArgumentException("Select a party.");new PartyDeclarationWindow(service,project,selected){Owner=this}.ShowDialog();Refresh();}));p.Children.Add(bar);p.Children.Add(status);DockPanel.SetDock(p,Dock.Top);root.Children.Add(p);
  WorkWindowFields.Columns(grid,("Role","Role"),("Party (recorded)","PartyName"),("Contact (recorded)","ContactName"),("State","State"),("Evidence","Note"),("Recorded UTC","RecordedUtc"),("Retirement evidence","RetirementNote"),("Retired UTC","RetiredUtc"));root.Children.Add(grid);Content=root;history.Checked+=(_,_)=>Run(Refresh);history.Unchecked+=(_,_)=>Run(Refresh);Run(Refresh);
 }
 void Run(Action action){try{action();}catch(UnauthorizedAccessException){grid.ItemsSource=null;status.Text="Access denied.";}catch(ArgumentException e){status.Text=e.Message;}catch(Exception){status.Text="Party operation unavailable; refresh before retrying.";}}
 void Refresh(){grid.ItemsSource=null;grid.ItemsSource=service.ReadProjectParties(project,history.IsChecked==true);status.Text="";}
 void Register()
 {
  var w=new Window{Owner=this,Title="Register project party",Width=610,Height=490,WindowStartupLocation=WindowStartupLocation.CenterOwner};var p=new StackPanel{Margin=new Thickness(16)};
  var clients=new ComboBox{ItemsSource=service.SearchClients("").Where(c=>c.Status=="Active").ToArray(),DisplayMemberPath="DisplayName"};p.Children.Add(new TextBlock{Text="Active CRM party"});p.Children.Add(clients);
  var contacts=new ComboBox{DisplayMemberPath="Name"};p.Children.Add(new TextBlock{Text="Optional contact (leave unselected for organization)"});p.Children.Add(contacts);
  var role=new ComboBox{ItemsSource=LocalPractice.ProjectPartyRoles};p.Children.Add(new TextBlock{Text="Project role"});p.Children.Add(role);
  var note=new TextBox{MaxLength=1000,AcceptsReturn=true,Height=90,TextWrapping=TextWrapping.Wrap};p.Children.Add(new TextBlock{Text="Evidence of participation"});p.Children.Add(note);var confirm=new CheckBox{Content="I verified this party and role for the selected project."};p.Children.Add(confirm);
  var save=new Button{Content="Register party"};p.Children.Add(save);w.Content=new ScrollViewer{Content=p};
  clients.SelectionChanged+=(_,_)=>{contacts.ItemsSource=null;if(clients.SelectedItem is Client c){try{contacts.ItemsSource=service.Contacts(c.Id);}catch(UnauthorizedAccessException){MessageBox.Show(w,"Access denied.");}catch(Exception){MessageBox.Show(w,"Contact lookup unavailable.");}}};
  save.Click+=(_,_)=>{try{if(clients.SelectedItem is not Client c||role.SelectedItem is not string r)throw new ArgumentException("Select party and role.");service.RegisterProjectParty(project,c.Id,(contacts.SelectedItem as Contact)?.Id,r,note.Text,confirm.IsChecked==true);w.DialogResult=true;}catch(ArgumentException e){MessageBox.Show(w,e.Message);}catch(UnauthorizedAccessException){MessageBox.Show(w,"Access denied.");}catch(Exception){MessageBox.Show(w,"Party registration unavailable; refresh before retrying.");}};
  if(w.ShowDialog()==true)Refresh();
 }
 void Retire(){if(grid.SelectedItem is not ProjectParty p||p.State!="Active")throw new ArgumentException("Select an active party.");var note=WorkWindowFields.Note(this,"Retire project party");if(note==null)return;var result=service.RetireProjectParty(project,p.Id,note,true);Refresh();status.Text=result;}
}
