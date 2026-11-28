using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using PracticeManager.Core;
using PracticeManager.Persistence;
namespace PracticeManager.Desktop;
internal sealed class LegacyCrmImportWindow:Window
{
 readonly LocalPractice service;readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap};readonly Button import=new(){Content="Commit reviewed CRM import",IsEnabled=false};
 readonly TextBox note=new(){MaxLength=1000,AcceptsReturn=true,Height=100,TextWrapping=TextWrapping.Wrap};
 readonly CheckBox confirmed=new(){Content="I verified the original source mapping, row counts, duplicate groups and target recovery copy."};byte[]? data;LegacyCrmPreview? preview;
 public LegacyCrmImportWindow(LocalPractice service)
 {
  this.service=service;Title="Legacy CRM transfer — controlled import";Width=740;Height=560;WindowStartupLocation=WindowStartupLocation.CenterOwner;
  var p=new StackPanel{Margin=new Thickness(20)};p.Children.Add(new TextBlock{Text=Title,FontSize=24,FontWeight=FontWeights.SemiBold});p.Children.Add(new TextBlock{Text="Use a reviewed CRC-Legacy-CRM-v1 JSON transfer exported from a trusted copy of the legacy database. This is not a direct Access importer. Only clients and contacts are imported into an empty CRM; no financial records, passwords, staff or projects. New native client numbers are assigned; original IDs/numbers are retained as migration evidence. Duplicate name/number groups are never merged.",TextWrapping=TextWrapping.Wrap});
  var pick=new Button{Content="Select and validate transfer"};pick.Click+=(_,_)=>Run(Select);p.Children.Add(pick);var history=new Button{Content="View imported ID mappings"};history.Click+=(_,_)=>Run(()=>new LegacyMappingWindow(service){Owner=this}.ShowDialog());p.Children.Add(history);p.Children.Add(status);p.Children.Add(new TextBlock{Text="Review evidence (source mapping, counts and backup)"});p.Children.Add(note);p.Children.Add(confirmed);import.Click+=(_,_)=>Run(Import);p.Children.Add(import);Content=new ScrollViewer{Content=p};Closed+=(_,_)=>Clear();
 }
 void Clear(){if(data!=null)Array.Clear(data);data=null;preview=null;import.IsEnabled=false;confirmed.IsChecked=false;note.Clear();}
 void Run(Action action){try{action();}catch(UnauthorizedAccessException){Clear();status.Text="Access denied.";}catch(ArgumentException e){status.Text=e.Message;}catch(Exception){Clear();status.Text="Transfer operation failed. No successful import assumed; verify target state." ;}}
 void Select()
 {
  Clear();status.Text="";var picker=new OpenFileDialog{Title="Select trusted normalized CRM transfer",Filter="JSON (*.json)|*.json",CheckFileExists=true};if(picker.ShowDialog(this)!=true)return;
  using var file=new FileStream(picker.FileName,FileMode.Open,FileAccess.Read,FileShare.Read);if(file.Length is <1 or >LegacyCrmTransfer.MaxBytes)throw new ArgumentException("Transfer outside supported size.");
  data=new byte[(int)file.Length];file.ReadExactly(data);if(file.ReadByte()!=-1)throw new ArgumentException("Transfer changed during read.");
  preview=service.PreviewLegacyCrm(data);status.Text=$"Target: {preview.TargetStatus}\nClients: {preview.Clients}; contacts: {preview.Contacts}\nDuplicate client-name groups: {preview.DuplicateNameGroups}; duplicate legacy-number groups: {preview.DuplicateNumberGroups}\nSHA-256: {preview.Sha256}\nValidation only. No rows written.";
  import.IsEnabled=preview.TargetStatus=="ReadyForExplicitReview";
 }
 void Import()
 {
  if(data==null||preview==null||confirmed.IsChecked!=true)throw new ArgumentException("Validate transfer and confirm review first.");
  if(MessageBox.Show(this,"Commit this exact validated package to the empty CRM? This is a one-time, non-merging import.","Confirm legacy CRM import",MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes)return;
  var r=service.ImportLegacyCrm(data,preview.Sha256,true,note.Text);Clear();status.Text=$"{r.Outcome}: {r.Clients} clients, {r.Contacts} contacts.\nSHA-256: {r.Sha256}";
 }
}
