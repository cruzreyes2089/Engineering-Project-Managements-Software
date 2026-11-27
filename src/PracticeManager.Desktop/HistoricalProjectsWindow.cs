using System.Windows;
using System.Windows.Controls;
using PracticeManager.Persistence;
namespace PracticeManager.Desktop;
internal sealed class HistoricalProjectsWindow:Window
{
 readonly LocalPractice service;
 readonly DataGrid grid=WorkWindowFields.Grid();
 readonly TextBox search=new(){Width=280,MaxLength=150,Margin=new Thickness(0,0,6,0)};
 readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,6,0,6)};
 public HistoricalProjectsWindow(LocalPractice service)
 {
  this.service=service;Title="Historical projects — read-only archive";Width=1150;Height=640;
  MinWidth=720;MinHeight=420;WindowStartupLocation=WindowStartupLocation.CenterOwner;ShowInTaskbar=false;
  var root=new DockPanel{Margin=new Thickness(16)};var head=new StackPanel();
  head.Children.Add(new TextBlock{Text="Historical projects",FontSize=24,FontWeight=FontWeights.SemiBold});
  head.Children.Add(new TextBlock{Text="Confidential legacy records only. Historical status and authorization labels are not current approvals. Archived records cannot authorize work, generate invoices, create project folders or become active projects.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,4,0,10)});
  var bar=new WrapPanel();
  bar.Children.Add(new TextBlock{Text="Search ID, project number, title, site or status",VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,8,0)});
  bar.Children.Add(search);WorkWindowFields.Button(bar,"Search / refresh",Refresh);WorkWindowFields.Button(bar,"Clear",()=>{search.Clear();Refresh();});if(service.Can("Administration.Manage")&&service.Can("Administration.HistoricalProjectImport")&&service.Can("HistoricalProjects.View")&&service.Can("Clients.View"))WorkWindowFields.Button(bar,"Validate legacy transfer",()=>{new HistoricalArchivePreviewWindow(service){Owner=this}.ShowDialog();Refresh();});WorkWindowFields.Button(bar,"Close",Close);
  search.KeyDown+=(_,e)=>{if(e.Key==System.Windows.Input.Key.Enter){e.Handled=true;Refresh();}};
  head.Children.Add(bar);head.Children.Add(status);DockPanel.SetDock(head,Dock.Top);root.Children.Add(head);
  WorkWindowFields.Columns(grid,("Legacy ID","LegacyId"),("Legacy project #","HistoricalNumber"),("Project title","Title"),("Site","Site"),("Legacy client ID","LegacyClientId"),("Native client ID","NativeClientId"),("Historical status","HistoricalStatus"),("Historical authorization label","HistoricalAuthorizationStatus"));
  grid.CanUserDeleteRows=false;grid.ClipboardCopyMode=DataGridClipboardCopyMode.None;
  root.Children.Add(grid);Content=root;
  Closed+=(_,_)=>{grid.ItemsSource=null;search.Clear();status.Text="";};
  Refresh();
 }
 void Refresh()
 {
  grid.ItemsSource=null;
  try
  {
   var all=service.ReadHistoricalProjects();var term=search.Text.Trim();
   bool Match(string value)=>value.Contains(term,StringComparison.OrdinalIgnoreCase);
   var visible=all.Where(x=>Match(x.LegacyId)||Match(x.LegacyClientId)||Match(x.HistoricalNumber)||Match(x.Title)||Match(x.Site)||Match(x.HistoricalStatus)||Match(x.HistoricalAuthorizationStatus)).ToArray();
   grid.ItemsSource=visible;status.Text=all.Count==0?"No sealed historical archive is available.":$"Showing {visible.Length:N0} of {all.Count:N0} archived records. Read only — no current work authorization.";
  }
  catch(UnauthorizedAccessException){status.Text="Access denied. Sign in again or request authorized access.";}
  catch(Exception){status.Text="Historical archive unavailable or integrity verification failed. No records displayed.";}
 }
}
