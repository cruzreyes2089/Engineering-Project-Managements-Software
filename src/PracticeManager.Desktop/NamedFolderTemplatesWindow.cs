using System.Windows;
using System.Windows.Controls;
using PracticeManager.Core;
using PracticeManager.Persistence;
namespace PracticeManager.Desktop;
internal sealed class NamedFolderTemplatesWindow:Window
{
 readonly LocalPractice service;
 readonly ComboBox choices=new(){DisplayMemberPath="Display"};
 readonly TextBox name=new(){MaxLength=60};
 readonly TextBox paths=new(){AcceptsReturn=true,MinHeight=200,MaxLength=12000,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
 readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap};
 long expected;
 long? preferenceRevision;
 public NamedFolderTemplatesWindow(LocalPractice service)
 {
  this.service=service;Title="Business folder templates";Width=690;Height=650;WindowStartupLocation=WindowStartupLocation.CenterOwner;
  var panel=new StackPanel{Margin=new Thickness(16)};
  panel.Children.Add(new TextBlock{Text="Named production templates",FontSize=22,FontWeight=FontWeights.SemiBold});
  panel.Children.Add(new TextBlock{Text="Choose a saved template, copy the suggested preset, or start from scratch. Enter one relative folder per line; use / for nested folders and list the parent first. Restricted storage is separate.",TextWrapping=TextWrapping.Wrap});
  panel.Children.Add(choices);panel.Children.Add(new TextBlock{Text="Template name"});panel.Children.Add(name);panel.Children.Add(paths);
  var bar=new WrapPanel();WorkWindowFields.Button(bar,"New blank",()=>Draft("",Array.Empty<string>(),0));
  WorkWindowFields.Button(bar,"Copy suggested preset",()=>Draft("",ProjectFolderLayout.Default().Names,0));
  WorkWindowFields.Button(bar,"Copy existing business layout",()=>{try{Draft("",service.ReadFirmFolderLayout().Names,0);}catch(Exception){status.Text="Business layout unavailable."; }});
  WorkWindowFields.Button(bar,"Copy selected",()=>{if(choices.SelectedItem is NamedFolderChoice c)Draft("",c.Paths,0);});
  WorkWindowFields.Button(bar,"Set as preferred",()=>ChangePreference(false));
  WorkWindowFields.Button(bar,"Clear preferred",()=>ChangePreference(true));
  WorkWindowFields.Button(bar,"Save revision",Save);WorkWindowFields.Button(bar,"Reload",Reload);WorkWindowFields.Button(bar,"Close",Close);
  panel.Children.Add(bar);panel.Children.Add(status);Content=new ScrollViewer{Content=panel};
  choices.SelectionChanged+=(_,_)=>{if(choices.SelectedItem is NamedFolderChoice c)Draft(c.Revision==0?"":c.Name,c.Paths,c.Revision);};
  Closed+=(_,_)=>{name.Clear();paths.Clear();choices.ItemsSource=null;};Reload();
 }
 void Draft(string n,IReadOnlyList<string> p,long rev){if(rev==0&&choices.SelectedItem is NamedFolderChoice c&&c.Revision!=0)choices.SelectedItem=null;expected=rev;name.Text=n;paths.Text=string.Join(Environment.NewLine,p);status.Text="Draft — no physical folders changed.";}
 void Reload()
 {
  preferenceRevision=null;choices.ItemsSource=null;Draft("",Array.Empty<string>(),0);
  try{choices.ItemsSource=service.ReadNamedFolderChoices();var pref=service.ReadPreferredFolderTemplate();preferenceRevision=pref.Revision;
   var chosen=(choices.ItemsSource as IEnumerable<NamedFolderChoice>)?.FirstOrDefault(x=>x.Revision==pref.TemplateRevision);
   status.Text=pref.Available?"Preferred: "+chosen?.Name:pref.TemplateRevision.HasValue?"Preferred template changed; review and set again.":"No preferred template set.";}
  catch(Exception){status.Text="Template list unavailable.";}
 }
 void ChangePreference(bool clear)
 {
  try
  {
   if(preferenceRevision is not long revision)throw new ArgumentException("Reload templates first.");
   var choice=choices.SelectedItem as NamedFolderChoice;
   if(!clear&&choice==null)throw new ArgumentException("Select a saved template, not an unsaved draft.");
   if(MessageBox.Show(this,clear?"Clear the business preference?":"Use the selected saved template as the preferred suggestion for new project selections?","Confirm preference",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;
   service.SavePreferredFolderTemplate(clear?null:choice!.Revision,revision,true);Reload();
  }
  catch(ArgumentException e){status.Text=e.Message;}
  catch(UnauthorizedAccessException){Reload();status.Text="Access denied.";}
  catch(Exception){status.Text="Preference unavailable; reload before retrying.";}
 }
 void Save()
 {
  try
  {
   var input=paths.Text.Replace("\r\n","\n").Split('\n',StringSplitOptions.RemoveEmptyEntries);
   if(MessageBox.Show(this,"Save this named template revision? Existing project selections are retained.","Save template",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;
   var saved=service.SaveNamedFolderChoice(name.Text,input,expected,true);Reload();
   choices.SelectedItem=(choices.ItemsSource as IEnumerable<NamedFolderChoice>)?.FirstOrDefault(x=>x.Revision==saved.Revision);
   status.Text="Saved revision "+saved.Revision+". No physical folders changed.";
  }
  catch(ArgumentException e){status.Text=e.Message;}
  catch(UnauthorizedAccessException){Reload();status.Text="Access denied.";}
  catch(Exception){status.Text="Save unavailable; reload before retrying.";}
 }
}
