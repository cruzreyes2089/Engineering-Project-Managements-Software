using System.Security.Cryptography;
using PracticeManager.Core;
namespace PracticeManager.Persistence;

// Local private review copies only. No open, send, overwrite or automatic issue.
// File publication and database preparation audit are deliberately separate.
public static class VerifiedDocumentFile
{
 public static void Save(string destination,string extension,byte[] bytes,string expectedHash)
 {
  if(bytes.Length is <1 or > 64*1024*1024||extension is not (".pdf" or ".dwg" or ".dxf" or ".rvt" or ".docx" or ".xlsx" or ".txt" or ".csv" or ".zip")||
   !string.Equals(Path.GetExtension(destination),extension,StringComparison.OrdinalIgnoreCase))
   throw new ArgumentException("Use a new local file with its required extension and bounded content.");
  var target=LocalStoragePath.FilePath(destination,mustExist:false,mustBeNew:true,rejectSidecars:true);
  var frozen=bytes.ToArray();
  try
  {
   if(!string.Equals(Convert.ToHexString(SHA256.HashData(frozen)),expectedHash,StringComparison.OrdinalIgnoreCase))
    throw new ArgumentException("Document content hash mismatch.");
   using var stage=new RecoveryStaging(target);
   using(var stream=new FileStream(stage.FilePath,FileMode.CreateNew,FileAccess.Write,FileShare.None)){stream.Write(frozen);stream.Flush(true);}
   using(var read=new FileStream(stage.FilePath,FileMode.Open,FileAccess.Read,FileShare.Read))
    if(read.Length!=frozen.LongLength||!string.Equals(Convert.ToHexString(SHA256.HashData(read)),expectedHash,StringComparison.OrdinalIgnoreCase))
     throw new InvalidOperationException("Staged document content mismatch.");
   stage.Publish(frozen.LongLength,expectedHash);
  }
  finally{CryptographicOperations.ZeroMemory(frozen);}
 }
}
