using PracticeManager.Core;

internal static class InstalledProgramBoundaryTests
{
 internal static void Run(string parent,Action<bool,string> check)
 {
  if(!OperatingSystem.IsWindows())return;
  var profile=Directory.CreateDirectory(Path.Combine(parent,"installer-profile")).FullName;
  var programs=Directory.CreateDirectory(Path.Combine(profile,"Programs")).FullName;
  var root=WorkspaceConfidentiality.EnsureWorkspaceDirectory(Path.Combine(programs,"CRC Practice Manager"));
  var boundary=InstalledProgramBoundary.ReviewExisting(profile);
  void Refuse(Action action,string label)
  {
   try{action();}catch(ArgumentException){check(true,label);return;}
   throw new Exception("FAIL: "+label);
  }
  check(boundary.ProgramRoot==root,"installed program root fixed to per-user Programs directory");
  var digest=new string('a',64);
  var target=boundary.VersionDirectory(digest);
  check(target==Path.Combine(root,"versions",digest)&&!Directory.Exists(target),"version path derived from complete inventory digest without creating directory");
  foreach(var invalid in new[]{"../other","v1.0",new string('A',64),new string('a',63),new string('g',64)})
   Refuse(()=>boundary.VersionDirectory(invalid),"invalid version directory identifier rejected");
  Refuse(()=>boundary.AcquireExistingLease(),"missing lease cannot be silently created");
  check(!File.Exists(boundary.LeasePath),"failed lease acquisition leaves absent file absent");
  boundary.ReserveNewLeaseFile();
  Refuse(()=>boundary.ReserveNewLeaseFile(),"existing coordination file cannot be replaced");
  using(boundary.AcquireExistingLease())
  {
   try{using var competing=boundary.AcquireExistingLease();throw new Exception("FAIL: competing program operation acquired lease");}
   catch(IOException){check(true,"exclusive lease refuses competing program operation");}
  }
  using(boundary.AcquireExistingLease())
  {
   check(new FileInfo(boundary.LeasePath).Length==0,"lease can be reacquired after previous holder releases");
   Refuse(()=>boundary.ReviewExistingVersion(digest),"missing version directory not adopted");
   WorkspaceConfidentiality.EnsureWorkspaceDirectory(boundary.VersionsRoot);
   WorkspaceConfidentiality.EnsureWorkspaceDirectory(target);
   check(boundary.ReviewExistingVersion(digest)==target,"existing protected version directory qualifies for path review only");
  }
  check(File.Exists(boundary.LeasePath),"coordination file persists after release");
  File.WriteAllText(boundary.LeasePath,"unexpected content");
  Refuse(()=>boundary.AcquireExistingLease(),"unexpected coordination file content refused");
  check(File.ReadAllText(boundary.LeasePath)=="unexpected content","lease refusal does not truncate existing contents");
  check(!Directory.Exists(Path.Combine(profile,"CRC Practice Manager DATA"))&&!Directory.Exists(Path.Combine(profile,"CRC Practice Manager DEV")),"program boundary does not create business or development stores");
 }
}
