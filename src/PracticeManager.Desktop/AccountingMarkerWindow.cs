using System.Windows;
using System.Windows.Controls;
using PracticeManager.Persistence;
using PracticeManager.Core;
namespace PracticeManager.Desktop;
internal sealed class AccountingMarkerWindow:Window
{
 readonly LocalPractice service;
 readonly TextBlock state=new(){TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,10,0,10)};
 readonly TextBox report=new(){IsReadOnly=true,TextWrapping=TextWrapping.Wrap,AcceptsReturn=true,Height=100};
 readonly Button verify=new(){Margin=new Thickness(0,8,0,8)};
 CancellationTokenSource? pending;bool closed;
 public AccountingMarkerWindow(LocalPractice service)
 {
  this.service=service;Title="Accounting business marker — synthetic test only";Width=600;SizeToContent=SizeToContent.Height;WindowStartupLocation=WindowStartupLocation.CenterOwner;ShowInTaskbar=false;
  var layout=new StackPanel{Margin=new Thickness(20)};
  layout.Children.Add(new TextBlock{Text="Accounting business marker",FontSize=23,FontWeight=FontWeights.SemiBold});
  layout.Children.Add(new TextBlock{Text="Existing test custom field: "+BusinessMarker.TestName+"\nEnroll only in the synthetic Manager business. CRC records the definition UUID locally and checks it before saved reads and replacement connections. Do not delete or rename the field. A cloned or restored business can retain the same marker; production posting is not enabled.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,0)});
  layout.Children.Add(state);layout.Children.Add(verify);layout.Children.Add(report);
  var close=new Button{Content="Close",Margin=new Thickness(0,10,0,0)};close.Click+=(_,_)=>Close();layout.Children.Add(close);Content=layout;
  verify.Click+=async (_,_)=>await RunAsync();Closed+=(_,_)=>{closed=true;pending?.Cancel();};Refresh();
 }
 void Refresh(){var enrolled=service.ReadBusinessMarker().Enrolled;state.Text=enrolled?"Marker enrolled locally. Verify to check the saved business.":"Marker not enrolled.";verify.Content=enrolled?"Verify enrolled marker":"Enroll test marker";verify.IsEnabled=pending==null;}
 async Task RunAsync(){
  if(pending!=null)return;
  using var cts=new CancellationTokenSource();pending=cts;verify.IsEnabled=false;
  try{
   var enrolled=service.ReadBusinessMarker().Enrolled;
   if(!enrolled&&MessageBox.Show(this,"Enroll the marker in the currently connected synthetic business? The UUID cannot be replaced automatically.","Confirm enrollment",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;
   report.Text="Checking saved connection...";
   var r=await service.EnrollOrVerifyMarkerAsync(!enrolled,cts.Token);if(closed)return;
   report.Text=r.Success?"HTTP 200 — exact marker UUID matched. Enrollment retained locally. No Manager writes. Clone/restore identity is not certified.":"Marker check failed: "+r.Outcome+". No identity change.";
  }catch(UnauthorizedAccessException){if(!closed)report.Text="Access denied.";}catch(Exception){if(!closed)report.Text="Marker check failed. No success assumed.";}
  finally{pending=null;if(!closed){try{Refresh();}catch{verify.IsEnabled=false;}}}
 }
}
