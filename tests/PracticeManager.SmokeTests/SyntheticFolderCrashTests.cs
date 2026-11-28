using System.Diagnostics;

// The worker receives only a fixed synthetic scenario, never a path.
// Its fresh random temporary root is created exclusively for this test.
internal static class SyntheticFolderCrashTests
{
 const string Prefix="CRC-folder-crash-";
 const string ProjectNumber="PRJ-000031";
 const long ProjectId=31;
 const string JournalName="synthetic-journal.db";
 public static int Worker(string phase)
 {
  if(phase is not ("partial" or "before-outcome"))return 25;
  var root=Path.Combine(Path.GetTempPath(),Prefix+Guid.NewGuid().ToString("N"));
  if(Directory.Exists(root)||File.Exists(root))return 25;
  Directory.CreateDirectory(root);
  Console.WriteLine(root);Console.Out.Flush();
  try
  {
   Directory.CreateDirectory(Path.Combine(root,"SyntheticProduction"));
   Directory.CreateDirectory(Path.Combine(root,"SyntheticRestricted"));
   using(var file=new FileStream(Path.Combine(root,SyntheticFolderOperationFence.FileName),FileMode.CreateNew,FileAccess.Write,FileShare.None))file.Flush(true);
   var folders=new SyntheticFolderProvisioning(root,ProjectId,ProjectNumber);
   var journal=new SyntheticFolderJournal(Path.Combine(root,JournalName),folders,ProjectId);
   var review=folders.Preview(false,false);
   void StopAtBoundary()
   {
    Console.WriteLine("CRC-SYNTHETIC-CRASH-READY");Console.Out.Flush();
    // Parent terminates this exact test child while the fence is still held.
    // Exit on closed stdin rather than leave a worker running indefinitely.
    Console.ReadLine();
    throw new IOException("Synthetic worker released without termination.");
   }
   journal.ExecuteReviewed(false,false,review.ReviewFingerprint,true,
    afterCreate:n=>{if(phase=="partial"&&n==3)StopAtBoundary();},
    beforeOutcome:()=>{if(phase=="before-outcome")StopAtBoundary();});
   return 26;
  }
  catch(Exception){return 27;}
 }
 static bool OwnRoot(string? path)
 {
  if(string.IsNullOrEmpty(path)||!Path.IsPathFullyQualified(path))return false;
  var full=Path.GetFullPath(path);
  var name=Path.GetFileName(full);
  return string.Equals(Path.GetDirectoryName(full),Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())),StringComparison.OrdinalIgnoreCase)&&
    name.StartsWith(Prefix,StringComparison.Ordinal)&&Guid.TryParseExact(name[Prefix.Length..],"N",out _);
 }
 public static void Run(Action<bool,string> check)
 {
  foreach(var phase in new[]{"partial","before-outcome"})
  {
   var info=new ProcessStartInfo("dotnet"){UseShellExecute=false,CreateNoWindow=true,
    RedirectStandardOutput=true,RedirectStandardInput=true};
   info.ArgumentList.Add(typeof(SyntheticFolderCrashTests).Assembly.Location);
   info.ArgumentList.Add("--synthetic-folder-crash-worker");info.ArgumentList.Add(phase);
   using var process=Process.Start(info)??throw new InvalidOperationException("Synthetic crash worker failed to start.");
   string? root=null;
   try
   {
    root=process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(20)).GetAwaiter().GetResult();
    if(!OwnRoot(root))throw new InvalidOperationException("Invalid synthetic crash workspace.");
    var marker=process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(20)).GetAwaiter().GetResult();
    check(marker=="CRC-SYNTHETIC-CRASH-READY"&&!process.HasExited,"crash worker reached controlled "+phase+" boundary");
    check(SyntheticFolderOperationFence.ProbeInOtherProcess(root!)==23,"crash worker retains exclusive fence");
    process.Kill(entireProcessTree:true);
    if(!process.WaitForExit(15000))throw new TimeoutException("Synthetic crash worker did not terminate.");
    check(SyntheticFolderOperationFence.ProbeInOtherProcess(root!)==0,"terminated worker releases OS fence");
    var folders=new SyntheticFolderProvisioning(root!,ProjectId,ProjectNumber);
    var journal=new SyntheticFolderJournal(Path.Combine(root!,JournalName),folders,ProjectId);
    var pending=journal.Pending();
    check(pending!=null&&journal.State(pending)=="Uncertain","actual termination preserves durable uncertainty");
    var observed=folders.Preview(false,false);
    check(observed.Steps.Count(x=>x.Exists)==(phase=="partial"?3:10),"actual termination preserves expected physical progress");
    bool denied=false;
    try{journal.ExecuteReviewed(false,false,observed.ReviewFingerprint,true);}
    catch(ArgumentException){denied=true;}
    check(denied,"actual crash blocks unreviewed replay");
    var repair=journal.ExecuteReviewed(false,false,observed.ReviewFingerprint,true,pending);
    check(journal.State(pending!)=="Superseded"&&journal.State(repair)=="Confirmed"&&journal.Pending()==null,
     "actual crash reconciled by linked repair");
    check(folders.Preview(false,false).Steps.All(x=>x.Exists)&&
      folders.Preview(false,false).Steps.Count==10,"actual crash repair verifies complete flat production layout");
   }
   finally
   {
    if(!process.HasExited)
    {
     process.Kill(entireProcessTree:true);
     process.WaitForExit(15000);
    }
    // Remove only the fixed-name random test workspace reported by this child.
    if(OwnRoot(root)&&Directory.Exists(root)&&
      (File.GetAttributes(root!)&FileAttributes.ReparsePoint)==0)Directory.Delete(root!,true);
   }
  }
 }
}
