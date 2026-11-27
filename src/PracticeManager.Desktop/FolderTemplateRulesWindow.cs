using System.Windows;
using System.Windows.Controls;
using PracticeManager.Persistence;
namespace PracticeManager.Desktop;

internal sealed class FolderTemplateRulesWindow:Window
{
 readonly LocalPractice service;
 readonly ComboBox rules=new(){DisplayMemberPath="Display"},templates=new(){DisplayMemberPath="Display"};
 readonly TextBox category=new(){MaxLength=60};
 readonly CheckBox enabled=new(){Content="Recommend this template",IsChecked=true};
 readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap};
 long? revision;
 public FolderTemplateRulesWindow(LocalPractice service)
 {
  this.service=service;Title="Project category recommendations";Width=670;Height=470;WindowStartupLocation=WindowStartupLocation.CenterOwner;
  var panel=new StackPanel{Margin=new Thickness(16)};
  panel.Children.Add(new TextBlock{Text="Project category recommendations",FontSize=22,FontWeight=FontWeights.SemiBold});
  panel.Children.Add(new TextBlock{Text="Define your business's project categories and suggest a template for each. Suggestions never assign a template or create folders. When a template changes, review its recommendation again.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,6,0,12)});
  panel.Children.Add(new TextBlock{Text="Existing category"});panel.Children.Add(rules);
  panel.Children.Add(new TextBlock{Text="Category name"});panel.Children.Add(category);
  panel.Children.Add(new TextBlock{Text="Recommended template"});panel.Children.Add(templates);panel.Children.Add(enabled);
  var bar=new WrapPanel();WorkWindowFields.Button(bar,"New category",New);WorkWindowFields.Button(bar,"Save recommendation",Save);
  WorkWindowFields.Button(bar,"Reload",Load);WorkWindowFields.Button(bar,"Close",Close);
  panel.Children.Add(bar);panel.Children.Add(status);Content=new ScrollViewer{Content=panel};
  rules.SelectionChanged+=(_,_)=>{if(rules.SelectedItem is FolderTemplateRecommendation r){revision=r.Revision;category.Text=r.Category;enabled.IsChecked=r.Enabled;templates.SelectedItem=(templates.ItemsSource as IEnumerable<NamedFolderChoice>)?.FirstOrDefault(x=>x.Revision==r.TemplateRevision);status.Text=r.Display;}};
  Closed+=(_,_)=>{rules.ItemsSource=null;templates.ItemsSource=null;category.Clear();revision=null;};Load();
 }
 void New(){rules.SelectedItem=null;category.Clear();templates.SelectedItem=null;enabled.IsChecked=true;revision=0;status.Text="New category draft.";}
 void Load()
 {
  revision=null;rules.ItemsSource=null;templates.ItemsSource=null;category.Clear();
  try{templates.ItemsSource=service.ReadNamedFolderChoices();rules.ItemsSource=service.ReadFolderTemplateRules();New();}
  catch(Exception){status.Text="Recommendation settings unavailable.";}
 }
 void Save()
 {
  try
  {
   if(revision is not long expected)throw new ArgumentException("Reload settings first.");
   var prior=rules.SelectedItem as FolderTemplateRecommendation;
   var choice=templates.SelectedItem as NamedFolderChoice;
   if(choice==null&&(enabled.IsChecked==true||prior==null))throw new ArgumentException("Select a template.");
   var id=choice?.Revision??prior!.TemplateRevision;
   if(MessageBox.Show(this,"Save this recommendation? Existing projects will not change.","Confirm recommendation",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;
   var saved=service.SaveFolderTemplateRule(category.Text,id,enabled.IsChecked==true,expected,true);
   Load();rules.SelectedItem=(rules.ItemsSource as IEnumerable<FolderTemplateRecommendation>)?.FirstOrDefault(x=>x.Revision==saved.Revision);
   status.Text="Recommendation saved.";
  }
  catch(UnauthorizedAccessException){Load();status.Text="Access denied.";}
  catch(ArgumentException e){status.Text=e.Message;}
  catch(Exception){status.Text="Save unavailable; reload before retrying.";}
 }
}
