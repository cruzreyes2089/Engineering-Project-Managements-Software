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
 readonly List<StoredTransmittal> loaded=new();bool closed,ready;
 readonly Button prepareButton=new(){Content="Record preparation",IsEnabled=false};
 readonly Dictionary<string,Button> eventButtons=new();
 public TransmittalAcceptanceWindow(LocalPractice service,long project)
 {
  this.service=service;this.project=project;Title="SYNTHETIC — Transmittal center";Width=1100;Height=850;WindowStartupLocation=WindowStartupLocation.CenterOwner;
  var body=new StackPanel{Margin=new Thickness(16)};
  void Label(string s)=>body.Children.Add(new TextBlock{Text=s,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,3,0,3)});
  Label("Synthetic attestations only. Select exact current production PDF versions. This window does not send, sign, stamp or authorize any actual document.");
  var refresh=new WrapPanel();WorkWindowFields.Button(refresh,"Reload register",()=>Refresh());body.Children.Add(refresh);
  documents.Height=145;documents.SelectionMode=DataGridSelectionMode.Extended;WorkWindowFields.Columns(documents,("ID","Id"),("File","FileName"),("Category","Category"),("SHA-256","Hash"));body.Children.Add(documents);
  Label("Proposed recipient");body.Children.Add(recipient);Label("Purpose");body.Children.Add(purpose);Label("External evidence reference (synthetic only)");body.Children.Add(evidence);Label("Review note");body.Children.Add(note);
  var prepare=new WrapPanel();prepareButton.Click+=(_,_)=>Prepare();prepare.Children.Add(prepareButton);body.Children.Add(prepare);
  Label("Recorded transmittals");body.Children.Add(registers);registers.SelectionChanged+=(_,_)=>ShowHistory();
  Label("Selected transmittal document manifest");manifest.Height=100;WorkWindowFields.Columns(manifest,("ID","Id"),("File","FileName"),("Category","Category"),("SHA-256","Sha256"));body.Children.Add(manifest);
  history.Height=150;WorkWindowFields.Columns(history,("Sequence","Sequence"),("State","Event.State"),("Evidence","Event.EvidenceRef"),("Recorded UTC","Event.RecordedUtc"),("Actor","Event.Actor"));body.Children.Add(history);
  var actions=new WrapPanel();foreach(var (label,state) in new[]{("Record issue","IssueRecorded"),("Record delivery","DeliveryRecorded"),("Record withdrawal","Withdrawn")}){var b=new Button{Content=label,IsEnabled=false,Margin=new Thickness(2)};b.Click+=(_,_)=>Append(state);eventButtons.Add(state,b);actions.Children.Add(b);}body.Children.Add(actions);body.Children.Add(status);
  Content=new ScrollViewer{Content=body};documents.SelectionChanged+=(_,_)=>UpdateActions();foreach(var field in new[]{recipient,purpose,evidence,note})field.TextChanged+=(_,_)=>UpdateActions();Closed+=(_,_)=>{closed=true;Clear();};Refresh();
 }
 StoredTransmittal? Selected()=>registers.SelectedIndex>=0&&registers.SelectedIndex<loaded.Count?loaded[registers.SelectedIndex]:null;
 void UpdateActions(){var row=Selected();var valid=ready&&!closed&&!string.IsNullOrWhiteSpace(evidence.Text)&&!string.IsNullOrWhiteSpace(note.Text);prepareButton.IsEnabled=valid&&row==null&&documents.SelectedItems.Count is >=1 and <=50&&!string.IsNullOrWhiteSpace(recipient.Text)&&!string.IsNullOrWhiteSpace(purpose.Text);var allowed=DocumentTransmittalReview.AvailableAttestations(row?.History.LastOrDefault()?.Event.State);foreach(var entry in eventButtons)entry.Value.IsEnabled=valid&&row!=null&&allowed.Contains(entry.Key);}
 void Clear(){ready=false;loaded.Clear();documents.ItemsSource=null;registers.ItemsSource=null;history.ItemsSource=null;manifest.ItemsSource=null;recipient.Clear();purpose.Clear();evidence.Clear();note.Clear();status.Text="";UpdateActions();}
 bool Refresh()
 {
  Clear();if(closed)return false;
  try
  {
   loaded.AddRange(service.ReviewAcceptanceTransmittals(project));
   registers.ItemsSource=loaded.Select(x=>$"{x.Id} — {x.Intent.Purpose} — {x.Intent.Recipient} — {x.History.Last().Event.State}").ToArray();
   documents.ItemsSource=service.ReadProjectDocuments(project).Where(d=>d.Classification=="Production"&&d.Category is ("Drawings" or "Calculations" or "Reports")&&d.FileName.EndsWith(".pdf",StringComparison.OrdinalIgnoreCase)).ToArray();
   ready=true;UpdateActions();status.Text="Select PDFs for preparation, or select a recorded transmittal for an attestation. Reload to clear the selection.";return true;
  }
  catch(UnauthorizedAccessException){Clear();status.Text="Access denied.";}
  catch(Exception){Clear();status.Text="Transmittal register unavailable.";}
  return false;
 }
 void ShowHistory()
 {
  var row=Selected();
  history.ItemsSource=row?.History;manifest.ItemsSource=row?.Intent.Documents;evidence.Clear();note.Clear();UpdateActions();
 }
 bool Confirm(string text)=>MessageBox.Show(this,text,"Confirm synthetic attestation",MessageBoxButton.YesNo,MessageBoxImage.Warning)==MessageBoxResult.Yes&&!closed;
 void Act(Action a)
 {
  try{a();}
  catch(UnauthorizedAccessException){Clear();status.Text="Access denied.";}
  catch(ArgumentException e){Clear();status.Text=e.Message+" Reload register before retrying.";}
  catch(Exception){Clear();status.Text="Record not confirmed; reload and reconcile before retrying.";}
 }
 void Prepare()=>Act(()=>{
  if(!prepareButton.IsEnabled)throw new ArgumentException("Select preparation context and complete evidence fields.");
  var intent=DocumentTransmittalReview.Prepare(project,recipient.Text,purpose.Text,documents.SelectedItems.Cast<ProjectDocument>().Select(d=>new ReviewedIssueDocument(d.Id,d.Hash,d.FileName,d.Category,d.Bytes)).ToArray());
  var e=evidence.Text;var n=note.Text;
  if(!Confirm($"Record preparation of {intent.Documents.Count} PDF(s) for {intent.Recipient}? No actual issuance."))return;
  var saved=service.PrepareAcceptanceTransmittal(Guid.NewGuid(),intent,e,n,true);if(!closed){if(Refresh())status.Text=$"Preparation recorded: {saved.Id}.";else status.Text=$"Preparation recorded: {saved.Id}. Reload failed; do not repeat.";}
 });
 void Append(string state)=>Act(()=>{
  if(!eventButtons.TryGetValue(state,out var button)||!button.IsEnabled)throw new ArgumentException("Select an allowed attestation and complete evidence fields.");
  var row=Selected()??throw new ArgumentException("Select a transmittal.");var e=evidence.Text;var n=note.Text;var expected=row.History.Last().Event.Hash;
  if(!Confirm($"Record {state} for synthetic transmittal {row.Id}? This is an attestation, not actual delivery or approval."))return;
  service.SaveAcceptanceTransmittalEvent(project,row.Id,Guid.NewGuid(),expected,state,e,n,true);if(!closed){if(Refresh())status.Text="Synthetic attestation recorded.";else status.Text="Synthetic attestation recorded. Reload failed; do not repeat.";}
 });
}
