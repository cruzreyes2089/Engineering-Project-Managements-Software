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
  var activeExe=Path.Combine(boundary.VersionDirectory(second),"CRC.PracticeManager.exe");
  var previousExe=Path.Combine(boundary.VersionDirectory(first),"CRC.PracticeManager.exe");
  Refuse(()=>InstalledVersionSelectionCandidate.AcquireSelectedLaunchLease(boundary,previousExe),"previous installed executable cannot acquire launch lease");
  using(InstalledVersionSelectionCandidate.AcquireSelectedLaunchLease(boundary,activeExe))
  {
   try{using var conflicting=boundary.AcquireExistingLease();throw new Exception("FAIL: installer acquired running application's lease");}
   catch(IOException){check(true,"installed application lifetime blocks installer access");}
  }
  check(InstalledVersionSelectionCandidate.ReviewCurrent(boundary)==second,"installed application lease releases on disposal");
  check(Directory.Exists(boundary.VersionDirectory(first))&&Directory.EnumerateFiles(root,"previous-*.txt").Single() is string retained&&File.ReadAllText(retained)==first,"prior selection and payload retained");
  var pending=Path.Combine(root,"activation-pending.txt");
  File.WriteAllText(pending,first);
  Refuse(()=>InstalledVersionSelectionCandidate.ReviewCurrent(boundary),"interrupted selection blocks launch review");
  Refuse(()=>InstalledVersionSelectionCandidate.SelectReviewedVersion(boundary,second,first),"interrupted selection blocks retry or rollback");
  check(File.ReadAllText(pending)==first&&File.ReadAllText(Path.Combine(root,"active-version.txt"))==second,"interruption evidence and active pointer unchanged");
  File.Delete(pending); // disposable fixture only; not a recovery procedure
  var projects=Directory.CreateDirectory(Path.Combine(profile,"user-projects")).FullName;
  var sentinel=Path.Combine(projects,"synthetic-user-record.txt");
  File.WriteAllText(sentinel,"must survive removal");
  Refuse(()=>InstalledVersionSelectionCandidate.RemoveReviewedActiveVersion(boundary,first),"stale active version cannot be removed");
  File.WriteAllText(unexpected,"unexpected");
  Refuse(()=>InstalledVersionSelectionCandidate.RemoveReviewedActiveVersion(boundary,second),"unexpected file blocks uninstall preflight");
  check(File.Exists(unexpected)&&File.Exists(secondFile)&&File.ReadAllText(Path.Combine(root,"active-version.txt"))==second,"failed removal preserves candidate and selection");
  File.Delete(unexpected);
  File.WriteAllBytes(secondFile,original.Select(x=>(byte)(x^1)).ToArray());
  Refuse(()=>InstalledVersionSelectionCandidate.RemoveReviewedActiveVersion(boundary,second),"modified program file blocks uninstall preflight");
  File.WriteAllBytes(secondFile,original);
  var removalPending=Path.Combine(root,"uninstall-pending.txt");
  File.WriteAllText(removalPending,second);
  Refuse(()=>InstalledVersionSelectionCandidate.ReviewCurrent(boundary),"interrupted removal blocks launch review");
  Refuse(()=>InstalledVersionSelectionCandidate.RemoveReviewedActiveVersion(boundary,second),"interrupted removal blocks automatic retry");
  check(File.ReadAllText(removalPending)==second&&File.Exists(secondFile),"interrupted removal preserves evidence and remaining files");
  File.Delete(removalPending); // disposable fixture only
  check(InstalledVersionSelectionCandidate.RemoveReviewedActiveVersion(boundary,second)==17,"removal deletes exactly the verified active payload");
  check(!Directory.Exists(boundary.VersionDirectory(second))&&InstalledVersionSelectionCandidate.ReviewCurrent(boundary)==null,"uninstalled selection has no fallback active version");
  check(Directory.Exists(boundary.VersionDirectory(first))&&File.Exists(Path.Combine(manifests,second+".json"))&&Directory.EnumerateFiles(root,"uninstalled-*.txt").Count()==1,"previous payload and uninstall evidence retained");
  check(File.ReadAllText(sentinel)=="must survive removal","uninstall does not touch user project records");
  // Nonexecutable fixtures simulate extraction into a separate directory.
  (string Source,byte[] Manifest,string Digest) Extracted(string label)
  {
   var source=Directory.CreateDirectory(Path.Combine(parent,label)).FullName;
   var rows=new List<object>();long bytes=0;
   foreach(var file in Directory.EnumerateFiles(boundary.VersionDirectory(first),"*",SearchOption.AllDirectories).OrderBy(x=>x,StringComparer.Ordinal))
   {
    var name=Path.GetRelativePath(boundary.VersionDirectory(first),file).Replace(Path.DirectorySeparatorChar,'/');
    var output=Path.Combine(source,name.Replace('/',Path.DirectorySeparatorChar));
    Directory.CreateDirectory(Path.GetDirectoryName(output)!);
    var content=System.Text.Encoding.UTF8.GetBytes("Synthetic extracted "+label+" "+name);
    File.WriteAllBytes(output,content);bytes+=content.Length;
    rows.Add(new{path=name,bytes=content.Length,sha256=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(content)).ToLowerInvariant()});
   }
   var raw=System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new{format="CRC-Development-Build-Inventory-v1",architecture="win-x64",fileCount=rows.Count,bytes,files=rows});
   return(source,raw,Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(raw)).ToLowerInvariant());
  }
  var extracted=Extracted("extract-one");
  var installPending=Path.Combine(root,"install-pending.txt");
  var extraSource=Path.Combine(extracted.Source,"extra.dll");
  File.WriteAllText(extraSource,"unexpected");
  Refuse(()=>InstalledVersionSelectionCandidate.StageAndSelectReviewedPayload(boundary,null,extracted.Digest,extracted.Source,extracted.Manifest),"extra extracted file refuses install");
  check(!Directory.Exists(boundary.VersionDirectory(extracted.Digest))&&!File.Exists(installPending),"invalid extracted tree leaves install location unchanged");
  File.Delete(extraSource);
  InstalledVersionSelectionCandidate.StageAndSelectReviewedPayload(boundary,null,extracted.Digest,extracted.Source,extracted.Manifest);
  check(InstalledVersionSelectionCandidate.ReviewCurrent(boundary)==extracted.Digest&&!File.Exists(installPending),"fresh staged payload selected and verified");
  check(File.Exists(Path.Combine(extracted.Source,"hostfxr.dll")),"source extraction remains unchanged");
  var upgrade=Extracted("extract-two");
  Refuse(()=>InstalledVersionSelectionCandidate.StageAndSelectReviewedPayload(boundary,null,upgrade.Digest,upgrade.Source,upgrade.Manifest),"stale expected selection refuses staging");
  using(InstalledVersionSelectionCandidate.AcquireSelectedLaunchLease(boundary,Path.Combine(boundary.VersionDirectory(extracted.Digest),"CRC.PracticeManager.exe")))
  {
   try{InstalledVersionSelectionCandidate.StageAndSelectReviewedPayload(boundary,extracted.Digest,upgrade.Digest,upgrade.Source,upgrade.Manifest);throw new Exception("FAIL: install during active launch lease");}
   catch(IOException){check(true,"running application lease prevents staging");}
  }
  InstalledVersionSelectionCandidate.StageAndSelectReviewedPayload(boundary,extracted.Digest,upgrade.Digest,upgrade.Source,upgrade.Manifest);
  check(InstalledVersionSelectionCandidate.ReviewCurrent(boundary)==upgrade.Digest&&Directory.Exists(boundary.VersionDirectory(extracted.Digest)),"combined upgrade retains prior payload");
  File.WriteAllText(installPending,upgrade.Digest);
  Refuse(()=>InstalledVersionSelectionCandidate.ReviewCurrent(boundary),"incomplete install blocks startup review");
  Refuse(()=>InstalledVersionSelectionCandidate.StageAndSelectReviewedPayload(boundary,upgrade.Digest,first,extracted.Source,extracted.Manifest),"incomplete install blocks further staging");
  check(File.ReadAllText(sentinel)=="must survive removal","staging and selection preserve project data");
  File.Delete(installPending); // disposable fixture only
  var freshProfile=Directory.CreateDirectory(Path.Combine(parent,"fresh-install")).FullName;
  var fresh=InstalledProgramBoundary.InitializeNewInstallation(freshProfile);
  var freshId=fresh.VerifyInitializedLayout();
  check(Guid.TryParseExact(freshId,"N",out var parsed)&&parsed!=Guid.Empty,"fresh install reserves protected root and unique identity");
  check(InstalledProgramBoundary.ReviewInitialized(freshProfile).VerifyInitializedLayout()==freshId,"existing initialized installation retains identity");
  Refuse(()=>InstalledProgramBoundary.InitializeNewInstallation(freshProfile),"existing program root cannot be reinitialized");
  InstalledVersionSelectionCandidate.StageAndSelectReviewedPayload(fresh,null,extracted.Digest,extracted.Source,extracted.Manifest);
  check(InstalledVersionSelectionCandidate.ReviewCurrent(fresh)==extracted.Digest,"fresh initialization and staged installation work together");
  var uninstaller=Path.Combine(fresh.ProgramRoot,"CRC.Uninstall.exe");
  File.WriteAllText(uninstaller,"Synthetic nonexecutable uninstaller fixture");
  var uninstallerHash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(uninstaller))).ToLowerInvariant();
  var shortcutFolder=Directory.CreateDirectory(Path.Combine(parent,"crc-shortcut-fixture")).FullName;
  var registryPath=@"Software\CRC.PracticeManager.SyntheticTests-"+Guid.NewGuid().ToString("N");
  using(var hive=Microsoft.Win32.RegistryKey.OpenBaseKey(Microsoft.Win32.RegistryHive.CurrentUser,Microsoft.Win32.RegistryView.Registry64))
  {
   using(var registry=hive.CreateSubKey(registryPath,true)??throw new InvalidOperationException("Synthetic registry fixture unavailable."))
   {
    try
    {
     var receiptPath=Path.Combine(fresh.ProgramRoot,"registration-receipt.json");
     var shortcutPath=Path.Combine(shortcutFolder,WindowsRegistrationCandidate.ShortcutName);
     InstalledVersionSelectionCandidate.RegisterReviewedInstallation(fresh,extracted.Digest,uninstallerHash,registry,shortcutFolder);
     check(File.Exists(shortcutPath)&&File.Exists(receiptPath)&&!File.Exists(Path.Combine(fresh.ProgramRoot,"registration-pending.txt")),"registration publishes shortcut and receipt after verification");
     var receipt=System.Text.Json.JsonSerializer.Deserialize<WindowsRegistrationCandidate.Registration>(File.ReadAllBytes(receiptPath))??throw new Exception("Missing registration receipt");
     WindowsRegistrationCandidate.VerifyOwnedRegistration(fresh,registry,shortcutFolder,receipt);
     check(receipt.Identity==freshId&&receipt.Version==extracted.Digest,"registration binds installation identity and exact version");
     Refuse(()=>InstalledVersionSelectionCandidate.RegisterReviewedInstallation(fresh,extracted.Digest,uninstallerHash,registry,shortcutFolder),"existing registration cannot be replaced");
     using(var key=registry.OpenSubKey(WindowsRegistrationCandidate.ProductKey,true)!)
      key.SetValue("Unexpected","unrelated");
     Refuse(()=>WindowsRegistrationCandidate.RemoveOwnedKey(registry,receipt),"unexpected registry value blocks removal");
     using(var key=registry.OpenSubKey(WindowsRegistrationCandidate.ProductKey,true)!)
     {
      check((string?)key.GetValue("Unexpected")=="unrelated","unrelated registry value preserved");
      key.DeleteValue("Unexpected"); // disposable fixture only
     }
     var shortcutBytes=File.ReadAllBytes(shortcutPath);
     File.AppendAllText(shortcutPath,"unexpected");
     Refuse(()=>WindowsRegistrationCandidate.VerifyOwnedRegistration(fresh,registry,shortcutFolder,receipt),"modified shortcut refuses registration ownership");
     File.WriteAllBytes(shortcutPath,shortcutBytes); // disposable fixture only
     Refuse(()=>InstalledVersionSelectionCandidate.StageAndSelectReviewedPayload(fresh,extracted.Digest,upgrade.Digest,upgrade.Source,upgrade.Manifest),"registered installation refuses uncoordinated upgrade");
     Refuse(()=>InstalledVersionSelectionCandidate.RemoveReviewedActiveVersion(fresh,extracted.Digest),"registered installation refuses uncoordinated removal");
     File.AppendAllText(shortcutPath,"unexpected");
     Refuse(()=>InstalledVersionSelectionCandidate.UpgradeRegisteredInstallation(fresh,extracted.Digest,upgrade.Digest,upgrade.Source,upgrade.Manifest,registry,shortcutFolder),"modified shortcut blocks registered upgrade");
     check(InstalledVersionSelectionCandidate.ReviewCurrent(fresh)==extracted.Digest&&!Directory.Exists(fresh.VersionDirectory(upgrade.Digest)),"registration refusal leaves selection and candidate unchanged");
     File.WriteAllBytes(shortcutPath,shortcutBytes); // disposable fixture only
     InstalledVersionSelectionCandidate.UpgradeRegisteredInstallation(fresh,extracted.Digest,upgrade.Digest,upgrade.Source,upgrade.Manifest,registry,shortcutFolder);
     var upgraded=System.Text.Json.JsonSerializer.Deserialize<WindowsRegistrationCandidate.Registration>(File.ReadAllBytes(receiptPath))??throw new Exception("Missing upgraded receipt");
     WindowsRegistrationCandidate.VerifyOwnedRegistration(fresh,registry,shortcutFolder,upgraded);
     check(upgraded.Version==upgrade.Digest&&InstalledVersionSelectionCandidate.ReviewCurrent(fresh)==upgrade.Digest,"registered upgrade selects new payload and verifies registration");
     check(Directory.Exists(fresh.VersionDirectory(extracted.Digest))&&Directory.EnumerateFiles(fresh.ProgramRoot,"registration-history-*.json").Count()==1,"registered upgrade retains prior payload and registration receipt");
     var upgradedShortcutBytes=File.ReadAllBytes(shortcutPath);
     File.AppendAllText(shortcutPath,"unexpected");
     Refuse(()=>InstalledVersionSelectionCandidate.RemoveRegisteredInstallation(fresh,upgrade.Digest,registry,shortcutFolder),"modified shortcut blocks registered removal");
     check(File.Exists(shortcutPath)&&InstalledVersionSelectionCandidate.ReviewCurrent(fresh)==upgrade.Digest,"refused removal preserves registered program");
     // Recover only our disposable fixture's known exact shortcut bytes.
     // Capture valid bytes before tampering instead of accepting any new hash.
     using(var key=registry.OpenSubKey(WindowsRegistrationCandidate.ProductKey,false))
      check(key!=null,"refused removal leaves registry intact");
     File.WriteAllBytes(shortcutPath,upgradedShortcutBytes); // disposable fixture only
     check(InstalledVersionSelectionCandidate.RemoveRegisteredInstallation(fresh,upgrade.Digest,registry,shortcutFolder)==17,"registered uninstall removes exact active payload");
     using(var removed=registry.OpenSubKey(WindowsRegistrationCandidate.ProductKey,false))
      check(removed==null&&!File.Exists(shortcutPath)&&!File.Exists(receiptPath),"registered uninstall removes only owned Windows entries");
     check(InstalledVersionSelectionCandidate.ReviewCurrent(fresh)==null&&Directory.Exists(fresh.VersionDirectory(extracted.Digest))&&File.Exists(uninstaller),"registered removal retains earlier version and uninstaller evidence");
     check(Directory.EnumerateFiles(fresh.ProgramRoot,"registration-history-*.json").Count()==2,"registered removal retains receipt history");
     Refuse(()=>WindowsRegistrationCandidate.VerifyOwnedKey(registry,upgraded),"missing registry key refuses ownership");

    }
    finally
    {
     // Exact disposable test key only; no actual Windows Apps registration.
     registry.DeleteSubKey(WindowsRegistrationCandidate.ProductKey,false);
    }
   }
   hive.DeleteSubKey(registryPath,false);
  }
  var freshMarker=Path.Combine(fresh.ProgramRoot,"installation-identity.txt");
  File.WriteAllText(freshMarker,"invalid identity");
  Refuse(()=>InstalledProgramBoundary.ReviewInitialized(freshProfile),"invalid installation identity refuses reopen");
  check(File.ReadAllText(freshMarker)=="invalid identity","invalid installation identity remains unchanged");
  var collisionProfile=Directory.CreateDirectory(Path.Combine(parent,"collision-install")).FullName;
  var collisionPrograms=Directory.CreateDirectory(Path.Combine(collisionProfile,"Programs")).FullName;
  var collisionRoot=WorkspaceConfidentiality.EnsureWorkspaceDirectory(Path.Combine(collisionPrograms,"CRC Practice Manager"));
  var existing=Path.Combine(collisionRoot,"existing.txt");
  File.WriteAllText(existing,"retain existing");
  Refuse(()=>InstalledProgramBoundary.InitializeNewInstallation(collisionProfile),"existing unmarked program root not adopted");
  check(File.ReadAllText(existing)=="retain existing"&&!File.Exists(Path.Combine(collisionRoot,"program.lock")),"existing program content preserved");
  var interruptedProfile=Directory.CreateDirectory(Path.Combine(parent,"interrupted-install")).FullName;
  var interruptedPrograms=Directory.CreateDirectory(Path.Combine(interruptedProfile,"Programs")).FullName;
  var reservation=Directory.CreateDirectory(Path.Combine(interruptedPrograms,".crc-install-reservation-"+Guid.NewGuid().ToString("N"))).FullName;
  Refuse(()=>InstalledProgramBoundary.InitializeNewInstallation(interruptedProfile),"interrupted first installation refuses replacement reservation");
  check(Directory.Exists(reservation)&&!Directory.Exists(Path.Combine(interruptedPrograms,"CRC Practice Manager")),"interrupted reservation retained without publishing program root");
  check(!Directory.Exists(Path.Combine(freshProfile,"CRC Practice Manager DATA"))&&!Directory.Exists(Path.Combine(freshProfile,"CRC Practice Manager DEV")),"first install creates no business database");


  check(File.Exists(boundary.LeasePath),"coordination file persists after release");
  File.WriteAllText(boundary.LeasePath,"unexpected content");
  Refuse(()=>boundary.AcquireExistingLease(),"unexpected coordination file content refused");
  check(File.ReadAllText(boundary.LeasePath)=="unexpected content","lease refusal does not truncate existing contents");
  check(!Directory.Exists(Path.Combine(profile,"CRC Practice Manager DATA"))&&!Directory.Exists(Path.Combine(profile,"CRC Practice Manager DEV")),"program boundary does not create business or development stores");
 }
}
