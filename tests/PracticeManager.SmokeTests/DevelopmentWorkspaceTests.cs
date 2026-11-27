using PracticeManager.Core;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
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
  if(OperatingSystem.IsWindows())VerifyWindowsAcl(root,normal,one,check);
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
 [SupportedOSPlatform("windows")]
 static void VerifyWindowsAcl(string root,DevelopmentWorkspace normal,DevelopmentWorkspace one,Action<bool,string> check)
 {
  var dev=Path.GetDirectoryName(normal.DatabasePath)!;
  var session=Path.GetDirectoryName(one.DatabasePath)!;
  check(WorkspaceConfidentiality.HasRestrictedDirectoryAcl(dev),"new development directory has restricted explicit ACL");
  check(WorkspaceConfidentiality.HasRestrictedDirectoryAcl(Path.GetDirectoryName(session)!),"new acceptance root has restricted explicit ACL");
  check(WorkspaceConfidentiality.HasRestrictedDirectoryAcl(session),"new acceptance session has restricted explicit ACL");
  using var identity=WindowsIdentity.GetCurrent();
  var trusted=new HashSet<SecurityIdentifier>{identity.User!,new(WellKnownSidType.LocalSystemSid,null),new(WellKnownSidType.BuiltinAdministratorsSid,null)};
  var marker=new FileInfo(Path.Combine(session,"acceptance-session.txt")).GetAccessControl(AccessControlSections.Access);
  var rules=marker.GetAccessRules(true,true,typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().ToArray();
  check(rules.Length>0&&rules.All(r=>r.IdentityReference is SecurityIdentifier sid&&trusted.Contains(sid)&&r.AccessControlType==AccessControlType.Allow),
   "new acceptance marker inherits restricted principals");
  var existing=Path.Combine(root,"existing-legacy-workspace");
  Directory.CreateDirectory(existing);
  var folder=new DirectoryInfo(existing);
  var before=folder.GetAccessControl(AccessControlSections.Access|AccessControlSections.Owner).GetSecurityDescriptorBinaryForm();
  WorkspaceConfidentiality.EnsureWorkspaceDirectory(existing);
  var after=folder.GetAccessControl(AccessControlSections.Access|AccessControlSections.Owner).GetSecurityDescriptorBinaryForm();
  check(before.SequenceEqual(after),"existing workspace ACL never rewritten");
  var altered=folder.GetAccessControl(AccessControlSections.Access);
  altered.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.WorldSid,null),FileSystemRights.ReadAndExecute,AccessControlType.Allow));
  folder.SetAccessControl(altered);
  check(!WorkspaceConfidentiality.HasRestrictedDirectoryAcl(existing),"unexpected broad directory grant not qualified");
 }

}
