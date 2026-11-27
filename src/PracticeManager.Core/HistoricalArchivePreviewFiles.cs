using System.Security.Cryptography;
namespace PracticeManager.Core;

/// <summary>Bounded local input reader. No file is modified and no input contents are logged.</summary>
public static class HistoricalArchivePreviewFiles
{
 public static byte[] Read(string path,string expectedSha256)
 {
  if(expectedSha256 is null||expectedSha256.Length!=64||!expectedSha256.All(c=>c is >= '0' and <= '9' or >= 'a' and <= 'f'))
   throw new ArgumentException("Enter an independently recorded lowercase SHA-256.");
  using var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
  if(file.Length is <1 or >HistoricalProjectTransfer.MaxBytes)throw new ArgumentException("Historical transfer outside supported size.");
  var data=new byte[checked((int)file.Length)];
  try
  {
   file.ReadExactly(data);
   if(file.ReadByte()!=-1||Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant()!=expectedSha256)
    throw new ArgumentException("Historical transfer changed or does not match the independently recorded digest.");
   return data;
  }
  catch{CryptographicOperations.ZeroMemory(data);throw;}
 }
}
