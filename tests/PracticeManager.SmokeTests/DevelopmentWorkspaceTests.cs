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
  Invalid(()=>DevelopmentWorkspace.Resolve(root,new[]{"--acceptance-session",two.AcceptanceId!}),"unfinished acceptance workspace cannot silently reopen");
  var marker=Path.Combine(Path.GetDirectoryName(one.DatabasePath)!,"acceptance-session.txt");File.WriteAllText(marker,"wrong");
  Invalid(()=>DevelopmentWorkspace.Resolve(root,new[]{"--acceptance-session",one.AcceptanceId!}),"altered session identity rejected");
  check(File.ReadAllText(normal.DatabasePath)=="synthetic protected default","acceptance startup never opens or modifies default store");
 }
}
