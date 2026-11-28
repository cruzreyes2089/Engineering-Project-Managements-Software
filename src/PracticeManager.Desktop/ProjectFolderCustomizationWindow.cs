using System.Windows;
using System.Windows.Controls;
using PracticeManager.Persistence;
namespace PracticeManager.Desktop;

internal sealed class ProjectFolderCustomizationWindow:Window
{
 readonly LocalPractice service;readonly long project;
 readonly TextBox paths=new(){AcceptsReturn=true,MinHeight=230,MaxLength=12000,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
 readonly CheckBox correspondence=new(){Content="Include optional Correspondence"};
 readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap};
 long? expected;
 readonly Button restore=new(){Content="Restore original template",Margin=new Thickness(2),IsEnabled=false};
 public ProjectFolderCustomizationWindow(LocalPractice service,long project)
 {
  this.service=service;this.project=project;Title="Customize this project — logical plan";Width=690;Height=620;
  WindowStartupLocation=WindowStartupLocation.CenterOwner;ShowInTaskbar=false;
  var panel=new StackPanel{Margin=new Thickness(16)};
  panel.Children.Add(new TextBlock{Text="Customize project folders",FontSize=22,FontWeight=FontWeights.SemiBold});
  panel.Children.Add(new TextBlock{Text="Edit this project's production paths, one per line. Use / to specify subfolders and list their parents first. Changes do not alter the business template, other projects, or physical directories. Restore original template returns to the exact recorded base folder names, not a newer business template. The saved Correspondence option is retained.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,6,0,12)});
  panel.Children.Add(paths);panel.Children.Add(correspondence);
  var bar=new WrapPanel();WorkWindowFields.Button(bar,"Save project plan",Save);restore.Click+=(_,_)=>Restore();bar.Children.Add(restore);WorkWindowFields.Button(bar,"Reload",Load);WorkWindowFields.Button(bar,"Close",Close);
  panel.Children.Add(bar);panel.Children.Add(status);Content=new ScrollViewer{Content=panel};
  Closed+=(_,_)=>{paths.Clear();status.Text="";expected=null;};Load();
 }
 void Load()
 {
  expected=null;restore.IsEnabled=false;paths.Clear();status.Text="";
  try
  {
   var draft=service.ReadProjectFolderCustomization(project);expected=draft.SelectionId;restore.IsEnabled=draft.Customized;
   paths.Text=string.Join(Environment.NewLine,draft.Paths);correspondence.IsChecked=draft.IncludeCorrespondence;
   status.Text="Editing saved selection "+draft.SelectionId+".";
  }
  catch(UnauthorizedAccessException){status.Text="Access denied.";}
  catch(ArgumentException e){status.Text=e.Message;}
  catch(Exception){status.Text="Project folder plan unavailable.";}
 }
 void Restore()
 {
  try
  {
   if(expected is not long previous||!restore.IsEnabled)throw new ArgumentException("Reload the customized project plan first.");
   if(MessageBox.Show(this,"Restore the original recorded template revision for this project? Unsaved edits are discarded. The saved Correspondence option and historical selections are retained. No physical folders or storage bindings are changed.","Restore original template",MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes)return;
   service.RestoreProjectFolderBase(project,previous,true);DialogResult=true;
  }
  catch(UnauthorizedAccessException){expected=null;restore.IsEnabled=false;paths.Clear();status.Text="Access denied.";}
  catch(ArgumentException e){status.Text=e.Message;}
  catch(Exception){status.Text="Restore unavailable; reload before retrying.";}
 }
 void Save()
 {
  try
  {
   if(expected is not long previous)throw new ArgumentException("Select a template or reload the saved project plan first.");
   var input=paths.Text.Replace("\r\n","\n").Split('\n',StringSplitOptions.RemoveEmptyEntries);
   if(MessageBox.Show(this,"Save these production folders for this project only? No files or directories will be changed.","Confirm project plan",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;
   service.SaveProjectFolderCustomization(project,previous,input,correspondence.IsChecked==true,true);DialogResult=true;
  }
  catch(UnauthorizedAccessException){expected=null;restore.IsEnabled=false;paths.Clear();status.Text="Access denied.";}
  catch(ArgumentException e){status.Text=e.Message;}
  catch(Exception){status.Text="Save unavailable; reload before retrying.";}
 }
}
