using System.IO;
using System.Security.Cryptography;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using PracticeManager.Core;
using PracticeManager.Persistence;
namespace PracticeManager.Desktop;
internal sealed class PartyDeclarationWindow:Window
{
 readonly LocalPractice service;readonly long project;readonly ProjectParty party;
 readonly DataGrid grid=WorkWindowFields.Grid();readonly ComboBox document=new(){DisplayMemberPath="FileName"},decision=new(){ItemsSource=new[]{"Unverified","Concern","ReviewedNoConflict"},SelectedIndex=0};
 readonly TextBox note=new(){MaxLength=4000,Height=85,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap};readonly CheckBox confirmed=new(){Content="I reviewed the signed declaration, identity, project and restrictions."};readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap};long? expected;
 public PartyDeclarationWindow(LocalPractice service,long project,ProjectParty party)
 {
  this.service=service;this.project=project;this.party=party;Title="Participant declarations — restricted";Width=970;Height=680;WindowStartupLocation=WindowStartupLocation.CenterOwner;
  var root=new DockPanel{Margin=new Thickness(16)};var p=new StackPanel();
  p.Children.Add(new TextBlock{Text=party.PartyName+" — "+party.Role,FontSize=22,FontWeight=FontWeights.SemiBold});
  p.Children.Add(new TextBlock{Text="Choose or add a signed declaration PDF here. Review the signed document before recording your decision. A new declaration requires renewed project screening.",TextWrapping=TextWrapping.Wrap});p.Children.Add(status);
  p.Children.Add(new TextBlock{Text="Current restricted Contracts PDF"});p.Children.Add(document);p.Children.Add(decision);p.Children.Add(note);p.Children.Add(confirmed);
  var bar=new WrapPanel();if(party.State=="Active"&&service.Can("Compliance.Record")&&service.Can("Documents.Restricted")&&service.CanRegisterProjectDocuments(project))WorkWindowFields.Button(bar,"Add declaration PDF",()=>Run(AddPdf));WorkWindowFields.Button(bar,"Refresh",()=>Run(Refresh));if(service.Can("Compliance.Record")&&party.State=="Active")WorkWindowFields.Button(bar,"Record declaration review",()=>Run(Save));p.Children.Add(bar);
  DockPanel.SetDock(p,Dock.Top);root.Children.Add(p);WorkWindowFields.Columns(grid,("Review","Id"),("Document","DocumentId"),("Hash","DocumentHash"),("Decision","Decision"),("Current","CurrentStatus"),("Note","Note"),("Reviewer","ReviewerId"),("UTC","ReviewedUtc"));root.Children.Add(grid);Content=root;Run(Refresh);
 }
 void Run(Action action){try{action();}catch(UnauthorizedAccessException){grid.ItemsSource=null;document.ItemsSource=null;status.Text="Access denied.";}catch(ArgumentException e){status.Text=e.Message;}catch(Exception){status.Text="Declaration review unavailable.";}}
 void Refresh(){grid.ItemsSource=null;document.ItemsSource=null;var rows=service.ReadPartyDeclarations(project,party.Id);expected=rows.FirstOrDefault()?.Id;grid.ItemsSource=rows;document.ItemsSource=service.ReadProjectDocuments(project).Where(d=>d.Classification=="Restricted"&&d.Category=="Contracts"&&d.FileName.EndsWith(".pdf",StringComparison.OrdinalIgnoreCase)).ToArray();status.Text="Current declaration: "+(rows.FirstOrDefault()?.CurrentStatus??"Missing");decision.SelectedIndex=0;note.Clear();confirmed.IsChecked=false;}
 void AddPdf()
 {
  var picker=new OpenFileDialog{Title="Choose signed declaration PDF",Filter="PDF document (*.pdf)|*.pdf",CheckFileExists=true,Multiselect=false};if(picker.ShowDialog(this)!=true)return;
  if(!service.Can("Compliance.Record")||!service.Can("Documents.Restricted")||!service.CanRegisterProjectDocuments(project))throw new UnauthorizedAccessException();
  var source=LocalStoragePath.FilePath(picker.FileName,mustExist:true);using var stream=new FileStream(source,FileMode.Open,FileAccess.Read,FileShare.Read);
  if(stream.Length is <5 or >20971520)throw new ArgumentException("Declaration PDF must be 20 MiB or smaller.");var bytes=new byte[(int)stream.Length];long id;
  try{stream.ReadExactly(bytes);if(stream.ReadByte()!=-1||!bytes.AsSpan(0,5).SequenceEqual("%PDF-"u8))throw new ArgumentException("Choose a valid PDF file.");id=service.RegisterProjectDocument(project,Guid.NewGuid(),null,"Restricted","Contracts",Path.GetFileName(source),bytes);}finally{CryptographicOperations.ZeroMemory(bytes);}
  try{Refresh();document.SelectedItem=((IEnumerable<ProjectDocument>)document.ItemsSource).FirstOrDefault(x=>x.Id==id);status.Text="Declaration PDF added. Review it and record your decision; clearance has not been granted.";}catch(UnauthorizedAccessException){grid.ItemsSource=null;document.ItemsSource=null;status.Text="PDF added; access changed. No declaration review recorded.";}catch(Exception){grid.ItemsSource=null;document.ItemsSource=null;status.Text="PDF added; refresh failed. Do not add it again. No declaration review recorded.";}
 }
 void Save(){if(document.SelectedItem is not ProjectDocument d||decision.SelectedItem is not string choice)throw new ArgumentException("Select declaration PDF and decision.");service.RecordPartyDeclaration(project,party.Id,expected,d.Id,d.Hash,choice,note.Text,confirmed.IsChecked==true);Refresh();}
}
