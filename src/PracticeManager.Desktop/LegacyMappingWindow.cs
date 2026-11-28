using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using PracticeManager.Persistence;
namespace PracticeManager.Desktop;
internal sealed class LegacyMappingWindow:Window
{
 readonly LocalPractice service;readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap};readonly DataGrid grid=WorkWindowFields.Grid();LegacyCrmHistory? history;
 public LegacyMappingWindow(LocalPractice service)
 {
  this.service=service;Title="Legacy migration evidence";Width=1040;Height=620;WindowStartupLocation=WindowStartupLocation.CenterOwner;
  var root=new DockPanel{Margin=new Thickness(16)};var head=new StackPanel();head.Children.Add(new TextBlock{Text="Legacy ID mappings — read only",FontSize=24,FontWeight=FontWeights.SemiBold});head.Children.Add(new TextBlock{Text="Original source identifiers and new native numbers. Counts reconcile imported rows, not later-added CRM records. This is not an accounting reconciliation.",TextWrapping=TextWrapping.Wrap});head.Children.Add(status);var bar=new WrapPanel();WorkWindowFields.Button(bar,"Refresh",()=>Run(Refresh));WorkWindowFields.Button(bar,"Save confidential mapping CSV",()=>Run(Save));head.Children.Add(bar);DockPanel.SetDock(head,Dock.Top);root.Children.Add(head);
  WorkWindowFields.Columns(grid,("Kind","Kind"),("Legacy ID","LegacyId"),("Legacy number","LegacyNumber"),("Legacy parent","LegacyParentId"),("Native ID","NativeId"),("Native client number","NativeClientNumber"));root.Children.Add(grid);Content=root;Run(Refresh);
 }
 void Run(Action action){try{action();}catch(UnauthorizedAccessException){history=null;grid.ItemsSource=null;status.Text="Access denied.";}catch(ArgumentException e){status.Text=e.Message;}catch(Exception){history=null;grid.ItemsSource=null;status.Text="Mapping unavailable or integrity verification failed." ;}}
 void Refresh(){grid.ItemsSource=null;history=service.ReadLegacyCrmHistory();if(history==null){status.Text="No legacy CRM import recorded.";return;}grid.ItemsSource=history.Mappings;status.Text=$"Clients: {history.Clients}; contacts: {history.Contacts}\nImported UTC: {history.ImportedUtc}; actor ID: {history.ImportedBy}\nSource SHA-256: {history.Sha256}\nReview: {history.ReviewNote}";}
 void Save(){if(history==null)throw new ArgumentException("No imported mapping available.");var picker=new SaveFileDialog{Title="Save confidential legacy mapping",FileName="CRC-legacy-crm-mapping.csv",Filter="CSV (*.csv)|*.csv",DefaultExt=".csv",AddExtension=true,OverwritePrompt=true};if(picker.ShowDialog(this)!=true)return;if(!string.Equals(Path.GetExtension(picker.FileName),".csv",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Use .csv extension.");var csv=service.ExportLegacyCrmMapping(history.Sha256);using var file=new FileStream(picker.FileName,FileMode.CreateNew,FileAccess.Write,FileShare.None);using var writer=new StreamWriter(file,new UTF8Encoding(true));writer.Write(csv);writer.Flush();file.Flush(true);status.Text="Confidential mapping saved. Existing files were not overwritten.";}
}
