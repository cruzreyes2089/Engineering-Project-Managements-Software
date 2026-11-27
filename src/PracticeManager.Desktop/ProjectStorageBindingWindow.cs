using System.Windows;
using System.Windows.Controls;
using PracticeManager.Persistence;
namespace PracticeManager.Desktop;
internal sealed class ProjectStorageBindingWindow:Window
{
 readonly LocalPractice service;readonly long project;
 readonly TextBlock saved=new(){TextWrapping=TextWrapping.Wrap},candidate=new(){TextWrapping=TextWrapping.Wrap},status=new(){TextWrapping=TextWrapping.Wrap};
 ProjectStorageBindingReview? reviewed;
 public ProjectStorageBindingWindow(LocalPractice service,long project)
 {
  this.service=service;this.project=project;Title="Project storage assignment — inactive";Width=720;Height=570;MinWidth=500;MinHeight=400;
  WindowStartupLocation=WindowStartupLocation.CenterOwner;ShowInTaskbar=false;
  var panel=new StackPanel{Margin=new Thickness(16)};
  panel.Children.Add(new TextBlock{Text="Project storage assignment",FontSize=22,FontWeight=FontWeights.SemiBold});
  panel.Children.Add(new TextBlock{Text="Retain the exact business storage preference and project folder selection for this project. This saves an inactive database record only; no filesystem access, movement or creation.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,6,0,12)});
  panel.Children.Add(new TextBlock{Text="Saved assignment",FontWeight=FontWeights.SemiBold});panel.Children.Add(saved);
  panel.Children.Add(new TextBlock{Text="Proposed assignment",FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,12,0,0)});panel.Children.Add(candidate);
  var bar=new WrapPanel{Margin=new Thickness(0,12,0,0)};WorkWindowFields.Button(bar,"Review current preferences",Review);WorkWindowFields.Button(bar,"Save reviewed assignment",Save);WorkWindowFields.Button(bar,"Close",Close);
  panel.Children.Add(bar);panel.Children.Add(status);Content=new ScrollViewer{Content=panel};
  Closed+=(_,_)=>{reviewed=null;saved.Text=candidate.Text=status.Text="";};Load();
 }
 void Load()
 {
  reviewed=null;candidate.Text=saved.Text=status.Text="";
  try
  {
   var binding=service.ReadProjectStorageBinding(project);
   saved.Text=binding==null?"Not assigned.":$"Production: {binding.ProductionRoot}\nRestricted: {binding.RestrictedRoot}\nPreference {binding.PreferenceRevision}; selection {binding.SelectionId}. Inactive.\n"+
    (!binding.CurrentSelection?"Folder plan changed; review again. ": "")+(!binding.CurrentPreference?"Business preferences changed; saved roots retained.":"");
  }
  catch(UnauthorizedAccessException){status.Text="Access denied.";}
  catch(Exception){status.Text="Saved assignment unavailable.";}
 }
 void Review()
 {
  Load();
  try
  {
   reviewed=service.ReviewProjectStorageBinding(project);
   var draft=reviewed.Draft;
   candidate.Text=$"Production: {draft.ProductionRoot}\nRestricted: {draft.RestrictedRoot}\n{draft.ProductionDirectories} production and {draft.RestrictedDirectories} restricted directories, including roots.\nPreference {reviewed.PreferenceRevision}; selection {reviewed.SelectionId}.";
   status.Text="Review only. No filesystem access or approval.";
  }
  catch(UnauthorizedAccessException){saved.Text=candidate.Text="";status.Text="Access denied.";}
  catch(ArgumentException e){status.Text=e.Message;}
  catch(Exception){status.Text="Review unavailable.";}
 }
 void Save()
 {
  try
  {
   var review=reviewed??throw new ArgumentException("Review current preferences first.");
   if(MessageBox.Show(this,"Save the exact reviewed inactive assignment? This will not move any project files or approve storage.","Confirm storage assignment",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;
   service.SaveProjectStorageBinding(project,review.ExpectedBinding,review.PreferenceRevision,review.SelectionId,review.Draft.Fingerprint,true);
   Load();status.Text="Inactive assignment saved.";
  }
  catch(UnauthorizedAccessException){Load();status.Text="Access denied.";}
  catch(ArgumentException e){reviewed=null;candidate.Text="";status.Text=e.Message;}
  catch(Exception){reviewed=null;candidate.Text="";status.Text="Save unavailable; review again.";}
 }
}
