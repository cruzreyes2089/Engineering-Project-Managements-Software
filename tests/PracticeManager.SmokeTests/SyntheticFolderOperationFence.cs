using System.Diagnostics;

// Cooperative process fence in a test-owned workspace. Never deletes or replaces its lock file.
// This does not defend against hostile processes or qualify Windows root/ACL identity.
internal static class SyntheticFolderOperationFence
{
 internal const string FileName="synthetic-folder-operation.lock";
 public static FileStream Acquire(string workspace)
 {
  var path=Path.Combine(workspace,FileName);
  if(!File.Exists(path))throw new IOException("Synthetic lock file missing.");
  var stream=new FileStream(path,FileMode.Open,FileAccess.ReadWrite,FileShare.None);
  try
  {
   if((File.GetAttributes(path)&(FileAttributes.ReparsePoint|FileAttributes.Directory))!=0||
       stream.Length!=0)throw new IOException("Invalid synthetic lock file.");
   return stream;
  }
  catch{stream.Dispose();throw;}
 }
 // The subprocess only opens a pre-existing empty lock file for exclusive read access.
 // It cannot create directories, acquire a journal, change records or write file bytes.
 public static int Probe(string path)
 {
  try
  {
   var full=Path.GetFullPath(path);
   var temp=Path.GetFullPath(Path.GetTempPath());
   if(!Path.GetFileName(full).Equals(FileName,StringComparison.Ordinal)||
      !full.StartsWith(temp,StringComparison.OrdinalIgnoreCase)||
      (File.GetAttributes(full)&(FileAttributes.ReparsePoint|FileAttributes.Directory))!=0)return 24;
   using var stream=new FileStream(full,FileMode.Open,FileAccess.Read,FileShare.None);
   return stream.Length==0?0:24;
  }
  catch(IOException){return 23;}
  catch(UnauthorizedAccessException){return 24;}
  catch(ArgumentException){return 24;}
 }
 public static int ProbeInOtherProcess(string workspace)
 {
  var info=new ProcessStartInfo("dotnet"){UseShellExecute=false,CreateNoWindow=true};
  info.ArgumentList.Add(typeof(SyntheticFolderOperationFence).Assembly.Location);
  info.ArgumentList.Add("--synthetic-folder-lock-probe");
  info.ArgumentList.Add(Path.Combine(workspace,FileName));
  using var process=Process.Start(info)??throw new InvalidOperationException("Synthetic lock probe did not launch.");
  if(!process.WaitForExit(15000))
  {
   process.Kill(entireProcessTree:true);
   process.WaitForExit();
   throw new TimeoutException("Synthetic lock probe timed out.");
  }
  return process.ExitCode;
 }
}
