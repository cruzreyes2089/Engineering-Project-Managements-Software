using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using PracticeManager.Persistence;
namespace PracticeManager.Desktop;
internal sealed class AccountingWireHistoryWindow:Window
{
 readonly LocalPractice service;
 readonly DataGrid grid=new(){IsReadOnly=true,AutoGenerateColumns=false,CanUserAddRows=false,CanUserDeleteRows=false};
 readonly CheckBox pending=new(){Content="Only unresolved attempts",IsChecked=true,Margin=new Thickness(0,8,0,8)};
 readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,6,0,6)};
 readonly Button next=new(){Content="Next page",Margin=new Thickness(8,0,0,0)};
 long? cursor;
 public AccountingWireHistoryWindow(LocalPractice service)
 {
  this.service=service;Title="Synthetic transaction history — no posting";Width=960;Height=530;WindowStartupLocation=WindowStartupLocation.CenterOwner;ShowInTaskbar=false;
  var root=new DockPanel{Margin=new Thickness(16)};
  var header=new StackPanel();
  header.Children.Add(new TextBlock{Text="Synthetic transaction history",FontSize=22,FontWeight=FontWeights.SemiBold});
  header.Children.Add(new TextBlock{Text="Local rehearsal records only. No real posting, cancellation, retry, or hold release is available here. Missing assessment evidence is not a verified result. Refresh before review; pages are not a locked snapshot.",TextWrapping=TextWrapping.Wrap});
  header.Children.Add(pending);header.Children.Add(status);
  DockPanel.SetDock(header,Dock.Top);root.Children.Add(header);
  var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,10,0,0)};
  var refresh=new Button{Content="Refresh / first page"};refresh.Click+=(_,_)=>LoadPage(null);
  next.Click+=(_,_)=>{if(cursor.HasValue)LoadPage(cursor);};
  var close=new Button{Content="Close",Margin=new Thickness(8,0,0,0)};close.Click+=(_,_)=>Close();
  buttons.Children.Add(refresh);buttons.Children.Add(next);buttons.Children.Add(close);DockPanel.SetDock(buttons,Dock.Bottom);root.Children.Add(buttons);
  void Column(string title,string property)=>grid.Columns.Add(new DataGridTextColumn{Header=title,Binding=new Binding(property),Width=DataGridLength.Auto});
  Column("Operation reference","Reference");Column("Type","Kind");Column("Draft state","DraftState");Column("Attempt state","AttemptState");Column("Reason","Reason");Column("Created UTC","CreatedUtc");Column("Attempt updated UTC","AttemptUpdatedUtc");Column("Recorded assessment","AssessmentEvidence");Column("Simulated HTTP status","ObservedStatus");Column("Recovery finding","RecoveryFinding");
  root.Children.Add(grid);Content=root;
  pending.Checked+=(_,_)=>LoadPage(null);pending.Unchecked+=(_,_)=>LoadPage(null);LoadPage(null);
 }
 void LoadPage(long? before)
 {
  grid.ItemsSource=null;cursor=null;next.IsEnabled=false;
  try{
   var page=service.ReadSyntheticWireHistory(pending.IsChecked==true,before);
   grid.ItemsSource=page.Items;cursor=page.NextBeforeId;next.IsEnabled=cursor.HasValue;
   status.Text=$"{page.Items.Count} rows on this page. "+(cursor.HasValue?"More history available.":"End of current results.");
  }catch(UnauthorizedAccessException){status.Text="Access denied.";}
   catch(Exception){status.Text="History unavailable. No authorization assumed.";}
 }
}
