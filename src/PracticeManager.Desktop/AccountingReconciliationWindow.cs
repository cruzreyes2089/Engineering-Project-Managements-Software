using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using PracticeManager.Core;
using PracticeManager.Persistence;
namespace PracticeManager.Desktop;
internal sealed class AccountingReconciliationWindow:Window
{
 readonly LocalPractice service;
 readonly string[] labels={"Accounts receivable","Cash","Income","Expenses","Assets","Liabilities","Equity","Net profit"};
 readonly TextBox[] inputs=new TextBox[8];
 readonly CheckBox confirmed=new(){Content="I independently checked the current test summary, single currency and zero opening balances.",Margin=new Thickness(0,12,0,8)};
 readonly Button run=new(){Content="Run read-only reconciliation",Padding=new Thickness(12,8,12,8),Margin=new Thickness(0,8,0,8)};
 readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,8)};
 CancellationTokenSource? pending;bool closed;
 public AccountingReconciliationWindow(LocalPractice service)
 {
  this.service=service;Title="Synthetic accounting reconciliation — read-only";Width=650;SizeToContent=SizeToContent.Height;MaxHeight=SystemParameters.WorkArea.Height-40;WindowStartupLocation=WindowStartupLocation.CenterOwner;ShowInTaskbar=false;
  var panel=new StackPanel{Margin=new Thickness(20)};
  panel.Children.Add(new TextBlock{Text="Independent accounting reconciliation",FontSize=23,FontWeight=FontWeights.SemiBold});
  panel.Children.Add(new TextBlock{Text="Development only: CRC ACCOUNTING TEST. Enter the current amounts directly from the Manager summary, with no currency symbols or separators. Use a decimal point. No amounts are prefilled. Keep Manager unchanged while checking. Two matching API reads are not an atomic snapshot.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,12)});
  var grid=new Grid();grid.ColumnDefinitions.Add(new ColumnDefinition());grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(160)});
  for(int i=0;i<labels.Length;i++){
   grid.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
   var label=new TextBlock{Text=labels[i],VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,4,12,4)};
   var field=new TextBox{MaxLength=16,Margin=new Thickness(0,4,0,4)};inputs[i]=field;
   Grid.SetRow(label,i);Grid.SetRow(field,i);Grid.SetColumn(field,1);grid.Children.Add(label);grid.Children.Add(field);
  }
  panel.Children.Add(grid);panel.Children.Add(confirmed);panel.Children.Add(run);panel.Children.Add(status);
  var close=new Button{Content="Close",Margin=new Thickness(0,12,0,0)};close.Click+=(_,_)=>Close();panel.Children.Add(close);
  Content=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
  run.Click+=async (_,_)=>await Run();Closed+=(_,_)=>{closed=true;pending?.Cancel();foreach(var f in inputs)f.Clear();status.Text="";};
 }
 async Task Run()
 {
  if(pending!=null)return;
  status.Text="";
  if(confirmed.IsChecked!=true){status.Text="Confirm independent evidence first.";return;}
  var a=new decimal[8];
  for(int i=0;i<a.Length;i++)
   if(!decimal.TryParse(inputs[i].Text,NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out a[i])||a[i]<0||a[i]>999999999999.99m||decimal.Round(a[i],2)!=a[i]){
    status.Text="Enter a valid nonnegative amount with at most two decimal places for "+labels[i]+".";return;
   }
  using var cts=new CancellationTokenSource();pending=cts;
  run.IsEnabled=confirmed.IsEnabled=false;foreach(var f in inputs)f.IsEnabled=false;
  status.Text="Checking two observations of the synthetic records...";
  try{
   var result=await service.ReconcileSavedSyntheticAsync(new(new(a[0],a[1],a[2],a[3],a[4],a[5],a[6],a[7]),true),cts.Token);
   if(closed)return;
   status.Text=result.Matches?"Synthetic comparison matched the independently entered summary. No records changed. Not production or full-ledger assurance.":result.Outcome+": "+string.Join(", ",result.Differences)+". No records changed.";
  }catch(UnauthorizedAccessException){if(!closed)status.Text="Access denied.";}
   catch(Exception){if(!closed)status.Text="Reconciliation failed. No success assumed.";}
  finally{pending=null;if(!closed){run.IsEnabled=confirmed.IsEnabled=true;foreach(var f in inputs)f.IsEnabled=true;confirmed.IsChecked=false;}}
 }
}
