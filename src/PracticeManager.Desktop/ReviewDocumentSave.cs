using System.IO;
using System.Windows;
using Microsoft.Win32;
using PracticeManager.Persistence;
namespace PracticeManager.Desktop;
internal static class ReviewDocumentSave
{
 public static void Save(Window owner,GeneratedReviewDocument doc)
 {
  var picker=new SaveFileDialog{Title="Save internal review copy",FileName=doc.FileName,DefaultExt=".docx",Filter="Word document (*.docx)|*.docx",AddExtension=true,OverwritePrompt=true};
  if(picker.ShowDialog(owner)!=true)return;
  if(!string.Equals(Path.GetExtension(picker.FileName),".docx",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Keep the .docx extension.");
  if(File.Exists(picker.FileName)||Directory.Exists(picker.FileName))throw new ArgumentException("Choose a new file name. Existing files are never overwritten.");
  using(var stream=new FileStream(picker.FileName,FileMode.CreateNew,FileAccess.Write,FileShare.None)){stream.Write(doc.Bytes);stream.Flush(true);}
  MessageBox.Show(owner,"Registered review copy saved. No file was opened or sent.\nSHA-256: "+doc.Sha256,"Review document",MessageBoxButton.OK,MessageBoxImage.Information);
 }
}
