using System.Security.Cryptography;
using PracticeManager.Core;
internal static class HistoricalArchivePreviewFileTests
{
 public static void Run(string root,Action<bool,string> check)
 {
  Directory.CreateDirectory(root);
  var path=Path.Combine(root,"synthetic.json");
  var data="{\"synthetic\":true}"u8.ToArray();
  File.WriteAllBytes(path,data);
  var hash=Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
  check(HistoricalArchivePreviewFiles.Read(path,hash).SequenceEqual(data),"historical file snapshot exact bytes");
  void Invalid(Action action,string label){try{action();}catch(ArgumentException){check(true,label);return;}throw new Exception("FAIL: "+label);}
  Invalid(()=>HistoricalArchivePreviewFiles.Read(path,new string('0',64)),"historical file mismatched digest rejected");
  Invalid(()=>HistoricalArchivePreviewFiles.Read(path,"bad"),"historical file malformed digest rejected");
  Invalid(()=>HistoricalArchivePreviewFiles.Read(path,hash.ToUpperInvariant()),"historical file core lowercase digest contract");
  File.AppendAllText(path," ");Invalid(()=>HistoricalArchivePreviewFiles.Read(path,hash),"historical changed bytes rejected");
  File.WriteAllBytes(path,Array.Empty<byte>());Invalid(()=>HistoricalArchivePreviewFiles.Read(path,hash),"empty historical file rejected");
  using(var f=File.OpenWrite(path))f.SetLength(HistoricalProjectTransfer.MaxBytes+1L);
  Invalid(()=>HistoricalArchivePreviewFiles.Read(path,hash),"oversized historical file rejected");
 }
}
