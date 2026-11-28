using System.Windows;
using System.Windows.Controls;
using PracticeManager.Core;
using PracticeManager.Persistence;
namespace PracticeManager.Desktop;
internal sealed class TransmittalAcceptanceWindow:Window
{
 readonly LocalPractice service;readonly long project;
 readonly DataGrid documents=WorkWindowFields.Grid(),history=WorkWindowFields.Grid(),manifest=WorkWindowFields.Grid();
 readonly ListBox registers=new(){Height=100};
 readonly TextBox recipient=new(){MaxLength=200},purpose=new(){MaxLength=200},evidence=new(){MaxLength=200},note=new(){MaxLength=1000};
 readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap};
 readonly List<StoredTransmittal> loaded=new();bool closed;
 public TransmittalAcceptanceWindow(LocalPractice service,long project)
 {
  this.service=service;this.project=project;Title="SYNTHETIC — Transmittal center";Width=1100;Height=850;WindowStartupLocation=WindowStartupLocation.CenterOwner;
  var body=new StackPanel{Margin=new Thickness(16)};
  void Label(string s)=>body.Children.Add(new TextBlock{Text=s,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,3,0,3)});
  Label("Synthetic attestations only. Select exact current production PDF versions. This window does not send, sign, stamp or authorize any actual document.");
  var refresh=new WrapPanel();WorkWindowFields.Button(refresh,"Reload register",()=>Refresh());body.Children.Add(refresh);
  documents.Height=145;documents.SelectionMode=DataGridSelectionMode.Extended;WorkWindowFields.Columns(documents,("ID","Id"),("File","FileName"),("Category","Category"),("SHA-256","Hash"));body.Children.Add(documents);
  Label("Proposed recipient");body.Children.Add(recipient);Label("Purpose");body.Children.Add(purpose);Label("External evidence reference (synthetic only)");body.Children.Add(evidence);Label("Review note");body.Children.Add(note);
  var prepare=new WrapPanel();WorkWindowFields.Button(prepare,"Record preparation",Prepare);body.Children.Add(prepare);
  Label("Recorded transmittals");body.Children.Add(registers);registers.SelectionChanged+=(_,_)=>ShowHistory();
  Label("Selected transmittal document manifest");manifest.Height=100;WorkWindowFields.Columns(manifest,("ID","Id"),("File","FileName"),("Category","Category"),("SHA-256","Sha256"));body.Children.Add(manifest);
  history.Height=150;WorkWindowFields.Columns(history,("Sequence","Sequence"),("State","Event.State"),("Evidence","Event.EvidenceRef"),("Recorded UTC","Event.RecordedUtc"),("Actor","Event.Actor"));body.Children.Add(history);
  var actions=new WrapPanel();foreach(var (label,state) in new[]{("Record issue","IssueRecorded"),("Record delivery","DeliveryRecorded"),("Record withdrawal","Withdrawn")})WorkWindowFields.Button(actions,label,()=>Append(state));body.Children.Add(actions);body.Children.Add(status);
  Content=new ScrollViewer{Content=body};Closed+=(_,_)=>{closed=true;Clear();};Refresh();
 }
 void Clear(){loaded.Clear();documents.ItemsSource=null;registers.ItemsSource=null;history.ItemsSource=null;manifest.ItemsSource=null;recipient.Clear();purpose.Clear();evidence.Clear();note.Clear();status.Text="";}
 bool Refresh()
 {
  Clear();if(closed)return false;
  try
  {
   loaded.AddRange(service.ReviewAcceptanceTransmittals(project));
   registers.ItemsSource=loaded.Select(x=>$"{x.Id} — {x.Intent.Purpose} — {x.Intent.Recipient} — {x.History.Last().Event.State}").ToArray();
   documents.ItemsSource=service.ReadProjectDocuments(project).Where(d=>d.Classification=="Production"&&d.Category is ("Drawings" or "Calculations" or "Reports")&&d.FileName.EndsWith(".pdf",StringComparison.OrdinalIgnoreCase)).ToArray();
   return true;
  }
  catch(UnauthorizedAccessException){Clear();status.Text="Access denied.";}
  catch(Exception){Clear();status.Text="Transmittal register unavailable.";}
  return false;
 }
 void ShowHistory()
 {
  var row=registers.SelectedIndex>=0&&registers.SelectedIndex<loaded.Count?loaded[registers.SelectedIndex]:null;
  history.ItemsSource=row?.History;manifest.ItemsSource=row?.Intent.Documents;evidence.Clear();note.Clear();
 }
 bool Confirm(string text)=>MessageBox.Show(this,text,"Confirm synthetic attestation",MessageBoxButton.YesNo,MessageBoxImage.Warning)==MessageBoxResult.Yes&&!closed;
 void Act(Action a)
 {
  try{a();}
  catch(UnauthorizedAccessException){Clear();status.Text="Access denied.";}
  catch(ArgumentException e){status.Text=e.Message;}
  catch(Exception){Clear();status.Text="Record not confirmed; reload and reconcile before retrying.";}
 }
 void Prepare()=>Act(()=>{
  var intent=DocumentTransmittalReview.Prepare(project,recipient.Text,purpose.Text,documents.SelectedItems.Cast<ProjectDocument>().Select(d=>new ReviewedIssueDocument(d.Id,d.Hash,d.FileName,d.Category,d.Bytes)).ToArray());
  var e=evidence.Text;var n=note.Text;
  if(!Confirm($"Record preparation of {intent.Documents.Count} PDF(s) for {intent.Recipient}? No actual issuance."))return;
  service.PrepareAcceptanceTransmittal(Guid.NewGuid(),intent,e,n,true);if(Refresh())status.Text="Preparation recorded.";
 });
 void Append(string state)=>Act(()=>{
  if(registers.SelectedIndex<0||registers.SelectedIndex>=loaded.Count)throw new ArgumentException("Select a transmittal.");
  var row=loaded[registers.SelectedIndex];var e=evidence.Text;var n=note.Text;var expected=row.History.Last().Event.Hash;
  if(!Confirm($"Record {state} for synthetic transmittal {row.Id}? This is an attestation, not actual delivery or approval."))return;
  service.SaveAcceptanceTransmittalEvent(project,row.Id,Guid.NewGuid(),expected,state,e,n,true);if(Refresh())status.Text="Synthetic attestation recorded.";
 });
}
