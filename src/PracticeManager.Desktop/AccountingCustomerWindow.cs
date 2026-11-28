using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using PracticeManager.Core;
using PracticeManager.Persistence;
namespace PracticeManager.Desktop;

internal sealed class AccountingCustomerWindow : Window
{
 readonly LocalPractice service;
 readonly DataGrid grid=new(){AutoGenerateColumns=false,IsReadOnly=true,CanUserAddRows=false,CanUserDeleteRows=false,CanUserSortColumns=false,ClipboardCopyMode=DataGridClipboardCopyMode.None,Margin=new Thickness(0,8,0,8)};
 readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,0)};
 readonly TextBlock business=new(){TextWrapping=TextWrapping.Wrap,Foreground=Brushes.DimGray};
 readonly TextBox evidence=new(){IsReadOnly=true,TextWrapping=TextWrapping.Wrap,AcceptsReturn=true,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Height=120,Margin=new Thickness(0,8,0,0)};
 readonly Button previous=new(){Content="Previous page",Padding=new Thickness(12,6,12,6),Margin=new Thickness(0,0,8,0)};
 readonly Button refresh=new(){Content="Refresh page",Padding=new Thickness(12,6,12,6),Margin=new Thickness(0,0,8,0)};
 readonly Button next=new(){Content="Next page",Padding=new Thickness(12,6,12,6)};
 readonly Button map=new(){Content="Manage provisional link",Padding=new Thickness(12,6,12,6),Margin=new Thickness(12,0,0,0)};
 AccountingCustomerPage? current;
 string? revision;
 CancellationTokenSource? pending;
 bool closed;
 int requested;
 public AccountingCustomerWindow(LocalPractice service)
 {
  this.service=service;Title="Manager.io customer register — read-only development preview";Width=880;Height=620;MinWidth=580;MinHeight=430;WindowStartupLocation=WindowStartupLocation.CenterOwner;ShowInTaskbar=false;Background=Brushes.White;
  var layout=new DockPanel{Margin=new Thickness(20)};
  var top=new StackPanel();
  top.Children.Add(new TextBlock{Text="Accounting customers",FontSize=23,FontWeight=FontWeights.SemiBold});
  top.Children.Add(new TextBlock{Text="Synthetic test business only. Provisional local links only; no accounting writes. Names and IDs are shown locally; share only the sanitized report.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,6,0,10)});
  top.Children.Add(business);top.Children.Add(status);
  DockPanel.SetDock(top,Dock.Top);layout.Children.Add(top);
  var bottom=new StackPanel();
  var nav=new StackPanel{Orientation=Orientation.Horizontal};nav.Children.Add(previous);nav.Children.Add(refresh);nav.Children.Add(next);nav.Children.Add(map);
  var close=new Button{Content="Close",Padding=new Thickness(12,6,12,6),Margin=new Thickness(12,0,0,0)};close.Click+=(_,_)=>Close();nav.Children.Add(close);
  bottom.Children.Add(nav);bottom.Children.Add(new TextBlock{Text="Sanitized verification report (no names or identifiers)",FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,12,0,0)});bottom.Children.Add(evidence);
  DockPanel.SetDock(bottom,Dock.Bottom);layout.Children.Add(bottom);
  grid.Columns.Add(new DataGridTextColumn{Header="Customer name",Binding=new Binding("Name"),Width=new DataGridLength(2,DataGridLengthUnitType.Star)});
  grid.Columns.Add(new DataGridTextColumn{Header="Status",Binding=new Binding("Status"),Width=new DataGridLength(1,DataGridLengthUnitType.Star)});
  grid.Columns.Add(new DataGridTextColumn{Header="Manager customer UUID",Binding=new Binding("Key"),Width=new DataGridLength(2,DataGridLengthUnitType.Star)});
  layout.Children.Add(grid);Content=layout;
  previous.IsEnabled=next.IsEnabled=map.IsEnabled=false;
  grid.SelectionChanged+=(_,_)=>UpdateMappingButton();
  map.Click+=(_,_)=>{if(current?.BusinessName is string name&&revision is string rev&&grid.SelectedItem is AccountingCustomer customer&&service.CanManageCustomerLinks())new ProvisionalCustomerLinkWindow(service,rev,name,current.Skip,customer){Owner=this}.ShowDialog();};
  refresh.Click+=async (_,_)=>await LoadAsync(current?.Skip??requested);
  previous.Click+=async (_,_)=>{if(current!=null)await LoadAsync(Math.Max(0,current.Skip-current.PageSize));};
  next.Click+=async (_,_)=>{if(current!=null&&((long)current.Skip+current.PageSize)<current.TotalRecords)await LoadAsync(current.Skip+current.PageSize);};
  Loaded+=async (_,_)=>await LoadAsync(0);
  Closed+=(_,_)=>{closed=true;pending?.Cancel();grid.ItemsSource=null;current=null;evidence.Clear();};
 }
 async Task LoadAsync(int skip)
 {
  if(pending!=null||closed)return;
  using var cts=new CancellationTokenSource();pending=cts;requested=skip;
  var prior=current;current=null;revision=null;map.IsEnabled=false;grid.ItemsSource=null;business.Text="";evidence.Clear();status.Foreground=Brushes.DimGray;status.Text="Retrieving customer page...";
  previous.IsEnabled=next.IsEnabled=refresh.IsEnabled=false;
  try{
   var r=await service.ReadSavedCustomersAsync(skip,cts.Token);
   if(closed)return;
   if(!r.Success||r.CustomerPage==null){
    status.Foreground=Brushes.DarkRed;
    status.Text=r.Outcome switch{"InvalidCustomerPage"=>"Customer page did not match the verified schema or pagination. No rows displayed.","Unauthorized"=>"Manager authentication failed. Check saved connection.","NotConfigured"=>"No saved connection.","CredentialUnavailable"=>"Saved credential unavailable.","ProfileChanged"=>"Saved connection changed. Reopen the register.",_=>"Customer read failed: "+r.Outcome};
    evidence.Text="Read failed. No records imported.";return;
   }
   var p=r.CustomerPage;current=p;revision=r.ProfileRevision;grid.ItemsSource=p.Customers;
   business.Text=p.BusinessName==null?"Business name unavailable.":("Business: "+p.BusinessName);
   status.Foreground=Brushes.DarkGreen;status.Text=$"HTTP 200 — {p.Customers.Count} rows on this page; {p.TotalRecords} total. No records imported.";
   var repeat=prior!=null&&prior.Skip==p.Skip;
   bool sameIds=repeat&&prior!.Customers.Select(x=>x.Key).ToHashSet().SetEquals(p.Customers.Select(x=>x.Key));
   bool? sameBusiness=repeat&&prior!.BusinessKey.HasValue&&p.BusinessKey.HasValue?prior.BusinessKey==p.BusinessKey:null;
   var identity= p.BusinessKey.HasValue?"UUID-shaped business key observed (not yet bound).":"No UUID-shaped business.key observed; identity unverified.";
   evidence.Text=$"HTTP 200 | Rows: {p.Customers.Count} | Skip: {p.Skip} | Page size: {p.PageSize} | Total: {p.TotalRecords}\nUnique customer UUIDs on page: {p.Customers.Select(x=>x.Key).Distinct().Count()}\nRepeated page UUIDs: "+(repeat?(sameIds?"Same set":"Changed set"):"Refresh this page to compare.")+"\nBusiness key comparison: "+(sameBusiness.HasValue?(sameBusiness.Value?"Same":"Changed"):"Not established")+"\n"+identity+"\nBusiness schema:\n"+p.BusinessSchema;
  }catch(UnauthorizedAccessException){if(!closed){status.Foreground=Brushes.DarkRed;status.Text="Access denied. Sign in again.";}}
  catch(Exception){if(!closed){status.Foreground=Brushes.DarkRed;status.Text="Customer read failed. No records displayed.";}}
  finally{pending=null;if(!closed){refresh.IsEnabled=true;previous.IsEnabled=current!=null&&current.Skip>0;next.IsEnabled=current!=null&&(long)current.Skip+current.PageSize<current.TotalRecords;UpdateMappingButton();}}
 }
 void UpdateMappingButton()
 {
  try{map.IsEnabled=pending==null&&current!=null&&!string.IsNullOrWhiteSpace(current.BusinessName)&&revision!=null&&grid.SelectedItem is AccountingCustomer&&service.CanManageCustomerLinks();}
  catch{map.IsEnabled=false;}
 }
}
