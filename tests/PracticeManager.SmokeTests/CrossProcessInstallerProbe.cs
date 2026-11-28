using System.Diagnostics;
using System.Reflection;
using PracticeManager.Core;

// Test harness only. No installer or helper is executed. The child may only
// attempt a handle open on a fixed synthetic fixture beneath the test temp root.
// It never writes bytes, deletes, renames, launches another process or unlocks.
internal static class CrossProcessInstallerProbe
{
 const string Prefix="CRC-test-";
 const string Command="--synthetic-installer-lock-probe";
 static string Root(string token)
 {
  if(!Guid.TryParseExact(token,"N",out var id)||id==Guid.Empty||id.ToString("N")!=token)
   throw new ArgumentException("Invalid fixture identity.");
  return LocalStoragePath.DirectoryPath(Path.Combine(Path.GetTempPath(),Prefix+token));
 }
 internal static int RunChild(string[] args)
 {
  try
  {
   if(!OperatingSystem.IsWindows()||args.Length!=3||args[0]!=Command||args[2] is not ("reservation" or "artifact"))return 1;
   var root=Root(args[1]);
   var reservation=LocalStoragePath.DirectoryPath(Path.Combine(root,"reservation-fixture","crc-extraction-reservation"));
   if(!WorkspaceConfidentiality.HasRestrictedDirectoryAcl(reservation))return 1;
   var artifact=args[2]=="artifact";
   var path=LocalStoragePath.FilePath(artifact?Path.Combine(reservation,"envelope","CRC.Uninstall.exe"):Path.Combine(reservation,"reservation.lock"),mustExist:true,rejectSidecars:true);
   if(!WorkspaceConfidentiality.HasRestrictedDirectoryAcl(Path.GetDirectoryName(path)!))return 1;
   try
   {
    using var stream=new FileStream(path,FileMode.Open,artifact?FileAccess.Write:FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
    return 11;
   }
   catch(IOException ex) when((ex.HResult&0xffff)==32||(ex.HResult&0xffff)==33){return 10;}
  }
  catch{return 1;}
 }
 internal static int Observe(string parent,string mode)
 {
  if(!OperatingSystem.IsWindows()||mode is not ("reservation" or "artifact"))throw new ArgumentException("Invalid synthetic probe.");
  var name=Path.GetFileName(parent);
  if(!name.StartsWith(Prefix,StringComparison.Ordinal)||!string.Equals(Root(name[Prefix.Length..]),parent,StringComparison.OrdinalIgnoreCase))
   throw new ArgumentException("Invalid test root.");
  var host=Environment.ProcessPath??throw new InvalidOperationException("Test host unavailable.");
  var start=new ProcessStartInfo(host){UseShellExecute=false,CreateNoWindow=true};
  if(string.Equals(Path.GetFileNameWithoutExtension(host),"dotnet",StringComparison.OrdinalIgnoreCase))
   start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
  start.ArgumentList.Add(Command);start.ArgumentList.Add(name[Prefix.Length..]);start.ArgumentList.Add(mode);
  using var child=Process.Start(start)??throw new InvalidOperationException("Synthetic probe could not start.");
  if(!child.WaitForExit(20000))
  {
   child.Kill();child.WaitForExit();throw new InvalidOperationException("Synthetic probe timed out.");
  }
  return child.ExitCode;
 }
}
