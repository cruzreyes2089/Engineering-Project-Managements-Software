using PracticeManager.Core;
namespace PracticeManager.Persistence;
public sealed partial class LocalPractice
{
 public static LocalPractice OpenDocumentAcceptance(string localAppData,string sessionId,bool newlyCreated)
 {
  var folder=LocalStoragePath.DirectoryPath(Path.Combine(LocalStoragePath.DirectoryPath(localAppData),"CRC Practice Manager DOCUMENT ACCEPTANCE",ValidateBudgetId(sessionId)));
  var marker=LocalStoragePath.FilePath(Path.Combine(folder,"document-acceptance-session.txt"),mustExist:true,rejectSidecars:true);
  using(var stream=new FileStream(marker,FileMode.Open,FileAccess.Read,FileShare.Read))
  {
   if(stream.Length!=32)throw new ArgumentException("Document acceptance marker mismatch.");
   var bytes=new byte[32];stream.ReadExactly(bytes);
   if(System.Text.Encoding.ASCII.GetString(bytes)!=sessionId)throw new ArgumentException("Document acceptance marker mismatch.");
  }
  var path=LocalStoragePath.FilePath(Path.Combine(folder,"practice-document-acceptance.db"),mustExist:!newlyCreated,mustBeNew:newlyCreated,rejectSidecars:newlyCreated);
  if(newlyCreated){using var reserve=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None);}
  return new LocalPractice(path,false,true);
 }
 public bool IsDocumentAcceptance=>documentAcceptance;
}
