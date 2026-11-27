using System.Windows;
using System.Windows.Controls;
using PracticeManager.Persistence;
namespace PracticeManager.Desktop;
internal sealed class ProjectStorageBindingWindow:Window
{
 readonly LocalPractice service;readonly long project;
 readonly TextBlock saved=new(){TextWrapping=TextWrapping.Wrap},candidate=new(){TextWrapping=TextWrapping.Wrap},status=new(){TextWrapping=TextWrapping.Wrap};
 ProjectStorageBindingReview? reviewed;
 long? bindingRevision;
 public ProjectStorageBindingWindow(LocalPractice service,long project)
 {
  this.service=service;this.project=project;Title="Project storage assignment — inactive";Width=720;Height=570;MinWidth=500;MinHeight=400;
  WindowStartupLocation=WindowStartupLocation.CenterOwner;ShowInTaskbar=false;
  var panel=new StackPanel{Margin=new Thickness(16)};
  panel.Children.Add(new TextBlock{Text="Project storage assignment",FontSize=22,FontWeight=FontWeights.SemiBold});
  panel.Children.Add(new TextBlock{Text="Retain the exact business storage preference and project folder selection for this project. Assignments remain inactive. The optional read-only inspection checks directory metadata only; nothing is moved or created.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,6,0,12)});
  panel.Children.Add(new TextBlock{Text="Saved assignment",FontWeight=FontWeights.SemiBold});panel.Children.Add(saved);
  panel.Children.Add(new TextBlock{Text="Proposed assignment",FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,12,0,0)});panel.Children.Add(candidate);
  var bar=new WrapPanel{Margin=new Thickness(0,12,0,0)};WorkWindowFields.Button(bar,"Review current preferences",Review);WorkWindowFields.Button(bar,"Save reviewed assignment",Save);WorkWindowFields.Button(bar,"Inspect saved locations",Inspect);WorkWindowFields.Button(bar,"Close",Close);
  panel.Children.Add(bar);panel.Children.Add(status);Content=new ScrollViewer{Content=panel};
  Closed+=(_,_)=>{reviewed=null;saved.Text=candidate.Text=status.Text="";};Load();
 }
 void Load()
 {
  bindingRevision=null;reviewed=null;candidate.Text=saved.Text=status.Text="";
  try
  {
   var binding=service.ReadProjectStorageBinding(project);bindingRevision=binding?.Revision;
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
 void Inspect()
 {
  reviewed=null;candidate.Text="";
  try
  {
   if(bindingRevision is not long revision)throw new ArgumentException("Save and reload an inactive assignment first.");
   if(MessageBox.Show(this,"Inspect the saved locations and planned directory metadata? No files will be opened or changed. This is not a storage approval.","Read-only inspection",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;
   var observed=service.ObserveProjectStorage(project,revision,true);
   candidate.Text=$"Inspection: {observed.Status}\nPlanned: {observed.Planned}; missing: {observed.Missing}; existing: {observed.Existing}; blocked: {observed.Blocked}.";
   status.Text="Point-in-time metadata only. Effective access, existing-folder adoption and safe creation remain unqualified.";
  }
  catch(UnauthorizedAccessException){Load();status.Text="Access denied.";}
  catch(ArgumentException e){Load();status.Text=e.Message;}
  catch(Exception){status.Text="Inspection unavailable. No storage was changed.";}
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
