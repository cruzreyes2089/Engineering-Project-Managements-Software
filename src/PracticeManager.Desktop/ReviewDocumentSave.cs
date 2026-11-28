using System.IO;
using System.Windows;
using Microsoft.Win32;
using PracticeManager.Persistence;
namespace PracticeManager.Desktop;
internal static class ReviewDocumentSave
{
 public static void Save(Window owner,string proposedName,Func<GeneratedReviewDocument> retrieve)
 {
  var picker=new SaveFileDialog{Title="Save internal review copy",FileName=proposedName,DefaultExt=".docx",Filter="Word document (*.docx)|*.docx",AddExtension=true,OverwritePrompt=true};
  if(picker.ShowDialog(owner)!=true)return;
  if(!string.Equals(Path.GetExtension(picker.FileName),".docx",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Keep the .docx extension.");
  _=PracticeManager.Core.LocalStoragePath.FilePath(picker.FileName,mustExist:false,mustBeNew:true,rejectSidecars:true);
  var doc=retrieve(); // Recheck current source and permission after the modal file picker.
  VerifiedDocumentFile.Save(picker.FileName,".docx",doc.Bytes,doc.Sha256);
  MessageBox.Show(owner,"Registered review copy saved. No file was opened or sent.\nSHA-256: "+doc.Sha256,"Review document",MessageBoxButton.OK,MessageBoxImage.Information);
 }
}
