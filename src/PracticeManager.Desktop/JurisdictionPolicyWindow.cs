using System.Windows;
using System.Windows.Controls;
using PracticeManager.Persistence;
namespace PracticeManager.Desktop;
internal sealed class JurisdictionPolicyWindow:Window
{
 readonly LocalPractice service;readonly DataGrid grid=WorkWindowFields.Grid();readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap};
 readonly CheckBox current=new(){Content="Current entries only",IsChecked=true};readonly TextBox jurisdiction=new(){MaxLength=300},note=new(){MaxLength=4000,AcceptsReturn=true,Height=100,TextWrapping=TextWrapping.Wrap};
 readonly ComboBox decision=new(){ItemsSource=new[]{"Unverified","Restricted","EligibleForScreening"},SelectedIndex=0};
 readonly CheckBox confirm=new(){Content="I reviewed the applicable employer/firm restrictions and supporting evidence."};
 public JurisdictionPolicyWindow(LocalPractice service)
 {
  this.service=service;Title="Firm jurisdiction policy";Width=1000;Height=660;WindowStartupLocation=WindowStartupLocation.CenterOwner;
  var root=new DockPanel{Margin=new Thickness(16)};var p=new StackPanel();p.Children.Add(new TextBlock{Text=Title,FontSize=24,FontWeight=FontWeights.SemiBold});
  p.Children.Add(new TextBlock{Text="Unknown jurisdiction is held. Enter the exact canonical permitting authority; aliases, municipal boundaries and overlapping authority must be independently verified. Eligible for screening is not legal clearance. Any new policy event invalidates prior reviews for that label.",TextWrapping=TextWrapping.Wrap});
  p.Children.Add(new TextBlock{Text="Canonical jurisdiction label"});p.Children.Add(jurisdiction);p.Children.Add(decision);p.Children.Add(new TextBlock{Text="Reviewed policy evidence"});p.Children.Add(note);p.Children.Add(confirm);
  var bar=new WrapPanel();bar.Children.Add(current);WorkWindowFields.Button(bar,"Refresh",()=>Run(Refresh));WorkWindowFields.Button(bar,"Use selected label",()=>Run(()=>{if(grid.SelectedItem is not JurisdictionPolicyEvent r)throw new ArgumentException("Select jurisdiction.");jurisdiction.Text=r.Jurisdiction;}));if(service.Can("Administration.Manage")&&service.Can("Compliance.Policy"))WorkWindowFields.Button(bar,"Record policy event",()=>Run(Save));p.Children.Add(bar);p.Children.Add(status);DockPanel.SetDock(p,Dock.Top);root.Children.Add(p);
  WorkWindowFields.Columns(grid,("Event","Id"),("Jurisdiction","Jurisdiction"),("Decision","Decision"),("Evidence","Evidence"),("Current","Current"),("UTC","RecordedUtc"));root.Children.Add(grid);Content=root;Run(Refresh);
 }
 void Run(Action a){try{a();}catch(UnauthorizedAccessException){grid.ItemsSource=null;status.Text="Access denied.";}catch(ArgumentException e){status.Text=e.Message;}catch(Exception){status.Text="Policy operation failed. Refresh.";}
 }
 void Refresh(){grid.ItemsSource=null;grid.ItemsSource=service.ReadJurisdictionPolicy(current.IsChecked==true);status.Text="";}
 void Save(){var name=jurisdiction.Text.Trim();var prior=service.ReadJurisdictionPolicy().SingleOrDefault(x=>string.Equals(x.Jurisdiction,name,StringComparison.OrdinalIgnoreCase));if(MessageBox.Show(this,"Record a new policy event and invalidate previous reviews for this jurisdiction?","Policy change",MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes)return;service.RecordJurisdictionPolicy(name,prior?.Id,(string)decision.SelectedItem,note.Text,confirm.IsChecked==true);note.Clear();confirm.IsChecked=false;Refresh();}
}
