using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PracticeManager.Core;
using PracticeManager.Persistence;
namespace PracticeManager.Desktop;
internal sealed class ProvisionalCustomerLinkWindow : Window
{
 readonly LocalPractice service;
 readonly string revision,business;
 readonly int skip;
 readonly AccountingCustomer customer;
 readonly ComboBox clients=new(){DisplayMemberPath="Label",MinWidth=350,Margin=new Thickness(0,6,0,10)};
 readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,10,0,0)};
 readonly Button link=new(){Content="Save provisional link",Margin=new Thickness(0,6,0,0)};
 readonly Button unlink=new(){Content="Remove local link",Margin=new Thickness(0,6,0,0)};
 readonly Button refresh=new(){Content="Revalidate local links",Margin=new Thickness(0,6,0,0)};
 sealed record ClientChoice(long Id,string Label);
 long? linkedClient;
 CancellationTokenSource? pending;
 bool closed;
 public ProvisionalCustomerLinkWindow(LocalPractice service,string revision,string business,int skip,AccountingCustomer customer)
 {
  this.service=service;this.revision=revision;this.business=business;this.skip=skip;this.customer=customer;
  Title="Provisional customer link — synthetic test only";Width=550;SizeToContent=SizeToContent.Height;MaxHeight=SystemParameters.WorkArea.Height-40;WindowStartupLocation=WindowStartupLocation.CenterOwner;ShowInTaskbar=false;
  var layout=new StackPanel{Margin=new Thickness(20)};
  layout.Children.Add(new TextBlock{Text="Local customer mapping",FontSize=23,FontWeight=FontWeights.SemiBold});
  layout.Children.Add(new TextBlock{Text="Development only. Business identity is not verified. Links are scoped to this saved connection revision, cannot transfer after replacement, and cannot post invoices or change Manager records.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,12)});
  layout.Children.Add(new TextBlock{Text="Selected Manager customer: "+customer.Name,TextWrapping=TextWrapping.Wrap});
  layout.Children.Add(new TextBlock{Text="Select an active CRC client:"});
  layout.Children.Add(clients);layout.Children.Add(link);layout.Children.Add(unlink);layout.Children.Add(refresh);layout.Children.Add(status);
  var close=new Button{Content="Close",Margin=new Thickness(0,12,0,0)};close.Click+=(_,_)=>Close();layout.Children.Add(close);
  Content=new ScrollViewer{Content=layout,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
  link.IsEnabled=unlink.IsEnabled=false;
  link.Click+=async (_,_)=>await RunAsync("save");
  unlink.Click+=async (_,_)=>await RunAsync("remove");
  refresh.Click+=async (_,_)=>await RunAsync("read");
  clients.SelectionChanged+=(_,_)=>{link.IsEnabled=pending==null&&linkedClient==null&&clients.SelectedItem is ClientChoice&&refresh.IsEnabled;};
  Loaded+=async (_,_)=>await RunAsync("read");
  Closed+=(_,_)=>{closed=true;pending?.Cancel();clients.ItemsSource=null;};
 }
 async Task RunAsync(string mode)
 {
  if(pending!=null||closed)return;
  using var cts=new CancellationTokenSource();pending=cts;
  var chosen=clients.SelectedItem as ClientChoice;
  link.IsEnabled=unlink.IsEnabled=refresh.IsEnabled=clients.IsEnabled=false;
  status.Foreground=Brushes.DimGray;status.Text="Revalidating connection...";
  var ready=false;string message="";
  try{
   if(mode=="save"&&chosen!=null){
    var r=await service.SaveProvisionalLinkAsync(revision,business,skip,chosen.Id,customer.Key,cts.Token);message=r.Outcome;
   }else if(mode=="remove"&&linkedClient is long id){
    service.RemoveProvisionalLink(revision,id,customer.Key);message="Removed";
   }
   var result=await service.ReadProvisionalLinksAsync(revision,business,skip,cts.Token);
   if(closed)return;
   if(result.Outcome!="Loaded"||result.Links==null){linkedClient=null;clients.ItemsSource=null;status.Foreground=Brushes.DarkRed;status.Text="Unable to validate links: "+result.Outcome;return;}
   var rows=result.Links;linkedClient=rows.SingleOrDefault(x=>x.CustomerKey==customer.Key)?.ClientId;
   var active=service.SearchClients("").Where(x=>x.Status=="Active").Select(x=>new ClientChoice(x.Id,x.Number+" — "+(string.IsNullOrWhiteSpace(x.Company)?(x.First+" "+x.Last).Trim():x.Company))).ToList();
   clients.ItemsSource=active;
   clients.SelectedItem=active.SingleOrDefault(x=>x.Id==(linkedClient??chosen?.Id));
   status.Foreground=Brushes.DimGray;
   status.Text=(message.Length==0?"":message+". ")+(linkedClient.HasValue?"Selected customer has a provisional local link.":"Selected customer is not linked.")+" No accounting writes. Total provisional links on this connection: "+rows.Count;
   ready=true;
  }catch(UnauthorizedAccessException){if(!closed)status.Text="Access denied. Sign in again.";}
  catch(Exception){if(!closed)status.Text="Operation failed. Revalidate before continuing.";}
  finally{pending=null;if(!closed){refresh.IsEnabled=true;clients.IsEnabled=ready;link.IsEnabled=ready&&linkedClient==null&&clients.SelectedItem is ClientChoice;unlink.IsEnabled=ready&&linkedClient.HasValue;}}
 }
}
