using System.Windows;
using System.Windows.Controls;
using PracticeManager.Persistence;
namespace PracticeManager.Desktop;

internal sealed class ProjectStorageDraftWindow:Window
{
 readonly LocalPractice service;readonly long project;readonly bool correspondence;
 readonly TextBox production=new(){MaxLength=180},restricted=new(){MaxLength=180};
 readonly TextBlock result=new(){TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,12,0,0)};
 public ProjectStorageDraftWindow(LocalPractice service,long project,bool correspondence)
 {
  this.service=service;this.project=project;this.correspondence=correspondence;
  Title="Storage location draft — not registered";Width=650;Height=440;MinWidth=480;MinHeight=360;
  WindowStartupLocation=WindowStartupLocation.CenterOwner;ShowInTaskbar=false;
  var panel=new StackPanel{Margin=new Thickness(16)};
  panel.Children.Add(new TextBlock{Text="Review proposed storage roots",FontSize=22,FontWeight=FontWeights.SemiBold});
  panel.Children.Add(new TextBlock{Text="Enter two separate dedicated Windows directory paths. This checks path syntax and the proposed project layout only. It does not access the filesystem, save settings, approve security or create folders.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,6,0,12)});
  panel.Children.Add(new TextBlock{Text="Production root"});panel.Children.Add(production);
  panel.Children.Add(new TextBlock{Text="Restricted administration root",Margin=new Thickness(0,8,0,0)});panel.Children.Add(restricted);
  var bar=new WrapPanel{Margin=new Thickness(0,10,0,0)};
  WorkWindowFields.Button(bar,"Review draft",Review);WorkWindowFields.Button(bar,"Close",Close);
  panel.Children.Add(bar);panel.Children.Add(result);
  production.TextChanged+=(_,_)=>result.Text="";restricted.TextChanged+=(_,_)=>result.Text="";
  Closed+=(_,_)=>{production.Clear();restricted.Clear();result.Text="";};
  Content=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
 }
 void Review()
 {
  result.Text="";
  try
  {
   var draft=service.ReviewProjectStorageDraft(project,production.Text,restricted.Text,correspondence);
   result.Text=$"Project {draft.ProjectNumber} — {draft.TemplateRevision}\n"+
    $"Production: {draft.ProductionDirectories} planned directories\nRestricted: {draft.RestrictedDirectories} planned directories\n"+
    "Path syntax accepted. Not registered. Windows directory identity and effective access remain unverified.";
  }
  catch(UnauthorizedAccessException){production.Clear();restricted.Clear();result.Text="Access denied.";}
  catch(ArgumentException e){result.Text=e.Message;}
  catch(Exception){result.Text="Storage draft unavailable. No changes were made.";}
 }
}
