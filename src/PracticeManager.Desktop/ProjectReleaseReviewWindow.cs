using System.Windows;
using System.Security.Cryptography;
using System.Windows.Controls;
using Microsoft.Win32;
using PracticeManager.Core;
using PracticeManager.Persistence;
namespace PracticeManager.Desktop;
internal sealed class ProjectReleaseReviewWindow:Window
{
 readonly LocalPractice service;readonly long project;
 readonly DataGrid grid=WorkWindowFields.Grid();
 readonly TextBox purpose=new(){MaxLength=200},recipient=new(){MaxLength=200},note=new(){MaxLength=1000};
 readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap};
 readonly Button save=new(){Content="Prepare and save private review ZIP",IsEnabled=false};
 bool closed,eligible;
 public ProjectReleaseReviewWindow(LocalPractice service,long project)
 {
  this.service=service;this.project=project;Title="Internal PDF release review";Width=1050;Height=720;WindowStartupLocation=WindowStartupLocation.CenterOwner;
  var root=new DockPanel{Margin=new Thickness(16)};var head=new StackPanel();
  head.Children.Add(new TextBlock{Text="Select current production PDF versions for internal review only. No electronic signatures, technical validation, permission to issue or delivery is inferred. Active project, conflict clearance, start authorization and resolved QA required.",TextWrapping=TextWrapping.Wrap});
  var refreshBar=new WrapPanel();WorkWindowFields.Button(refreshBar,"Refresh current versions",Refresh);head.Children.Add(refreshBar);head.Children.Add(status);DockPanel.SetDock(head,Dock.Top);root.Children.Add(head);
  var bottom=new StackPanel();bottom.Children.Add(new TextBlock{Text="Proposed purpose"});bottom.Children.Add(purpose);bottom.Children.Add(new TextBlock{Text="Proposed recipient (label only; no sending)"});bottom.Children.Add(recipient);bottom.Children.Add(new TextBlock{Text="Review note"});bottom.Children.Add(note);var saveBar=new WrapPanel();save.Click+=(_,_)=>Save();saveBar.Children.Add(save);bottom.Children.Add(saveBar);
  DockPanel.SetDock(bottom,Dock.Bottom);root.Children.Add(bottom);
  grid.SelectionMode=DataGridSelectionMode.Extended;
  WorkWindowFields.Columns(grid,("ID","Id"),("File","FileName"),("Category","Category"),("Bytes","Bytes"),("SHA-256","Hash"));
  root.Children.Add(grid);Content=root;grid.SelectionChanged+=(_,_)=>UpdateActions();purpose.TextChanged+=(_,_)=>UpdateActions();recipient.TextChanged+=(_,_)=>UpdateActions();note.TextChanged+=(_,_)=>UpdateActions();Loaded+=(_,_)=>Refresh();Closed+=(_,_)=>{closed=true;grid.ItemsSource=null;purpose.Clear();recipient.Clear();note.Clear();status.Text="";};
 }
 void UpdateActions(){save.IsEnabled=!closed&&eligible&&grid.SelectedItems.Count is >=1 and <=50&&!string.IsNullOrWhiteSpace(purpose.Text)&&!string.IsNullOrWhiteSpace(recipient.Text)&&!string.IsNullOrWhiteSpace(note.Text);}
 void Refresh()
 {
  eligible=false;grid.ItemsSource=null;status.Text="";UpdateActions();
  try{var rows=service.ReadReleaseReviewCandidates(project);grid.ItemsSource=rows;eligible=true;status.Text=$"{rows.Count} current production PDF candidates. Select 1–50 and enter all review fields.";}
  catch(UnauthorizedAccessException){status.Text="Access denied.";}
  catch(ArgumentException e){status.Text=e.Message;}
  catch(Exception){status.Text="Document review unavailable.";}
  UpdateActions();
 }
 void Save()
 {
  status.Text="";
  try
  {
   var selected=grid.SelectedItems.Cast<ProjectDocument>().Select(d=>new ReleaseReviewSelection(d.Id,d.Hash)).ToArray();
   if(selected.Length==0)throw new ArgumentException("Select at least one current PDF.");
   var p=purpose.Text;var r=recipient.Text;var n=note.Text;
   if(MessageBox.Show(this,$"Prepare {selected.Length} PDF version(s) for private review only? No issue or delivery will be recorded.","Confirm review preparation",MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes||closed)return;
   var picker=new SaveFileDialog{FileName="project-release-REVIEW-ONLY.zip",DefaultExt=".zip",Filter="Review package (*.zip)|*.zip",AddExtension=true,OverwritePrompt=true};
   if(picker.ShowDialog(this)!=true||closed)return;
   _=LocalStoragePath.FilePath(picker.FileName,mustExist:false,mustBeNew:true,rejectSidecars:true);
   var package=service.PrepareProjectReleaseReview(project,selected,p,r,n,true);
   try{VerifiedDocumentFile.Save(picker.FileName,".zip",package.Bytes,package.Sha256);}finally{CryptographicOperations.ZeroMemory(package.Bytes);}
   status.Text=$"Private internal review package saved: {package.DocumentCount} PDF(s). SHA-256: {package.Sha256}. No issue or delivery.";
  }
  catch(UnauthorizedAccessException){eligible=false;grid.ItemsSource=null;status.Text="Access denied.";}
  catch(ArgumentException ex){eligible=false;grid.ItemsSource=null;status.Text=ex.Message+" Refresh candidates before retrying.";}
  catch(Exception){eligible=false;grid.ItemsSource=null;status.Text="Review preparation or save not confirmed. Check destination and history; do not assume delivery.";}
  finally{UpdateActions();}
 }
}
