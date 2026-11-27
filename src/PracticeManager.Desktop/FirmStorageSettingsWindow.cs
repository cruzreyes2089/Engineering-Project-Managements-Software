using System.Windows;
using System.Windows.Controls;
using PracticeManager.Persistence;
namespace PracticeManager.Desktop;

internal sealed class FirmStorageSettingsWindow:Window
{
 readonly LocalPractice service;
 readonly TextBox production=new(){MaxLength=180},restricted=new(){MaxLength=180};
 readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,0)};
 long? revision;
 public FirmStorageSettingsWindow(LocalPractice service)
 {
  this.service=service;Title="Business storage preferences";Width=680;Height=430;MinWidth=480;MinHeight=360;
  WindowStartupLocation=WindowStartupLocation.CenterOwner;ShowInTaskbar=false;
  var panel=new StackPanel{Margin=new Thickness(16)};
  panel.Children.Add(new TextBlock{Text="Business storage preferences",FontSize=22,FontWeight=FontWeights.SemiBold});
  panel.Children.Add(new TextBlock{Text="Each business database has its own proposed locations. Changing a preference will not move or rename existing folders, approve storage, or create directories. Windows security verification is still required.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,6,0,12)});
  panel.Children.Add(new TextBlock{Text="Production root"});panel.Children.Add(production);
  panel.Children.Add(new TextBlock{Text="Restricted administration root",Margin=new Thickness(0,8,0,0)});panel.Children.Add(restricted);
  var bar=new WrapPanel{Margin=new Thickness(0,10,0,0)};
  WorkWindowFields.Button(bar,"Save preferences",Save);WorkWindowFields.Button(bar,"Reload",Load);WorkWindowFields.Button(bar,"Close",Close);
  panel.Children.Add(bar);panel.Children.Add(status);Content=new ScrollViewer{Content=panel};
  Closed+=(_,_)=>{production.Clear();restricted.Clear();revision=null;};Load();
 }
 void Clear(){revision=null;production.Clear();restricted.Clear();}
 void Load()
 {
  Clear();status.Text="";
  try
  {
   var saved=service.ReadFirmStoragePreference();revision=saved.Revision;
   production.Text=saved.ProductionRoot;restricted.Text=saved.RestrictedRoot;
   status.Text=saved.Configured?$"Saved preference revision {saved.Revision}. Not activated.":"No saved storage preferences.";
  }
  catch(UnauthorizedAccessException){status.Text="Access denied.";}
  catch(Exception){status.Text="Storage preferences unavailable.";}
 }
 void Save()
 {
  try
  {
   if(revision is not long expected)throw new ArgumentException("Reload preferences first.");
   if(MessageBox.Show(this,"Save these proposed locations for this business only? Existing files will not be moved. Storage remains unverified and inactive.","Save preferences",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;
   var saved=service.SaveFirmStoragePreference(production.Text,restricted.Text,expected,true);
   revision=saved.Revision;production.Text=saved.ProductionRoot;restricted.Text=saved.RestrictedRoot;
   status.Text=$"Saved preference revision {saved.Revision}. Not activated.";
  }
  catch(UnauthorizedAccessException){Clear();status.Text="Access denied.";}
  catch(ArgumentException e){status.Text=e.Message;}
  catch(Exception){status.Text="Save unavailable; reload before retrying.";}
 }
}
