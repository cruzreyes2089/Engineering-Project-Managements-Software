using System.IO;
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
 readonly Button attach=new(){Content="Register reviewed signed PDF"},authorize=new(){Content="Authorize project start"};
 readonly StackPanel checklist=new();
 ProjectHandoff? handoff;
 public ProjectHandoffWindow(LocalPractice service,long project)
 {
  this.service=service;this.project=project;Title="Project handoff and setup — development";Width=730;Height=720;WindowStartupLocation=WindowStartupLocation.CenterOwner;
  var panel=new StackPanel{Margin=new Thickness(18)};
  panel.Children.Add(new TextBlock{Text="Project handoff and setup",FontSize=22,FontWeight=FontWeights.SemiBold});
  panel.Children.Add(new TextBlock{Text="Synthetic only. The registered PDF is an immutable stored copy. This application does not validate digital signatures or legal enforceability.",TextWrapping=TextWrapping.Wrap});
  panel.Children.Add(summary);var conflict=new Button{Content="Conflict screening and history"};conflict.Click+=(_,_)=>Run(()=>{new ConflictReviewWindow(service,project){Owner=this}.ShowDialog();Refresh();});conflict.IsEnabled=service.Can("Compliance.View");panel.Children.Add(conflict);panel.Children.Add(new TextBlock{Text="Agreement review or start authorization evidence"});panel.Children.Add(evidence);panel.Children.Add(reviewed);panel.Children.Add(attach);panel.Children.Add(authorize);
  panel.Children.Add(new TextBlock{Text="Project setup checklist",FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,12,0,4)});panel.Children.Add(checklist);panel.Children.Add(status);
  var refresh=new Button{Content="Refresh"};refresh.Click+=(_,_)=>Refresh();panel.Children.Add(refresh);Content=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
  attach.Click+=(_,_)=>Run(Attach);authorize.Click+=(_,_)=>Run(Authorize);Refresh();
 }
 void Run(Action action){try{action();}catch(UnauthorizedAccessException){status.Text="Access denied.";}catch(ArgumentException e){status.Text=e.Message;}catch(Exception){status.Text="Operation failed; refresh before retrying.";}}
 void Refresh()
 {
  handoff=null;summary.Text="Agreement information restricted or unavailable.";attach.IsEnabled=authorize.IsEnabled=false;checklist.Children.Clear();reviewed.IsChecked=false;
  Run(()=>{if(service.Can("Agreements.View")){handoff=service.ReadProjectHandoff(project);var a=handoff.CurrentAgreement;
   summary.Text=$"{handoff.ProjectNumber} — {handoff.State}\n"+(a==null?"No reviewed signed PDF registered.":$"Registered copy: {a.FileName}\nSHA-256: {a.PdfHash}");
   summary.Text+="\nConflict screening: "+service.ReadProjectConflictStatus(project);attach.IsEnabled=handoff.State=="Pending"&&service.Can("Agreements.Record");authorize.IsEnabled=handoff.State=="Pending"&&a!=null&&service.Can("Projects.Authorize");}});
  Run(()=>{var setup=service.ReadProjectSetup(project);checklist.Children.Add(new TextBlock{Text="Setup: "+setup.State});
   foreach(var item in setup.Items){var row=new DockPanel{Margin=new Thickness(0,4,0,4)};var save=new Button{Content="Record",IsEnabled=service.Can("Projects.Setup")};DockPanel.SetDock(save,Dock.Right);row.Children.Add(save);
    var fields=new StackPanel();var check=new CheckBox{Content=item.Item,IsChecked=item.Complete,IsEnabled=save.IsEnabled};var note=new TextBox{Text=item.Note,MaxLength=1000,IsReadOnly=!save.IsEnabled};fields.Children.Add(check);fields.Children.Add(note);row.Children.Add(fields);checklist.Children.Add(row);
    save.Click+=(_,_)=>Run(()=>{service.RecordProjectSetupItem(project,item.Item,check.IsChecked==true,note.Text,item.Complete,item.Note);Refresh();});}});
 }
 void Attach()
 {
  var h=handoff??throw new ArgumentException("Refresh project.");
  if(reviewed.IsChecked!=true)throw new ArgumentException("Confirm that the signed document was reviewed.");
  var picker=new OpenFileDialog{Filter="PDF document (*.pdf)|*.pdf",CheckFileExists=true,Multiselect=false};
  if(picker.ShowDialog(this)!=true)return;
  // Copy only bounded bytes; do not execute or render the selected file.
  using var stream=new FileStream(picker.FileName,FileMode.Open,FileAccess.Read,FileShare.Read);
  if(stream.Length is <8 or >10485760)throw new ArgumentException("PDF must be 10 MiB or smaller.");
  var bytes=new byte[(int)stream.Length];stream.ReadExactly(bytes);if(stream.ReadByte()!=-1)throw new ArgumentException("File changed while reading.");
  service.RecordSignedAgreement(project,h.IssuedHash,Path.GetFileName(picker.FileName),bytes,evidence.Text,true);evidence.Clear();Refresh();
 }
 void Authorize()
 {
  var h=handoff??throw new ArgumentException("Refresh project.");var a=h.CurrentAgreement??throw new ArgumentException("Register reviewed agreement.");
  if(MessageBox.Show(this,"Explicitly authorize engineering work for this project based on the registered agreement? This is a separate decision from recording execution.","Authorize project start",MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes)return;
  var outcome=service.AuthorizeProjectStart(project,h.IssuedHash,a.Id,a.PdfHash,true,evidence.Text);evidence.Clear();Refresh();status.Text=outcome;
 }
}
