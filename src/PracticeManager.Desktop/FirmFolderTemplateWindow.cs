using System.Windows;
using System.Windows.Controls;
using PracticeManager.Core;
using PracticeManager.Persistence;
namespace PracticeManager.Desktop;

internal sealed class FirmFolderTemplateWindow:Window
{
 readonly LocalPractice service;
 readonly TextBox names=new(){AcceptsReturn=true,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,MinHeight=220,MaxLength=4000};
 readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,0)};
 long? revision;
 public FirmFolderTemplateWindow(LocalPractice service)
 {
  this.service=service;Title="Business folder template";Width=660;Height=580;MinWidth=480;MinHeight=400;
  WindowStartupLocation=WindowStartupLocation.CenterOwner;ShowInTaskbar=false;
  var panel=new StackPanel{Margin=new Thickness(16)};
  panel.Children.Add(new TextBlock{Text="Production folder template",FontSize=22,FontWeight=FontWeights.SemiBold});
  panel.Children.Add(new TextBlock{Text="Enter one production folder per line in the desired order. Add, remove or rename labels. Correspondence remains optional per project. Restricted administration is managed separately and is not editable here. No actual directories will be changed.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,6,0,12)});
  panel.Children.Add(names);
  var bar=new WrapPanel{Margin=new Thickness(0,10,0,0)};
  WorkWindowFields.Button(bar,"Save template",Save);
  WorkWindowFields.Button(bar,"Load standard preset",()=>names.Text=string.Join(Environment.NewLine,ProjectFolderLayout.Default().Names));
  WorkWindowFields.Button(bar,"Reload",Load);WorkWindowFields.Button(bar,"Close",Close);
  panel.Children.Add(bar);panel.Children.Add(status);
  names.TextChanged+=(_,_)=>status.Text="Unsaved template draft.";
  Closed+=(_,_)=>{names.Clear();revision=null;status.Text="";};
  Content=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};Load();
 }
 void Load()
 {
  revision=null;names.Clear();
  try
  {
   var current=service.ReadFirmFolderLayout();revision=current.RevisionId;
   names.Text=string.Join(Environment.NewLine,current.Names);status.Text=current.Revision+" — preview only";
  }
  catch(UnauthorizedAccessException){status.Text="Access denied.";}
  catch(Exception){status.Text="Template unavailable.";}
 }
 void Save()
 {
  try
  {
   if(revision is not long expected)throw new ArgumentException("Reload the template first.");
   var proposed=names.Text.Replace("\r\n","\n").Split('\n',StringSplitOptions.RemoveEmptyEntries);
   if(MessageBox.Show(this,"Save this business's production folder template? Existing files will not be moved, renamed or deleted.","Save template",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;
   var saved=service.SaveFirmFolderLayout(proposed,expected,true);revision=saved.RevisionId;
   names.Text=string.Join(Environment.NewLine,saved.Names);status.Text=saved.Revision+" — saved; physical provisioning disabled.";
  }
  catch(UnauthorizedAccessException){revision=null;names.Clear();status.Text="Access denied.";}
  catch(ArgumentException e){status.Text=e.Message;}
  catch(Exception){status.Text="Save unavailable; reload before retrying.";}
 }
}
