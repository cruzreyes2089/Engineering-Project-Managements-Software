using PracticeManager.Core;
using PracticeManager.Persistence;
internal static class DevelopmentWorkspaceTests
{
 internal static void Run(string parent,Action<bool,string> check)
 {
  var root=Path.Combine(parent,"workspace-tests");Directory.CreateDirectory(root);
  void Invalid(Action a,string name){try{a();}catch(ArgumentException){check(true,name);return;}throw new Exception("FAIL: "+name);}
  Invalid(()=>DevelopmentWorkspace.Resolve(Path.Combine(root,"missing-profile"),Array.Empty<string>()),"missing profile fails closed");
  var collision=Path.Combine(root,"collision");Directory.CreateDirectory(collision);
  File.WriteAllText(Path.Combine(collision,"CRC Practice Manager DEV"),"synthetic collision");
  Invalid(()=>DevelopmentWorkspace.Resolve(collision,Array.Empty<string>()),"development directory collision blocked");
  File.Delete(Path.Combine(collision,"CRC Practice Manager DEV"));Directory.CreateDirectory(Path.Combine(collision,"CRC Practice Manager DEV","practice-dev.db"));
  Invalid(()=>DevelopmentWorkspace.Resolve(collision,Array.Empty<string>()),"development database directory collision blocked");
  File.WriteAllText(Path.Combine(collision,"CRC Practice Manager ACCEPTANCE"),"synthetic collision");
  Invalid(()=>DevelopmentWorkspace.Resolve(collision,new[]{"--acceptance-new"}),"acceptance directory collision blocked");
  var normal=DevelopmentWorkspace.Resolve(root,Array.Empty<string>());
  check(!normal.IsAcceptance&&normal.DatabasePath==Path.Combine(root,"CRC Practice Manager DEV","practice-dev.db"),"default development workspace unchanged");
  Directory.CreateDirectory(Path.GetDirectoryName(normal.DatabasePath)!);File.WriteAllText(normal.DatabasePath,"synthetic protected default");
  Invalid(()=>DevelopmentWorkspace.Resolve(root,new[]{"--unknown"}),"unknown startup arguments fail closed");
  Invalid(()=>DevelopmentWorkspace.Resolve(root,new[]{"--production"}),"production startup remains disabled");
  Invalid(()=>DevelopmentWorkspace.Resolve(root,new[]{"--database",normal.DatabasePath}),"arbitrary database startup remains disabled");
  foreach(var suffix in new[]{"-wal","-shm","-journal"})
  {
   var sidecar=normal.DatabasePath+suffix;
   Directory.CreateDirectory(sidecar);
   Invalid(()=>DevelopmentWorkspace.Resolve(root,Array.Empty<string>()),"development sidecar directory blocked "+suffix);
   Directory.Delete(sidecar);
   File.WriteAllText(sidecar,"synthetic sidecar");
   check(DevelopmentWorkspace.Resolve(root,Array.Empty<string>())==normal&&File.ReadAllText(sidecar)=="synthetic sidecar","existing ordinary sidecar preserved "+suffix);
   File.Delete(sidecar);
  }
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
  var sidecarCollision=one.DatabasePath+"-journal";Directory.CreateDirectory(sidecarCollision);
  Invalid(()=>DevelopmentWorkspace.Resolve(root,new[]{"--acceptance-session",one.AcceptanceId!}),"acceptance sidecar directory blocked");
  Directory.Delete(sidecarCollision);
  var marker=Path.Combine(Path.GetDirectoryName(one.DatabasePath)!,"acceptance-session.txt");File.WriteAllText(marker,"wrong");
  Invalid(()=>DevelopmentWorkspace.Resolve(root,new[]{"--acceptance-session",one.AcceptanceId!}),"altered session identity rejected");
  check(File.ReadAllText(normal.DatabasePath)=="synthetic protected default","acceptance startup never opens or modifies default store");
 }
}
