using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using PracticeManager.Core;
using PracticeManager.Persistence;
namespace PracticeManager.Desktop;
internal sealed class HistoricalArchivePreviewWindow:Window
{
 readonly LocalPractice service;
 readonly string?[] paths=new string?[3];
 readonly TextBox[] hashes=Enumerable.Range(0,4).Select(_=>new TextBox{MaxLength=64,Margin=new Thickness(0,2,0,8)}).ToArray();
 readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,0)};
 public HistoricalArchivePreviewWindow(LocalPractice service)
 {
  this.service=service;Title="Historical project transfer — validation only";Width=760;Height=690;MinHeight=480;WindowStartupLocation=WindowStartupLocation.CenterOwner;ShowInTaskbar=false;
  var p=new StackPanel{Margin=new Thickness(20)};
  p.Children.Add(new TextBlock{Text=Title,FontSize=23,FontWeight=FontWeights.SemiBold});
  p.Children.Add(new TextBlock{Text="Select the reviewed CRM transfer, historical project transfer and independent project census. Enter the four SHA-256 digests from your separately retained trusted record. This validates an existing CRM mapping and historical identities; it does not prove source authenticity, import records, authorize work or create invoices.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,4,0,12)});
  string[] labels=["CRM transfer JSON","Historical project transfer JSON","Independent project census JSON"];
  for(int i=0;i<labels.Length;i++)
  {
   int index=i;
   var button=new Button{Content="Select "+labels[i],Margin=new Thickness(0,2,0,2)};
   var chosen=new TextBlock{Text="Not selected",TextWrapping=TextWrapping.Wrap};
   button.Click+=(_,_)=>{var picker=new OpenFileDialog{Title=labels[index],Filter="JSON (*.json)|*.json",CheckFileExists=true};if(picker.ShowDialog(this)==true){paths[index]=picker.FileName;chosen.Text="Selected (confidential local file)";status.Text="Inputs changed. Validate again."; }};
   p.Children.Add(button);p.Children.Add(chosen);
  }
  string[] digestLabels=["Original source SHA-256","CRM transfer SHA-256","Historical project transfer SHA-256","Independent census SHA-256"];
  for(int i=0;i<hashes.Length;i++)
  {
   p.Children.Add(new TextBlock{Text=digestLabels[i]});p.Children.Add(hashes[i]);
   hashes[i].TextChanged+=(_,_)=>status.Text="Inputs changed. Validate again.";
  }
  var bar=new WrapPanel();WorkWindowFields.Button(bar,"Validate exact package",Validate);WorkWindowFields.Button(bar,"Close",Close);
  p.Children.Add(bar);p.Children.Add(status);Content=new ScrollViewer{Content=p,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
  Closed+=(_,_)=>{Array.Clear(paths);foreach(var hash in hashes)hash.Clear();status.Text="";};
 }
 void Validate()
 {
  status.Text="";var bytes=new byte[3][];
  try
  {
   if(!service.Can("Administration.Manage")||!service.Can("Administration.HistoricalProjectImport")||!service.Can("HistoricalProjects.View")||!service.Can("Clients.View"))throw new UnauthorizedAccessException();
   if(paths.Any(x=>x is null))throw new ArgumentException("Select all three reviewed transfer files.");
   var values=hashes.Select(x=>x.Text.Trim().ToLowerInvariant()).ToArray();
   for(int i=0;i<3;i++)bytes[i]=HistoricalArchivePreviewFiles.Read(paths[i]!,values[i+1]);
   var preview=service.PreviewHistoricalProjects(bytes[0],bytes[1],bytes[2],values[0],values[1],values[2],values[3]);
   status.Text=$"Target: {preview.TargetStatus}\nHistorical records: {preview.Projects:N0}\nRecords linked to inactive source clients: {preview.InactiveSourceClientProjects:N0}\nDuplicate project number groups: {preview.DuplicateNumberGroups:N0}\nNo records imported. No current work authorization. Source authenticity not independently verified by the application.";
  }
  catch(UnauthorizedAccessException){status.Text="Access denied. Sign in again or request authorized access.";}
  catch(ArgumentException e){status.Text=e.Message;}
  catch(Exception){status.Text="Validation failed. Check the reviewed package and CRM mapping; no successful operation is assumed.";}
  finally{foreach(var data in bytes)if(data is not null)CryptographicOperations.ZeroMemory(data);}
 }
}
