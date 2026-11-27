using PracticeManager.Core;
using PracticeManager.Persistence;
internal static class DevelopmentWorkspaceTests
{
 internal static void Run(string parent,Action<bool,string> check)
 {
  var root=Path.Combine(parent,"workspace-tests");Directory.CreateDirectory(root);
  void Invalid(Action a,string name){try{a();}catch(ArgumentException){check(true,name);return;}throw new Exception("FAIL: "+name);}
  var normal=DevelopmentWorkspace.Resolve(root,Array.Empty<string>());
  check(!normal.IsAcceptance&&normal.DatabasePath==Path.Combine(root,"CRC Practice Manager DEV","practice-dev.db"),"default development workspace unchanged");
  Directory.CreateDirectory(Path.GetDirectoryName(normal.DatabasePath)!);File.WriteAllText(normal.DatabasePath,"synthetic protected default");
  Invalid(()=>DevelopmentWorkspace.Resolve(root,new[]{"--unknown"}),"unknown startup arguments fail closed");
  Invalid(()=>DevelopmentWorkspace.Resolve(root,new[]{"--acceptance-session","../practice-dev.db"}),"arbitrary path cannot select acceptance workspace");
  Invalid(()=>DevelopmentWorkspace.Resolve(root,new[]{"--acceptance-new","extra"}),"new acceptance requires exact arguments");
  var one=DevelopmentWorkspace.Resolve(root,new[]{"--acceptance-new"});
  var two=DevelopmentWorkspace.Resolve(root,new[]{"--acceptance-new"});
  check(one.IsAcceptance&&two.IsAcceptance&&one.AcceptanceId!=two.AcceptanceId&&one.DatabasePath!=two.DatabasePath&&one.DatabasePath!=normal.DatabasePath,"fresh acceptance workspaces isolated");
  check(!File.Exists(one.DatabasePath)&&!File.Exists(two.DatabasePath),"new acceptance paths have no preexisting database");
  var store=new LocalPractice(one.DatabasePath);check(!store.IsInitialized(),"acceptance store begins empty");
  store.Bootstrap("Synthetic acceptance firm","acceptance-admin","Synthetic-acceptance-passphrase-001","QA");
  var resumed=DevelopmentWorkspace.Resolve(root,new[]{"--acceptance-session",one.AcceptanceId!});
  check(resumed==one&&new LocalPractice(resumed.DatabasePath).IsInitialized(),"exact acceptance session may be reopened");
  Invalid(()=>DevelopmentWorkspace.Resolve(root,new[]{"--acceptance-session",Guid.NewGuid().ToString("N")}),"unknown acceptance session cannot initialize silently");
  Invalid(()=>DevelopmentWorkspace.Resolve(root,new[]{"--acceptance-session",one.AcceptanceId!.ToUpperInvariant()}),"noncanonical session identifier rejected");
  Invalid(()=>DevelopmentWorkspace.Resolve(root,new[]{"--acceptance-session",Guid.Empty.ToString("N")}),"empty session identifier rejected");
  Invalid(()=>DevelopmentWorkspace.Resolve(root,new[]{"--acceptance-session",two.AcceptanceId!}),"unfinished acceptance workspace cannot silently reopen");
  var marker=Path.Combine(Path.GetDirectoryName(one.DatabasePath)!,"acceptance-session.txt");File.WriteAllText(marker,"wrong");
  Invalid(()=>DevelopmentWorkspace.Resolve(root,new[]{"--acceptance-session",one.AcceptanceId!}),"altered session identity rejected");
  Invalid(()=>DevelopmentWorkspace.Resolve(root,new[]{"--production"}),"production startup remains disabled");
  Invalid(()=>LocalStoragePath.Normalize(Path.Combine(root,"..","unsafe.db")),"storage rejects traversal before normalization");
  Invalid(()=>LocalStoragePath.Normalize(Path.Combine(root,"NUL.db")),"storage rejects reserved device name");
  Invalid(()=>LocalStoragePath.Normalize(Path.Combine(root,"bad.db:stream")),"storage rejects alternate stream");
  Invalid(()=>LocalStoragePath.Normalize(Path.Combine(root,"bad.")),"storage rejects trailing dot");
  var side=normal.DatabasePath+"-wal";Directory.CreateDirectory(side);
  try{Invalid(()=>DevelopmentWorkspace.Resolve(root,Array.Empty<string>()),"development startup rejects sidecar directory");}
  finally{Directory.Delete(side);}
  var missing=Path.Combine(root,"new.db");File.WriteAllText(missing+"-journal","synthetic");
  Invalid(()=>LocalStoragePath.FilePath(missing,mustExist:false),"new database refuses orphan journal");
  File.Delete(missing+"-journal");
  var real=Path.Combine(root,"real-local");Directory.CreateDirectory(real);
  var link=Path.Combine(root,"redirected");
  bool linked=false;
  try{Directory.CreateSymbolicLink(link,real);linked=true;}
  catch(UnauthorizedAccessException){Console.WriteLine("SKIP: workspace symlink creation unavailable");}
  catch(IOException){Console.WriteLine("SKIP: workspace symlink creation unavailable");}
  catch(PlatformNotSupportedException){Console.WriteLine("SKIP: workspace symlink creation unavailable");}
  if(linked)
  {
   try{Invalid(()=>LocalStoragePath.DirectoryPath(Path.Combine(link,"new"),create:true),"storage rejects redirected ancestor before creation");
    check(!Directory.Exists(Path.Combine(real,"new")),"redirected ancestor remains untouched");}
   finally{Directory.Delete(link);}
  }
  check(File.ReadAllText(normal.DatabasePath)=="synthetic protected default","acceptance startup never opens or modifies default store");
 }
}
