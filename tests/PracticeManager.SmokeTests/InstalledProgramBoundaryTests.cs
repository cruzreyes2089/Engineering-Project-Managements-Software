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
  var manifests=WorkspaceConfidentiality.EnsureWorkspaceDirectory(Path.Combine(root,"manifests"));
  string PrepareVersion(string tag)
  {
   var names=new[]{"CRC.PracticeManager.exe","CRC.PracticeManager.dll","CRC.PracticeManager.deps.json","CRC.PracticeManager.runtimeconfig.json",
    "PracticeManager.Core.dll","PracticeManager.Persistence.Sqlite.dll","Microsoft.Data.Sqlite.dll","e_sqlite3.dll","SQLitePCLRaw.core.dll",
    "SQLitePCLRaw.provider.e_sqlite3.dll","SQLitePCLRaw.batteries_v2.dll","hostfxr.dll","hostpolicy.dll","coreclr.dll",
    "PresentationFramework.dll","WindowsBase.dll","fr/PresentationFramework.resources.dll"};
   string Hash(byte[] bytes)=>Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
   var data=names.Select(name=>(Name:name,Bytes:System.Text.Encoding.UTF8.GetBytes("Synthetic nonexecutable text "+tag+" "+name))).ToArray();
   var rows=data.Select(x=>new{path=x.Name,bytes=x.Bytes.Length,sha256=Hash(x.Bytes)}).ToArray();
   var manifest=System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new{format="CRC-Development-Build-Inventory-v1",architecture="win-x64",fileCount=rows.Length,bytes=rows.Sum(x=>x.bytes),files=rows});
   var id=Hash(manifest);
   var dir=WorkspaceConfidentiality.EnsureWorkspaceDirectory(boundary.VersionDirectory(id));
   foreach(var item in data)
   {
    var file=Path.Combine(dir,item.Name.Replace('/',Path.DirectorySeparatorChar));
    var folder=Path.GetDirectoryName(file)!;
    if(!Directory.Exists(folder))WorkspaceConfidentiality.EnsureWorkspaceDirectory(folder);
    File.WriteAllBytes(file,item.Bytes);
   }
   File.WriteAllBytes(Path.Combine(manifests,id+".json"),manifest);
   return id;
  }
  var first=PrepareVersion("first");
  var second=PrepareVersion("second");
  check(InstalledVersionSelectionCandidate.ReviewCurrent(boundary)==null,"fresh program selection absent");
  InstalledVersionSelectionCandidate.SelectReviewedVersion(boundary,null,first);
  check(InstalledVersionSelectionCandidate.ReviewCurrent(boundary)==first,"verified initial program selection recorded");
  Refuse(()=>InstalledVersionSelectionCandidate.SelectReviewedVersion(boundary,null,second),"stale expected selection refused");
  var secondFile=Path.Combine(boundary.VersionDirectory(second),"hostfxr.dll");
  var original=File.ReadAllBytes(secondFile);
  File.WriteAllBytes(secondFile,original.Select(x=>(byte)(x^1)).ToArray());
  Refuse(()=>InstalledVersionSelectionCandidate.SelectReviewedVersion(boundary,first,second),"same-length altered candidate rejected");
  check(InstalledVersionSelectionCandidate.ReviewCurrent(boundary)==first,"failed candidate leaves active selection unchanged");
  File.WriteAllBytes(secondFile,original);
  var unexpected=Path.Combine(boundary.VersionDirectory(second),"unknown.dll");
  File.WriteAllText(unexpected,"unexpected");
  Refuse(()=>InstalledVersionSelectionCandidate.SelectReviewedVersion(boundary,first,second),"unexpected candidate file rejected");
  File.Delete(unexpected);
  InstalledVersionSelectionCandidate.SelectReviewedVersion(boundary,first,second);
  check(InstalledVersionSelectionCandidate.ReviewCurrent(boundary)==second,"verified replacement program selection recorded");
  check(Directory.Exists(boundary.VersionDirectory(first))&&Directory.EnumerateFiles(root,"previous-*.txt").Single() is string retained&&File.ReadAllText(retained)==first,"prior selection and payload retained");
  var pending=Path.Combine(root,"activation-pending.txt");
  File.WriteAllText(pending,first);
  Refuse(()=>InstalledVersionSelectionCandidate.ReviewCurrent(boundary),"interrupted selection blocks launch review");
  Refuse(()=>InstalledVersionSelectionCandidate.SelectReviewedVersion(boundary,second,first),"interrupted selection blocks retry or rollback");
  check(File.ReadAllText(pending)==first&&File.ReadAllText(Path.Combine(root,"active-version.txt"))==second,"interruption evidence and active pointer unchanged");
  File.Delete(pending); // disposable fixture only; not a recovery procedure
  check(File.Exists(boundary.LeasePath),"coordination file persists after release");
  File.WriteAllText(boundary.LeasePath,"unexpected content");
  Refuse(()=>boundary.AcquireExistingLease(),"unexpected coordination file content refused");
  check(File.ReadAllText(boundary.LeasePath)=="unexpected content","lease refusal does not truncate existing contents");
  check(!Directory.Exists(Path.Combine(profile,"CRC Practice Manager DATA"))&&!Directory.Exists(Path.Combine(profile,"CRC Practice Manager DEV")),"program boundary does not create business or development stores");
 }
}
