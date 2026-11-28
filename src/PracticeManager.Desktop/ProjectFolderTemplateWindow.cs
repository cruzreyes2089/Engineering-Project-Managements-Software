using System.Windows;
using System.Windows.Controls;
using PracticeManager.Core;
namespace PracticeManager.Desktop;

internal sealed class ProjectFolderTemplateWindow:Window
{
 readonly StackPanel rows=new();
 readonly TextBox custom=new(){MaxLength=100};
 readonly TextBlock preview=new(){TextWrapping=TextWrapping.Wrap};
 readonly List<CheckBox> choices=new();
 public ProjectFolderTemplateWindow()
 {
  Title="CRC project folder template — preview";Width=650;Height=720;WindowStartupLocation=WindowStartupLocation.CenterOwner;
  var root=new DockPanel{Margin=new Thickness(16)};var head=new StackPanel();
  head.Children.Add(new TextBlock{Text="Select folders appropriate for the project. QA-QC is included by default. Consultant MEP plans belong in Reference Documents. This preview does not create or alter folders.",TextWrapping=TextWrapping.Wrap});
  var bar=new WrapPanel();WorkWindowFields.Button(bar,"Reset standard",Reset);WorkWindowFields.Button(bar,"Preview selected",Preview);
  head.Children.Add(bar);DockPanel.SetDock(head,Dock.Top);root.Children.Add(head);
  var body=new StackPanel();body.Children.Add(rows);
  body.Children.Add(new TextBlock{Text="Add custom folder label for this preview"});body.Children.Add(custom);
  var actions=new WrapPanel();WorkWindowFields.Button(actions,"Add",AddCustom);body.Children.Add(actions);body.Children.Add(preview);
  root.Children.Add(new ScrollViewer{Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});Content=root;Reset();
 }
 IReadOnlyList<ProjectFolderTemplateItem> Items()=>choices.Select(x=>new ProjectFolderTemplateItem((string)x.Content,x.IsChecked==true)).ToArray();
 void AddRow(ProjectFolderTemplateItem item)
 {
  var box=new CheckBox{Content=item.Name,IsChecked=item.IncludedByDefault,Margin=new Thickness(0,4,0,4)};
  box.Checked+=(_,_)=>Preview();box.Unchecked+=(_,_)=>Preview();choices.Add(box);rows.Children.Add(box);
 }
 void Reset(){choices.Clear();rows.Children.Clear();custom.Clear();foreach(var item in ProjectFolderTemplate.Defaults)AddRow(item);Preview();}
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
