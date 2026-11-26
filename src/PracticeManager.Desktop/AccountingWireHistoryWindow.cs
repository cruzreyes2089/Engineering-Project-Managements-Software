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
  var compare=new Button{Content="Compare witness evidence",Margin=new Thickness(8,0,0,0)};compare.Click+=(_,_)=>CompareWitness();
  buttons.Children.Add(refresh);buttons.Children.Add(next);buttons.Children.Add(compare);buttons.Children.Add(close);DockPanel.SetDock(buttons,Dock.Bottom);root.Children.Add(buttons);
  void Column(string title,string property)=>grid.Columns.Add(new DataGridTextColumn{Header=title,Binding=new Binding(property),Width=DataGridLength.Auto});
  Column("Operation reference","Reference");Column("Type","Kind");Column("Draft state","DraftState");Column("Attempt state","AttemptState");Column("Reason","Reason");Column("Created UTC","CreatedUtc");Column("Attempt updated UTC","AttemptUpdatedUtc");Column("Recorded assessment","AssessmentEvidence");Column("Simulated HTTP status","ObservedStatus");Column("Recovery finding","RecoveryFinding");
  root.Children.Add(grid);Content=root;
  pending.Checked+=(_,_)=>LoadPage(null);pending.Unchecked+=(_,_)=>LoadPage(null);LoadPage(null);
 }
 void CompareWitness()
 {
  var w=new Window{Owner=this,Title="Synthetic witness comparison — read only",Width=640,Height=330,WindowStartupLocation=WindowStartupLocation.CenterOwner};
  var p=new StackPanel{Margin=new Thickness(16)};p.Children.Add(new TextBlock{Text="Select the original retained synthetic witness directory and enter its independently recorded store identifier. This is comparison only; it cannot establish a trusted store binding, release a hold, or authorize posting.",TextWrapping=TextWrapping.Wrap});
  p.Children.Add(new TextBlock{Text="Original witness store GUID"});var key=new TextBox{MaxLength=36};p.Children.Add(key);
  var result=new TextBlock{TextWrapping=TextWrapping.Wrap};var b=new Button{Content="Select existing directory and compare"};p.Children.Add(b);p.Children.Add(result);w.Content=p;
  b.Click+=(_,_)=>{result.Text="";try{if(!Guid.TryParseExact(key.Text.Trim(),"D",out var id)||id==Guid.Empty)throw new ArgumentException("Enter the original store GUID.");
   var picker=new Microsoft.Win32.OpenFolderDialog{Title="Original retained synthetic witness directory",Multiselect=false};if(picker.ShowDialog(w)!=true)return;
   var r=service.CompareSyntheticAttemptWitness(picker.FolderName,id);result.Text=$"{r.Outcome}\nExpected: {r.Expected}; exact: {r.Exact}; missing: {r.Missing}\nConflicting: {r.Conflicting}; unexpected: {r.Orphaned}; unresolved: {r.Unresolved}\nCounts on blocked inspections may be incomplete. No send or hold-release authority.";
  }catch(UnauthorizedAccessException){result.Text="Access denied.";}catch(ArgumentException e){result.Text=e.Message;}catch(Exception){result.Text="Witness comparison unavailable.";}};w.ShowDialog();
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
