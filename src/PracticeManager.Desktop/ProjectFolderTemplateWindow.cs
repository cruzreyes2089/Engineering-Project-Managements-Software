using System.Windows;
using System.Windows.Controls;
using PracticeManager.Core;
using PracticeManager.Persistence;
namespace PracticeManager.Desktop;

internal sealed class ProjectFolderTemplateWindow:Window
{
 readonly StackPanel rows=new();
 readonly TextBox custom=new(){MaxLength=100};
 readonly TextBlock preview=new(){TextWrapping=TextWrapping.Wrap};
 readonly List<CheckBox> choices=new();
 readonly LocalPractice? service;readonly long project;
 FolderConfiguration? configuration;FolderRegisterEntry? reference;
 readonly Button create=new(){Content="Create selected folders (test)",IsEnabled=false};
 public ProjectFolderTemplateWindow(LocalPractice? service=null,long project=0)
 {
  this.service=service;this.project=project;
  if(service!=null&&!service.IsDocumentAcceptance)throw new UnauthorizedAccessException();
  Title=service==null?"Project folder template — preview":"Project folders — synthetic test";Width=650;Height=720;WindowStartupLocation=WindowStartupLocation.CenterOwner;
  var root=new DockPanel{Margin=new Thickness(16)};var head=new StackPanel();
  head.Children.Add(new TextBlock{Text=service==null?"Choose folders for your project. Consultant MEP belongs in Reference Documents. Preview only.":"Choose project folders to create in the dedicated synthetic test workspace. Existing files are preserved.",TextWrapping=TextWrapping.Wrap});
  var bar=new WrapPanel();WorkWindowFields.Button(bar,"Reset selection",Reset);if(service!=null){WorkWindowFields.Button(bar,"Reload",Load);create.Click+=(_,_)=>Create();bar.Children.Add(create);}
  head.Children.Add(bar);DockPanel.SetDock(head,Dock.Top);root.Children.Add(head);
  var body=new StackPanel();body.Children.Add(rows);
  if(service==null){body.Children.Add(new TextBlock{Text="Custom folder name (optional)"});body.Children.Add(custom);var actions=new WrapPanel();WorkWindowFields.Button(actions,"Add",AddCustom);body.Children.Add(actions);}body.Children.Add(preview);
  root.Children.Add(new ScrollViewer{Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});Content=root;Load();
 }
 IReadOnlyList<ProjectFolderTemplateItem> Items()=>choices.Select(x=>new ProjectFolderTemplateItem((string)x.Content,x.IsChecked==true)).ToArray();
 void AddRow(ProjectFolderTemplateItem item)
 {
  var box=new CheckBox{Content=item.Name,IsChecked=item.IncludedByDefault,Margin=new Thickness(0,4,0,4)};
  box.Checked+=(_,_)=>Preview();box.Unchecked+=(_,_)=>Preview();choices.Add(box);rows.Children.Add(box);
 }
 void Reset(){choices.Clear();rows.Children.Clear();custom.Clear();foreach(var item in configuration?.Template??ProjectFolderTemplate.Defaults)AddRow(item);Preview();}
 void Load()
 {
  create.IsEnabled=false;configuration=null;reference=null;choices.Clear();rows.Children.Clear();
  try{if(service!=null){configuration=service.ReviewAcceptanceFolderConfigurations().LastOrDefault();reference=service.ReviewAcceptanceProjectFolders(project).LastOrDefault();}Reset();create.IsEnabled=service!=null&&service.CanRegisterProjectDocuments(project)&&service.Can("Administration.Manage")&&(reference==null||reference.State=="Registered"&&reference.ConfigurationHash==configuration?.Hash);}
  catch(UnauthorizedAccessException){preview.Text="Access denied.";}catch(Exception){preview.Text="Folder setup unavailable. Reload before retrying.";}
 }
 void Create()
 {
  if(service==null||!create.IsEnabled)return;
  try{var selected=Items();var names=ProjectFolderTemplate.Preview(selected);if(MessageBox.Show(this,"Create the selected folders in the synthetic project workspace? Existing files remain unchanged.\n"+string.Join("\n",names),"Create project folders",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;
   var result=service.SetupAcceptanceProjectFolders(project,configuration?.Hash,reference?.Hash,selected,true);Load();preview.Text=$"{result.Created} created; {result.Existing} already present.\n{result.ProjectFolder}";}
  catch(UnauthorizedAccessException){create.IsEnabled=false;preview.Text="Access denied. Reload before retrying.";}
  catch(ArgumentException e){create.IsEnabled=false;preview.Text=e.Message+" Reload before retrying; earlier setup steps may remain.";}
  catch(Exception){create.IsEnabled=false;preview.Text="Setup not confirmed. Some folders or records may exist. Reload and reconcile before retrying.";}
 }
 void AddCustom()
 {
  try{var item=new ProjectFolderTemplateItem(custom.Text,true);_=ProjectFolderTemplate.Preview(Items().Append(item).ToArray());AddRow(item);custom.Clear();Preview();}
  catch(ArgumentException e){preview.Text=e.Message;}
 }
 void Preview()
 {
  try{preview.Text="Selected project folders:\n"+string.Join("\n",ProjectFolderTemplate.Preview(Items()));}
  catch(ArgumentException e){preview.Text=e.Message;}
 }
}
