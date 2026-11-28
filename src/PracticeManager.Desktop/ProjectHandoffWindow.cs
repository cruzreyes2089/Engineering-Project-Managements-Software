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
 readonly CheckBox reviewed=new(){Content="I reviewed the signatures, parties and terms against the accepted proposal."};
 readonly Button attach=new(){Content="Attach signed agreement"},authorize=new(){Content="Start project"},saveSetup=new(){Content="Save checklist",IsEnabled=false};
 readonly StackPanel checklist=new();
 ProjectHandoff? handoff;
 ProjectSetupReview? setupSnapshot;
 readonly List<(ProjectSetupItem Original,CheckBox Complete,TextBox Note)> fields=new();
 static string SetupLabel(string item)=>item switch{"ScopeReviewed"=>"Scope reviewed","ConflictReviewed"=>"Conflict review completed","ProjectContacts"=>"Project contacts reviewed","DeliveryPlan"=>"Delivery plan reviewed",_=>item};
 public ProjectHandoffWindow(LocalPractice service,long project)
 {
  this.service=service;this.project=project;Title="Project setup";Width=730;Height=720;WindowStartupLocation=WindowStartupLocation.CenterOwner;
  var panel=new StackPanel{Margin=new Thickness(18)};
  panel.Children.Add(new TextBlock{Text="Project setup",FontSize=22,FontWeight=FontWeights.SemiBold});
  panel.Children.Add(new TextBlock{Text="Review the agreement and conflict status before starting work.",TextWrapping=TextWrapping.Wrap});
  panel.Children.Add(summary);var conflict=new Button{Content="Conflict review"};conflict.Click+=(_,_)=>Run(()=>{new ConflictReviewWindow(service,project){Owner=this}.ShowDialog();Refresh();});conflict.IsEnabled=service.Can("Compliance.View");panel.Children.Add(conflict);var parties=new Button{Content="Project contacts",IsEnabled=service.Can("Contacts.View")};parties.Click+=(_,_)=>Run(()=>{new ProjectPartiesWindow(service,project){Owner=this}.ShowDialog();Refresh();});panel.Children.Add(parties);panel.Children.Add(new TextBlock{Text="Agreement or start review note"});panel.Children.Add(evidence);panel.Children.Add(reviewed);panel.Children.Add(attach);panel.Children.Add(authorize);
  panel.Children.Add(new TextBlock{Text="Project setup checklist",FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,12,0,4)});panel.Children.Add(checklist);panel.Children.Add(saveSetup);saveSetup.Click+=(_,_)=>Run(SaveChecklist);panel.Children.Add(status);
  var refresh=new Button{Content="Refresh"};refresh.Click+=(_,_)=>Refresh();panel.Children.Add(refresh);Content=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
  attach.Click+=(_,_)=>Run(Attach);authorize.Click+=(_,_)=>Run(Authorize);Refresh();
 }
 void Run(Action action){try{action();}catch(UnauthorizedAccessException){status.Text="Access denied.";}catch(ArgumentException e){status.Text=e.Message;}catch(Exception){status.Text="Operation failed; refresh before retrying.";}}
 void Refresh()
 {
  handoff=null;setupSnapshot=null;fields.Clear();summary.Text="Agreement information restricted or unavailable.";attach.IsEnabled=authorize.IsEnabled=saveSetup.IsEnabled=false;checklist.Children.Clear();reviewed.IsChecked=false;
  Run(()=>{if(service.Can("Agreements.View")){handoff=service.ReadProjectHandoff(project);var a=handoff.CurrentAgreement;
   summary.Text=$"{handoff.ProjectNumber} — {handoff.State}\n"+(a==null?"No reviewed signed PDF registered.":$"Signed agreement: {a.FileName}");
   var conflictState=service.ReadProjectConflictStatus(project);summary.Text+="\nConflict review: "+conflictState;attach.IsEnabled=handoff.State=="Pending"&&service.Can("Agreements.Record");authorize.IsEnabled=handoff.State=="Pending"&&a!=null&&conflictState=="Clear"&&service.Can("Projects.Authorize");}});
  Run(()=>{var setup=service.ReadProjectSetup(project);setupSnapshot=setup;checklist.Children.Add(new TextBlock{Text="Setup: "+setup.State});var editable=service.Can("Projects.Setup");
   foreach(var item in setup.Items){var row=new StackPanel{Margin=new Thickness(0,4,0,4)};var check=new CheckBox{Content=SetupLabel(item.Item),IsChecked=item.Complete,IsEnabled=editable};var note=new TextBox{Text=item.Note,MaxLength=1000,IsReadOnly=!editable};row.Children.Add(check);row.Children.Add(new Expander{Header="Review note",Content=note,IsExpanded=false});checklist.Children.Add(row);fields.Add((item,check,note));}
   saveSetup.IsEnabled=editable;});
 }
 void SaveChecklist()
 {
  var snapshot=setupSnapshot??throw new ArgumentException("Refresh the checklist.");
  var desired=fields.Select(x=>new ProjectSetupItem(x.Original.Item,x.Complete.IsChecked==true,x.Note.Text)).ToArray();
  foreach(var item in desired.Where((x,i)=>x!=snapshot.Items[i]))if(string.IsNullOrWhiteSpace(item.Note))throw new ArgumentException("Enter a review note for "+SetupLabel(item.Item)+".");
  service.SaveProjectSetupChecklist(project,snapshot.Items,desired);Refresh();status.Text="Checklist saved.";
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
  var bytes=new byte[(int)stream.Length];try{stream.ReadExactly(bytes);if(stream.ReadByte()!=-1)throw new ArgumentException("File changed while reading.");service.RecordSignedAgreement(project,h.IssuedHash,Path.GetFileName(source),bytes,evidence.Text,true);}finally{CryptographicOperations.ZeroMemory(bytes);}evidence.Clear();Refresh();
 }
 void Authorize()
 {
  var h=handoff??throw new ArgumentException("Refresh project.");var a=h.CurrentAgreement??throw new ArgumentException("Register reviewed agreement.");
  if(MessageBox.Show(this,"Explicitly authorize engineering work for this project based on the registered agreement? This is a separate decision from recording execution.","Authorize project start",MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes)return;
  var outcome=service.AuthorizeProjectStart(project,h.IssuedHash,a.Id,a.PdfHash,true,evidence.Text);evidence.Clear();Refresh();status.Text=outcome;
 }
}
