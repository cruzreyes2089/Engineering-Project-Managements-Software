using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Globalization;
using PracticeManager.Persistence;
namespace PracticeManager.Desktop;
internal sealed class AccountingFinancialWindow:Window
{
 readonly LocalPractice service;
 readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,10,0,8)};
 readonly TextBox report=new(){IsReadOnly=true,TextWrapping=TextWrapping.Wrap,AcceptsReturn=true,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Height=240};
 readonly Button run=new(){Content="Run financial preview",Padding=new Thickness(12,8,12,8),Margin=new Thickness(0,10,0,8)};
 CancellationTokenSource? pending;bool closed;
 public AccountingFinancialWindow(LocalPractice service)
 {
  this.service=service;Title="Financial preview — synthetic test only";Width=650;SizeToContent=SizeToContent.Height;MaxHeight=SystemParameters.WorkArea.Height-40;WindowStartupLocation=WindowStartupLocation.CenterOwner;ShowInTaskbar=false;
  var panel=new StackPanel{Margin=new Thickness(20)};
  panel.Children.Add(new TextBlock{Text="Financial preview",FontSize=23,FontWeight=FontWeights.SemiBold});
  panel.Children.Add(new TextBlock{Text="Use only the synthetic test business. No accounting writes or imports. Requires the enrolled marker. Reads complete first pages (maximum 20 records per resource), unspecified currency only. Results are sequential list totals, not an atomic ledger snapshot, bank balance, profit calculation or completed reconciliation.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,0)});
  panel.Children.Add(run);panel.Children.Add(status);panel.Children.Add(report);
  var close=new Button{Content="Close",Margin=new Thickness(0,12,0,0)};close.Click+=(_,_)=>Close();panel.Children.Add(close);Content=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
  run.Click+=async (_,_)=>await ReadAsync();
  Closed+=(_,_)=>{closed=true;pending?.Cancel();report.Clear();};
 }
 static string N(decimal n)=>n.ToString("0.00####",CultureInfo.InvariantCulture);
 async Task ReadAsync()
 {
  if(pending!=null)return;
  using var cts=new CancellationTokenSource();pending=cts;run.IsEnabled=false;report.Clear();status.Foreground=Brushes.DimGray;status.Text="Checking enrolled marker and financial registers...";
  try{
   var r=await service.ReadFinancialPreviewAsync(cts.Token);if(closed)return;
   var p=r.Preview;
   if(r.Outcome!="Connected"||p==null){status.Foreground=Brushes.DarkRed;status.Text="Preview blocked: "+r.Outcome+". No totals displayed.";return;}
   status.Foreground=p.MatchesFixture?Brushes.DarkGreen:Brushes.DarkOrange;status.Text=p.MatchesFixture?"Synthetic benchmark matched.":"Synthetic benchmark differs. Review the test business.";
   report.Text=$"Marker verified before and after reads\nInvoices: {p.InvoiceCount} | Receipts: {p.ReceiptCount} | Payments: {p.PaymentCount}\nInvoice total: {N(p.Invoiced)}\nOutstanding invoice balances: {N(p.Outstanding)}\nReceipt total: {N(p.Receipts)}\nPayment total: {N(p.Payments)}\nReceipt less payment: {N(p.NetReceiptLessPayment)}\nCurrency: unspecified (not verified USD)\nSynthetic fixture: "+(p.MatchesFixture?"MATCH":"DIFFERS")+"\nList totals only; no ledger reconciliation or accounting writes.";
  }catch(UnauthorizedAccessException){if(!closed)status.Text="Access denied.";}
  catch(Exception){if(!closed){report.Clear();status.Text="Financial preview failed. No totals assumed.";}}
  finally{pending=null;if(!closed)run.IsEnabled=true;}
 }
}
