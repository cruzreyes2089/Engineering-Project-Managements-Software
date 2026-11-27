using System.Windows;
using System.Windows.Controls;
using PracticeManager.Persistence;
namespace PracticeManager.Desktop;
internal sealed class ProjectFolderSelectionWindow:Window
{
 readonly LocalPractice service;readonly long project;
 readonly ComboBox choices=new(){DisplayMemberPath="Display"},recommendations=new(){DisplayMemberPath="Category"};
 readonly CheckBox correspondence=new(){Content="Include optional Correspondence"};
 readonly TextBlock paths=new(){TextWrapping=TextWrapping.Wrap},status=new(){TextWrapping=TextWrapping.Wrap};
 long? expected;
 public ProjectFolderSelectionWindow(LocalPractice service,long project)
 {
  this.service=service;this.project=project;Title="Choose project folder template";Width=660;Height=620;WindowStartupLocation=WindowStartupLocation.CenterOwner;
  var panel=new StackPanel{Margin=new Thickness(16)};
  panel.Children.Add(new TextBlock{Text="Select a project template",FontSize=22,FontWeight=FontWeights.SemiBold});
  panel.Children.Add(new TextBlock{Text="Choose the CRC preset or a saved business template. Saving retains this exact revision for this project. No actual directories are created.",TextWrapping=TextWrapping.Wrap});
  panel.Children.Add(new TextBlock{Text="Optional project category (suggestion only)"});panel.Children.Add(recommendations);
  panel.Children.Add(new TextBlock{Text="Template to select"});panel.Children.Add(choices);panel.Children.Add(correspondence);panel.Children.Add(paths);
  var bar=new WrapPanel();WorkWindowFields.Button(bar,"Confirm selection",Save);WorkWindowFields.Button(bar,"Reload",Load);WorkWindowFields.Button(bar,"Close",Close);
  panel.Children.Add(bar);panel.Children.Add(status);Content=new ScrollViewer{Content=panel};
  choices.SelectionChanged+=(_,_)=>paths.Text=choices.SelectedItem is NamedFolderChoice c?string.Join(Environment.NewLine,c.Paths):"";
  recommendations.SelectionChanged+=(_,_)=>{if(recommendations.SelectedItem is FolderTemplateRecommendation rule){choices.SelectedItem=(choices.ItemsSource as IEnumerable<NamedFolderChoice>)?.FirstOrDefault(x=>x.Revision==rule.TemplateRevision);status.Text="Suggestion only. Review or choose any other template before confirming.";}};
  Load();
 }
 void Load()
 {
  expected=null;recommendations.ItemsSource=null;choices.ItemsSource=null;paths.Text="";
  try
  {
   var current=service.ReadProjectFolderPreview(project);
   expected=current.Selection.Id;correspondence.IsChecked=current.Selection.IncludeCorrespondence;
   choices.ItemsSource=service.ReadNamedFolderChoices();
   recommendations.ItemsSource=service.ReadProjectTemplateRecommendations(project);
   choices.SelectedItem=current.Selection.Assigned?(choices.ItemsSource as IEnumerable<NamedFolderChoice>)?.FirstOrDefault(x=>x.Revision==current.Selection.TemplateRevision):null;
   status.Text=current.Selection.Assigned?"Current selection retained: "+current.TemplateRevision:"No template assigned. Choose one explicitly.";
  }
  catch(Exception){status.Text="Template selection unavailable.";}
 }
 void Save()
 {
  try
  {
   if(expected is not long previous||choices.SelectedItem is not NamedFolderChoice selected)throw new ArgumentException("Choose a template first.");
   if(MessageBox.Show(this,"Save this exact project template revision? This changes only the logical plan.","Confirm selection",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;
   service.SelectProjectFolderTemplate(project,selected.Revision,correspondence.IsChecked==true,previous,true);DialogResult=true;
  }
  catch(ArgumentException e){status.Text=e.Message;}
  catch(UnauthorizedAccessException){Load();status.Text="Access denied.";}
  catch(Exception){status.Text="Selection unavailable; reload before retrying.";}
 }
}
