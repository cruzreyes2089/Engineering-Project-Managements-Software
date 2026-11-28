using System.Windows;
using System.Windows.Controls;
using PracticeManager.Core;
using PracticeManager.Persistence;
namespace PracticeManager.Desktop;
internal sealed class DocumentFolderAcceptanceWindow:Window
{
 readonly LocalPractice service;readonly long project;
 readonly TextBox rootPath=new(){MaxLength=512},template=new(){AcceptsReturn=true,MinHeight=160,MaxLength=4096},reason=new(){MaxLength=1000},relative=new(){MaxLength=240};
 readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap};
 readonly DataGrid configurations=WorkWindowFields.Grid(),folders=WorkWindowFields.Grid();
 FolderConfiguration? current;FolderRegisterEntry? prior;bool closed,ready;
 readonly Dictionary<string,Button> commands=new();
 public DocumentFolderAcceptanceWindow(LocalPractice service,long project)
 {
  this.service=service;this.project=project;Title="SYNTHETIC — Project folder configuration and register";Width=1000;Height=780;WindowStartupLocation=WindowStartupLocation.CenterOwner;
  var body=new StackPanel{Margin=new Thickness(16)};
  void Label(string x)=>body.Children.Add(new TextBlock{Text=x,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,4,0,4)});
  Label("Synthetic local paths only. Existing directories are never renamed, deleted or moved. Directory creation is limited to the dedicated synthetic workspace. Consultant MEP belongs in Reference Documents. QA-QC is standard.");
  void Command(WrapPanel bar,string key,string label,Action action){var b=new Button{Content=label,IsEnabled=false,Margin=new Thickness(2)};b.Click+=(_,_)=>action();commands.Add(key,b);bar.Children.Add(b);}
  var actions=new WrapPanel();WorkWindowFields.Button(actions,"Reload current records",()=>Refresh());Command(actions,"root","Prepare synthetic root",PrepareRoot);body.Children.Add(actions);
  Label("Reviewed dedicated project root (existing local folder)");body.Children.Add(rootPath);
  Label("Template: one folder per line; prefix optional/unselected labels with '- '");body.Children.Add(template);
  Label("Reason for configuration or folder change");body.Children.Add(reason);
  var configBar=new WrapPanel();Command(configBar,"config","Record configuration revision",SaveConfig);body.Children.Add(configBar);
  configurations.Height=125;WorkWindowFields.Columns(configurations,("Revision","Revision"),("Root","RootPath"),("Reason","Reason"),("Recorded UTC","Utc"));body.Children.Add(configurations);
  Label("Relative path of existing project folder (within current root)");body.Children.Add(relative);
  var folderBar=new WrapPanel();Command(folderBar,"parent","Prepare synthetic project directory",PrepareParent);Command(folderBar,"register","Register existing folder",Register);Command(folderBar,"children","Preview / create template folders",CreateChildren);Command(folderBar,"retire","Retire current reference",Retire);body.Children.Add(folderBar);
  folders.Height=150;WorkWindowFields.Columns(folders,("Revision","Revision"),("State","State"),("Folder","DisplayPath"),("Configuration","ConfigurationHash"),("Reason","Reason"));body.Children.Add(folders);body.Children.Add(status);
  Content=new ScrollViewer{Content=body};foreach(var field in new[]{rootPath,template,reason,relative})field.TextChanged+=(_,_)=>UpdateActions();Closed+=(_,_)=>{closed=true;Clear();};Refresh();
 }
 void UpdateActions(){var available=ready&&!closed;var why=!string.IsNullOrWhiteSpace(reason.Text);commands["root"].IsEnabled=available;commands["config"].IsEnabled=available&&why&&!string.IsNullOrWhiteSpace(rootPath.Text)&&!string.IsNullOrWhiteSpace(template.Text);commands["parent"].IsEnabled=available&&current!=null;commands["register"].IsEnabled=available&&current!=null&&why&&!string.IsNullOrWhiteSpace(relative.Text);commands["children"].IsEnabled=available&&current!=null&&prior is {State:"Registered"}&&prior.ConfigurationHash==current.Hash;commands["retire"].IsEnabled=available&&why&&prior is {State:"Registered"};}
 void Clear(){ready=false;current=null;prior=null;configurations.ItemsSource=null;folders.ItemsSource=null;rootPath.Clear();template.Clear();reason.Clear();relative.Clear();status.Text="";UpdateActions();}
 bool Refresh()
 {
  Clear();if(closed)return false;
  try
  {
   var configs=service.ReviewAcceptanceFolderConfigurations();var history=service.ReviewAcceptanceProjectFolders(project);
   current=configs.LastOrDefault();prior=history.LastOrDefault();configurations.ItemsSource=configs;folders.ItemsSource=history;
   rootPath.Text=current?.RootPath??"";template.Text=string.Join("\n",(current?.Template??ProjectFolderTemplate.Defaults).Select(x=>(x.IncludedByDefault?"":"- ")+x.Name));
   if(prior?.State=="Registered")relative.Text=prior.RelativePath;
   ready=true;UpdateActions();status.Text="Current recorded configuration and project reference loaded.";return true;
  }
  catch(UnauthorizedAccessException){Clear();status.Text="Access denied.";}
  catch(Exception){Clear();status.Text="Folder register unavailable.";}
  return false;
 }
 bool Confirm(string text)=>MessageBox.Show(this,text,"Confirm synthetic record",MessageBoxButton.YesNo,MessageBoxImage.Warning)==MessageBoxResult.Yes&&!closed;
 void Act(Action a)
 {
  try{a();}
  catch(UnauthorizedAccessException){Clear();status.Text="Access denied.";}
  catch(ArgumentException e){status.Text=e.Message;}
  catch(Exception){Clear();status.Text="Record not confirmed; reload and reconcile before retrying.";}
 }
 void Recorded(string message){if(!closed){if(Refresh())status.Text=message;else status.Text=message+" Reload failed; reconcile before retrying.";}}
 void SaveConfig()=>Act(()=>{
  var path=rootPath.Text;var why=reason.Text;var expected=current?.Hash;
  var items=template.Text.Split(new[]{"\r\n","\n"},StringSplitOptions.RemoveEmptyEntries).Select(x=>x.StartsWith("- ",StringComparison.Ordinal)?new ProjectFolderTemplateItem(x[2..],false):new ProjectFolderTemplateItem(x,true)).ToArray();
  var preview=ProjectFolderTemplate.Preview(items);
  if(!Confirm("Record this root and template revision?\n"+path+"\n"+string.Join("\n",preview)))return;
  service.SaveAcceptanceFolderConfiguration(Guid.NewGuid(),expected,path,items,why,true);Recorded("Configuration revision recorded.");
 });
 void Register()=>Act(()=>{
  var config=current??throw new ArgumentException("Record a reviewed root first.");var path=relative.Text;var why=reason.Text;var expected=prior?.Hash;
  if(!Confirm("Register existing folder reference only?\n"+config.RootPath+"\n"+path))return;
  service.SaveAcceptanceProjectFolder(project,Guid.NewGuid(),expected,config.Hash,"Registered",path,why,true);Recorded("Folder reference registered.");
 });
 void Retire()=>Act(()=>{
  var record=prior is {State:"Registered"} p?p:throw new ArgumentException("No current reference.");var why=reason.Text;
  if(!Confirm("Retire reference without deleting files?\n"+record.DisplayPath))return;
  service.SaveAcceptanceProjectFolder(project,Guid.NewGuid(),record.Hash,record.ConfigurationHash,"Retired",record.PathKey,why,true);Recorded("Reference retired; directory unchanged.");
 });
 void PrepareRoot()=>Act(()=>{
  if(!Confirm("Prepare the dedicated synthetic test directory? No real project directory will be created."))return;
  rootPath.Text=service.PrepareAcceptanceFolderRoot(true);status.Text="Synthetic root prepared. Review and record its configuration.";
 });
 void PrepareParent()=>Act(()=>{
  var config=current??throw new ArgumentException("Record the dedicated synthetic root configuration first.");
  if(!Confirm("Prepare the application-named synthetic project directory? Existing contents will remain untouched."))return;
  relative.Text=service.PrepareAcceptanceProjectDirectory(project,config.Hash,true);status.Text="Synthetic parent prepared. Register the reference separately.";
 });
 void CreateChildren()=>Act(()=>{
  var config=current??throw new ArgumentException("Record the reviewed template first.");
  var folder=prior is {State:"Registered"} p?p:throw new ArgumentException("Register the synthetic project folder first.");
  var selection=config.Template.ToArray();
  var plan=service.PreviewAcceptanceFolderCreation(project,folder.Hash,config.Hash,selection);
  if(plan.Missing.Count==0){status.Text="All selected template folders already exist; nothing to create.";return;}
  if(!Confirm("Create only the following missing folders?\n"+plan.ProjectFolder+"\n"+string.Join("\n",plan.Missing)+"\nExisting folders preserved: "+plan.Existing.Count))return;
  var created=service.CreateAcceptanceFolders(project,folder.Hash,config.Hash,selection,plan.PlanHash,true);
  Recorded($"{created.Count} new synthetic template folders created. Existing contents preserved.");
 });

}
