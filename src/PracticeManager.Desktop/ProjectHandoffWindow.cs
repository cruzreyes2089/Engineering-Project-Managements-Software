using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using PracticeManager.Core;
using PracticeManager.Persistence;
namespace PracticeManager.Desktop;
internal sealed class ProjectHandoffWindow:Window
{
 readonly LocalPractice service;readonly long project;
 readonly TextBlock summary=new(){TextWrapping=TextWrapping.Wrap};
 readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap};
 readonly TextBox evidence=new(){MaxLength=1000,AcceptsReturn=true,Height=75,TextWrapping=TextWrapping.Wrap};
 readonly TextBox startNote=new(){MaxLength=1000,AcceptsReturn=true,Height=55,TextWrapping=TextWrapping.Wrap};
 readonly CheckBox reviewed=new(){Content="I reviewed the signatures, parties and terms against the accepted proposal."};
 readonly Button attach=new(){Content="Attach signed agreement"},authorize=new(){Content="Start project"},saveSetup=new(){Content="Save checklist",IsEnabled=false};
 readonly StackPanel checklist=new();
 ProjectHandoff? handoff;
 ProjectSetupReview? setupSnapshot;
 readonly List<(ProjectSetupItem Original,CheckBox Complete)> fields=new();
 readonly TextBox setupNote=new(){MaxLength=1000,AcceptsReturn=true,Height=55,TextWrapping=TextWrapping.Wrap};
 static string SetupLabel(string item)=>item switch{"ScopeReviewed"=>"Scope reviewed","ConflictReviewed"=>"Conflict review completed","ProjectContacts"=>"Project contacts reviewed","DeliveryPlan"=>"Delivery plan reviewed",_=>item};
 public ProjectHandoffWindow(LocalPractice service,long project)
 {
  this.service=service;this.project=project;Title="Project setup";Width=730;Height=720;WindowStartupLocation=WindowStartupLocation.CenterOwner;
  var panel=new StackPanel{Margin=new Thickness(18)};
  panel.Children.Add(new TextBlock{Text="Project setup",FontSize=22,FontWeight=FontWeights.SemiBold});
  panel.Children.Add(new TextBlock{Text="Development only. Review the signed agreement and conflict status before starting work. The software does not verify signatures or legal enforceability.",TextWrapping=TextWrapping.Wrap});
  panel.Children.Add(summary);var conflict=new Button{Content="Conflict review"};conflict.Click+=(_,_)=>Run(()=>{new ConflictReviewWindow(service,project){Owner=this}.ShowDialog();startNote.Clear();Refresh();});conflict.IsEnabled=service.Can("Compliance.View");panel.Children.Add(conflict);var parties=new Button{Content="Project contacts",IsEnabled=service.Can("Contacts.View")};parties.Click+=(_,_)=>Run(()=>{new ProjectPartiesWindow(service,project){Owner=this}.ShowDialog();startNote.Clear();Refresh();});panel.Children.Add(parties);panel.Children.Add(new TextBlock{Text="Signed agreement review note"});panel.Children.Add(evidence);panel.Children.Add(reviewed);panel.Children.Add(attach);panel.Children.Add(new TextBlock{Text="Start authorization note"});panel.Children.Add(startNote);panel.Children.Add(authorize);
  panel.Children.Add(new TextBlock{Text="Project setup checklist",FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,12,0,4)});panel.Children.Add(checklist);panel.Children.Add(new TextBlock{Text="Review note for changed items"});panel.Children.Add(setupNote);panel.Children.Add(saveSetup);saveSetup.Click+=(_,_)=>Run(SaveChecklist);panel.Children.Add(status);
  var refresh=new Button{Content="Refresh"};refresh.Click+=(_,_)=>{if(!HasUnsavedChecklist()||MessageBox.Show(this,"Discard unsaved checklist edits and reload?","Reload checklist",MessageBoxButton.YesNo)==MessageBoxResult.Yes)Refresh(true);};panel.Children.Add(refresh);Content=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
  attach.Click+=(_,_)=>Run(Attach);authorize.Click+=(_,_)=>Run(Authorize);Refresh();
 }
 void Run(Action action){try{action();}catch(UnauthorizedAccessException){status.Text="Access denied.";}catch(ArgumentException e){status.Text=e.Message;}catch(Exception){status.Text="Operation failed; refresh before retrying.";}}
 bool HasUnsavedChecklist()=>fields.Any(x=>(x.Complete.IsChecked==true)!=x.Original.Complete)||!string.IsNullOrWhiteSpace(setupNote.Text);
 void ClearChecklist(){setupSnapshot=null;fields.Clear();checklist.Children.Clear();setupNote.Clear();saveSetup.IsEnabled=setupNote.IsEnabled=false;}
 bool Refresh(bool discardSetup=false)
 {
  var previous=handoff;var dirty=!discardSetup&&HasUnsavedChecklist();var messages=new List<string>();bool success=true;
  handoff=null;summary.Text="Agreement information restricted or unavailable.";attach.IsEnabled=authorize.IsEnabled=false;reviewed.IsChecked=false;
  try{if(service.Can("Agreements.View")){handoff=service.ReadProjectHandoff(project);var a=handoff.CurrentAgreement;
   summary.Text=$"{handoff.ProjectNumber} — {handoff.State}\n"+(a==null?"No reviewed signed PDF registered.":$"Signed agreement: {a.FileName}");
   var conflictState=service.ReadProjectConflictStatus(project);summary.Text+="\nConflict review: "+conflictState;attach.IsEnabled=handoff.State=="Pending"&&service.Can("Agreements.Record");authorize.IsEnabled=handoff.State=="Pending"&&a!=null&&conflictState=="Clear"&&service.Can("Projects.Authorize");}}
  catch(Exception){handoff=null;attach.IsEnabled=authorize.IsEnabled=false;summary.Text="Agreement information unavailable.";messages.Add("Agreement refresh failed.");success=false;}
  if(handoff==null||previous?.CurrentAgreement?.Id!=handoff.CurrentAgreement?.Id||previous?.State!=handoff.State){evidence.Clear();startNote.Clear();}
  evidence.IsEnabled=reviewed.IsEnabled=attach.IsEnabled;startNote.IsEnabled=authorize.IsEnabled;
  try{var setup=service.ReadProjectSetup(project);var editable=service.Can("Projects.Setup");
   if(dirty&&editable&&setupSnapshot!=null){
    var current=setupSnapshot.Items.SequenceEqual(setup.Items);
    foreach(var field in fields)field.Complete.IsEnabled=current;
    saveSetup.IsEnabled=setupNote.IsEnabled=current;
    if(!current){messages.Add("Checklist changed elsewhere. Unsaved edits retained for reference; use Refresh to discard and reload.");success=false;}
   }else{
    ClearChecklist();setupSnapshot=setup;checklist.Children.Add(new TextBlock{Text="Setup: "+setup.State});
    foreach(var item in setup.Items){var row=new StackPanel{Margin=new Thickness(0,4,0,4)};var check=new CheckBox{Content=SetupLabel(item.Item),IsChecked=item.Complete,IsEnabled=editable};row.Children.Add(check);if(!string.IsNullOrWhiteSpace(item.Note))row.Children.Add(new Expander{Header="Previous review note",Content=new TextBlock{Text=item.Note,TextWrapping=TextWrapping.Wrap},IsExpanded=false});checklist.Children.Add(row);fields.Add((item,check));}
    saveSetup.IsEnabled=setupNote.IsEnabled=editable;
   }
  }catch(UnauthorizedAccessException){ClearChecklist();messages.Add("Checklist access denied.");success=false;}
  catch(Exception){foreach(var field in fields)field.Complete.IsEnabled=false;saveSetup.IsEnabled=setupNote.IsEnabled=false;messages.Add("Checklist unavailable. Refresh before saving.");success=false;}
  status.Text=string.Join(" ",messages);return success;
 }
 void SaveChecklist()
 {
  var snapshot=setupSnapshot??throw new ArgumentException("Refresh the checklist.");
  var desired=fields.Select(x=>(x.Complete.IsChecked==true)==x.Original.Complete?x.Original:new ProjectSetupItem(x.Original.Item,x.Complete.IsChecked==true,setupNote.Text)).ToArray();
  if(desired.SequenceEqual(snapshot.Items)){status.Text="No checklist changes.";return;}
  if(string.IsNullOrWhiteSpace(setupNote.Text))throw new ArgumentException("Enter one review note for the changed items.");
  service.SaveProjectSetupChecklist(project,snapshot.Items,desired);if(Refresh(true))status.Text="Checklist saved.";else status.Text="Checklist saved. "+status.Text;
 }
 void Attach()
 {
  var h=handoff??throw new ArgumentException("Refresh project.");
  if(reviewed.IsChecked!=true)throw new ArgumentException("Confirm that the signed document was reviewed.");
  var picker=new OpenFileDialog{Filter="PDF document (*.pdf)|*.pdf",CheckFileExists=true,Multiselect=false};
  if(picker.ShowDialog(this)!=true)return;
  // Copy only bounded bytes; do not execute or render the selected file.
  var source=LocalStoragePath.FilePath(picker.FileName,mustExist:true);using var stream=new FileStream(source,FileMode.Open,FileAccess.Read,FileShare.Read);
  if(stream.Length is <8 or >10485760)throw new ArgumentException("PDF must be 10 MiB or smaller.");
  var bytes=new byte[(int)stream.Length];try{stream.ReadExactly(bytes);if(stream.ReadByte()!=-1)throw new ArgumentException("File changed while reading.");service.RecordReviewedAgreement(project,h.IssuedHash,h.CurrentAgreement?.Id,Path.GetFileName(source),bytes,evidence.Text,true);}finally{CryptographicOperations.ZeroMemory(bytes);}evidence.Clear();startNote.Clear();if(Refresh())status.Text="Agreement recorded.";else status.Text="Agreement recorded. "+status.Text;
 }
 void Authorize()
 {
  var h=handoff??throw new ArgumentException("Refresh project.");var a=h.CurrentAgreement??throw new ArgumentException("Register reviewed agreement.");
  if(MessageBox.Show(this,"Explicitly authorize engineering work for this project based on the registered agreement? This is a separate decision from recording execution.","Authorize project start",MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes)return;
  var outcome=service.AuthorizeProjectStart(project,h.IssuedHash,a.Id,a.PdfHash,true,startNote.Text);if(outcome is "Active" or "AlreadyActive")startNote.Clear();var refreshed=Refresh();status.Text=outcome+(refreshed?"":" — "+status.Text);
 }
}
