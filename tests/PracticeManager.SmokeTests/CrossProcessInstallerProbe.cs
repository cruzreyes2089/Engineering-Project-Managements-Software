using System.IO.Pipes;
using System.Security.Cryptography;
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
 internal static int RunHandoffChild(string[] args)
 {
  try
  {
   const string prefix="CRC-synthetic-handoff-";
   if(!OperatingSystem.IsWindows()||args.Length!=3||args[0]!="--synthetic-installer-handoff-probe"||args[2] is not ("normal" or "disconnect" or "wrong-response")||
      !args[1].StartsWith(prefix,StringComparison.Ordinal)||
      !Guid.TryParseExact(args[1][prefix.Length..],"N",out var id)||id.ToString("N")!=args[1][prefix.Length..])return 1;
   using var pipe=new NamedPipeClientStream(".",args[1],PipeDirection.InOut,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
   using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(15));
   pipe.ConnectAsync(deadline.Token).GetAwaiter().GetResult();
   var challenge=new byte[64];
   pipe.ReadExactlyAsync(challenge,deadline.Token).AsTask().GetAwaiter().GetResult();
   if(args[2]=="disconnect")return 2;
   var response=SHA256.HashData(challenge);
   if(args[2]=="wrong-response")response[0]^=0xff;
   pipe.WriteAsync(response,deadline.Token).AsTask().GetAwaiter().GetResult();
   var result=new byte[1];
   pipe.ReadExactlyAsync(result,deadline.Token).AsTask().GetAwaiter().GetResult();
   return result[0]==1?0:1;
  }
  catch{return 1;}
 }
 internal static bool ObserveHandoff(InstallerHandoffReviewSession session,bool wrongPeer,string mode="normal")
 {
  var host=Environment.ProcessPath??throw new InvalidOperationException("Test host unavailable.");
  var start=new ProcessStartInfo(host){UseShellExecute=false,CreateNoWindow=true};
  if(string.Equals(Path.GetFileNameWithoutExtension(host),"dotnet",StringComparison.OrdinalIgnoreCase))
   start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
  start.ArgumentList.Add("--synthetic-installer-handoff-probe");start.ArgumentList.Add(session.PipeName);start.ArgumentList.Add(mode);
  using var child=Process.Start(start)??throw new InvalidOperationException("Synthetic peer unavailable.");
  using var current=Process.GetCurrentProcess();
  try
  {
   var result=session.ReviewAsync(wrongPeer?current:child).GetAwaiter().GetResult();
   if(!child.WaitForExit(5000))throw new InvalidOperationException("Synthetic peer did not exit.");
   return child.ExitCode==0&&!result.MayExecute;
  }
  finally
  {
   if(!child.HasExited){child.Kill();child.WaitForExit();}
  }
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
