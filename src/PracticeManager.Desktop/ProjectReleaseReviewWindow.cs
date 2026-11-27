using System.Windows;
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
 bool closed;
 public ProjectReleaseReviewWindow(LocalPractice service,long project)
 {
  this.service=service;this.project=project;Title="Internal PDF release review";Width=1050;Height=720;WindowStartupLocation=WindowStartupLocation.CenterOwner;
  var root=new DockPanel{Margin=new Thickness(16)};var head=new StackPanel();
  head.Children.Add(new TextBlock{Text="Select current production PDF versions for internal review only. No electronic signatures, technical validation, permission to issue or delivery is inferred. Active project, conflict clearance, start authorization and resolved QA required.",TextWrapping=TextWrapping.Wrap});
  WorkWindowFields.Button(head,"Refresh current versions",Refresh);head.Children.Add(status);DockPanel.SetDock(head,Dock.Top);root.Children.Add(head);
  var bottom=new StackPanel();bottom.Children.Add(new TextBlock{Text="Proposed purpose"});bottom.Children.Add(purpose);bottom.Children.Add(new TextBlock{Text="Proposed recipient (label only; no sending)"});bottom.Children.Add(recipient);bottom.Children.Add(new TextBlock{Text="Review note"});bottom.Children.Add(note);WorkWindowFields.Button(bottom,"Prepare and save private review ZIP",Save);
  DockPanel.SetDock(bottom,Dock.Bottom);root.Children.Add(bottom);
  grid.SelectionMode=DataGridSelectionMode.Extended;
  WorkWindowFields.Columns(grid,("ID","Id"),("File","FileName"),("Category","Category"),("Bytes","Bytes"),("SHA-256","Hash"));
  root.Children.Add(grid);Content=root;Loaded+=(_,_)=>Refresh();Closed+=(_,_)=>{closed=true;grid.ItemsSource=null;purpose.Clear();recipient.Clear();note.Clear();status.Text="";};
 }
 void Refresh()
 {
  grid.ItemsSource=null;status.Text="";
  try{grid.ItemsSource=service.ReadProjectDocuments(project).Where(d=>d.Classification=="Production"&&d.Category is ("Drawings" or "Calculations" or "Reports")&&d.FileName.EndsWith(".pdf",StringComparison.OrdinalIgnoreCase)).ToArray();}
  catch(UnauthorizedAccessException){status.Text="Access denied.";}
  catch(Exception){status.Text="Document review unavailable.";}
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
   VerifiedDocumentFile.Save(picker.FileName,".zip",package.Bytes,package.Sha256);
   status.Text=$"Private internal review package saved: {package.DocumentCount} PDF(s). SHA-256: {package.Sha256}. No issue or delivery.";
  }
  catch(UnauthorizedAccessException){grid.ItemsSource=null;status.Text="Access denied.";}
  catch(ArgumentException ex){status.Text=ex.Message;}
  catch(Exception){status.Text="Review preparation or save not confirmed. Check destination and history; do not assume delivery.";}
 }
}
