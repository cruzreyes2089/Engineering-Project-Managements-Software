using PracticeManager.Core;
namespace PracticeManager.Persistence;
public sealed partial class LocalPractice
{
 // Synthetic-only startup. Re-resolve the session marker and exact path instead
 // of accepting an arbitrary database path. No production migration entry point.
 public static LocalPractice OpenBudgetAcceptance(string localAppData,string sessionId,bool newlyCreated)
 {
  var folder=LocalStoragePath.DirectoryPath(Path.Combine(LocalStoragePath.DirectoryPath(localAppData),"CRC Practice Manager BUDGET ACCEPTANCE",ValidateBudgetId(sessionId)));
  var marker=LocalStoragePath.FilePath(Path.Combine(folder,"budget-acceptance-session.txt"),mustExist:true,rejectSidecars:true);
  using(var stream=new FileStream(marker,FileMode.Open,FileAccess.Read,FileShare.Read))
  {
   if(stream.Length!=32)throw new ArgumentException("Budget acceptance marker mismatch.");
   var bytes=new byte[32];stream.ReadExactly(bytes);
   if(System.Text.Encoding.ASCII.GetString(bytes)!=sessionId)throw new ArgumentException("Budget acceptance marker mismatch.");
  }
  var path=LocalStoragePath.FilePath(Path.Combine(folder,"practice-budget-acceptance.db"),mustExist:!newlyCreated,mustBeNew:newlyCreated,rejectSidecars:newlyCreated);
  if(newlyCreated)
  {
   // Reserve the new file exclusively before SQLite initialization.
   using(var reserve=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None)){}
  }
  if(!newlyCreated&&DevelopmentWorkspace.Resolve(localAppData,new[]{"--budget-acceptance-session",sessionId}).DatabasePath!=path)throw new ArgumentException("Acceptance workspace identity changed.");
  return new LocalPractice(path,true,existingOnly:true);
 }
 static string ValidateBudgetId(string id)
 {
  if(!Guid.TryParseExact(id,"N",out var parsed)||parsed==Guid.Empty||parsed.ToString("N")!=id)
   throw new ArgumentException("Exact budget acceptance ID required.");
  return id;
 }
}
