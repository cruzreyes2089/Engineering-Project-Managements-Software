using System.Windows;
using System.Windows.Controls;
using PracticeManager.Core;
using PracticeManager.Persistence;
namespace PracticeManager.Desktop;

internal sealed class ProjectFolderPreviewWindow:Window
{
 readonly LocalPractice service;readonly long project;
 readonly CheckBox correspondence=new(){Content="Include optional Correspondence folder"};
 readonly TreeView tree=new();
 readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,8)};
 public ProjectFolderPreviewWindow(LocalPractice service,long project)
 {
  this.service=service;this.project=project;Title="Project folders — preview";Width=680;Height=640;
  MinWidth=480;MinHeight=400;WindowStartupLocation=WindowStartupLocation.CenterOwner;ShowInTaskbar=false;
  var root=new DockPanel{Margin=new Thickness(16)};var head=new StackPanel();
  head.Children.Add(new TextBlock{Text="Project folders",FontSize=24,FontWeight=FontWeights.SemiBold});
  head.Children.Add(new TextBlock{Text="Preview the standard relative folder layout. No folders or files are created, moved, inspected or deleted. Unassigned layouts are suggestions only. Choose a saved template or the suggested preset. This is not a document access grant or a storage registration.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,4,0,10)});
  head.Children.Add(correspondence);
  var bar=new WrapPanel();WorkWindowFields.Button(bar,"Refresh preview",Refresh);
  if(service.Can("Administration.Manage")&&service.Can("Documents.Restricted"))
  {
   WorkWindowFields.Button(bar,"Choose template",()=>{new ProjectFolderSelectionWindow(service,project){Owner=this}.ShowDialog();Refresh();});
   WorkWindowFields.Button(bar,"Customize project folders",()=>{new ProjectFolderCustomizationWindow(service,project){Owner=this}.ShowDialog();Refresh();});
   WorkWindowFields.Button(bar,"Storage assignment",()=>new ProjectStorageBindingWindow(service,project){Owner=this}.ShowDialog());
   WorkWindowFields.Button(bar,"Review storage locations",()=>new ProjectStorageDraftWindow(service,project,correspondence.IsChecked==true){Owner=this}.ShowDialog());
  }
  WorkWindowFields.Button(bar,"Close",Close);
  head.Children.Add(bar);head.Children.Add(status);DockPanel.SetDock(head,Dock.Top);root.Children.Add(head);
  root.Children.Add(tree);Content=root;correspondence.Checked+=(_,_)=>Refresh();correspondence.Unchecked+=(_,_)=>Refresh();
  Closed+=(_,_)=>{tree.Items.Clear();status.Text="";};Refresh();
 }
 static TreeViewItem Item(ProjectFolderNode node)
 {
  var result=new TreeViewItem{Header=node.Name,IsExpanded=true};
  foreach(var child in node.Children)result.Items.Add(Item(child));return result;
 }
 void Refresh()
 {
  tree.Items.Clear();status.Text="";
  try
  {
   var preview=service.ReadProjectFolderPreview(project,correspondence.IsChecked==true);
   var production=new TreeViewItem{Header="Production / "+preview.ProjectNumber,IsExpanded=true};
   foreach(var folder in preview.Production)production.Items.Add(Item(folder));tree.Items.Add(production);
   if(preview.Restricted.Count>0)
   {
    var restricted=new TreeViewItem{Header="Restricted administration / "+preview.ProjectNumber+" — separate root",IsExpanded=true};
    foreach(var folder in preview.Restricted)restricted.Items.Add(Item(folder));tree.Items.Add(restricted);
   }
   correspondence.IsEnabled=!preview.Selection.Assigned;
   if(preview.Selection.Assigned&&correspondence.IsChecked!=preview.Selection.IncludeCorrespondence){correspondence.IsChecked=preview.Selection.IncludeCorrespondence;return;}
   var displayRevision=preview.TemplateRevision==ProjectFolderTemplate.Revision?"Suggested preset":preview.TemplateRevision;
   status.Text=$"Project: {preview.ProjectNumber}   |   Template: {displayRevision}   |   {preview.Plan.Entries.Count} planned directories including project roots   |   "+(preview.Selection.Customized?"Saved project-specific plan":preview.Selection.Assigned?"Saved logical selection":"Unassigned suggestion only");
  }
  catch(UnauthorizedAccessException){status.Text="Access denied.";}
  catch(Exception){status.Text="Project folder preview unavailable. No changes were made.";}
 }
}
