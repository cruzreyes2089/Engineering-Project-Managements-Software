using System.Security.Cryptography;
using System.Text.Json;
using PracticeManager.Core;

// Physical provisioning is qualified ONLY in a smoke-test-owned synthetic workspace.
// No application entry point, existing business folder adoption, ACL or live storage approval.
internal sealed class SyntheticFolderProvisioning
{
 internal sealed record Step(string Boundary,string RelativeDirectory,bool Exists);
 internal sealed record Review(string PlanFingerprint,string ReviewFingerprint,IReadOnlyList<Step> Steps);
 readonly string production,restricted;
 readonly long projectId;
 readonly string projectNumber;
 public SyntheticFolderProvisioning(string ownedRoot,long projectId,string projectNumber)
 {
  this.projectId=projectId;this.projectNumber=projectNumber;
  var root=Path.GetFullPath(ownedRoot);
  RequireDirectory(root);
  production=Path.Combine(root,"SyntheticProduction");
  restricted=Path.Combine(root,"SyntheticRestricted");
  RequireDirectory(production);RequireDirectory(restricted);
 }
 static FileAttributes? Attributes(string path)
 {
  try{return File.GetAttributes(path);}
  catch(FileNotFoundException){return null;}
  catch(DirectoryNotFoundException){return null;}
 }
 static bool IsDirectory(string path)
 {
  var a=Attributes(path);
  if(a is null)return false;
  if((a.Value&FileAttributes.ReparsePoint)!=0||(a.Value&FileAttributes.Directory)==0)
   throw new IOException("Synthetic folder path is obstructed or redirected.");
  return true;
 }
 static void RequireDirectory(string path)
 {
  var current=new DirectoryInfo(path);
  while(current is not null)
  {
   if(!IsDirectory(current.FullName))throw new IOException("Synthetic root unavailable.");
   current=current.Parent;
  }
 }
 string Resolve(ProjectFolderPlanEntry entry)
 {
  var root=entry.Boundary switch{"Production"=>production,"Restricted"=>restricted,_=>throw new ArgumentException("Unknown boundary.")};
  RequireDirectory(root);
  var parts=entry.RelativeDirectory.Split('/');
  if(parts.Length is <1 or >2||parts[0]!=projectNumber||parts.Any(x=>x is "." or ".."||x.Contains('\\')||x.Contains(':')))
   throw new ArgumentException("Unexpected synthetic path.");
  var target=Path.GetFullPath(Path.Combine(root,Path.Combine(parts)));
  if(!target.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))
   throw new ArgumentException("Path escapes synthetic root.");
  return target;
 }
 static string Hash(object value)=>Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value))).ToLowerInvariant();
 Review Build(bool correspondence,bool includeRestricted)
 {
  var plan=ProjectFolderPlanning.Build(projectId,projectNumber,correspondence,includeRestricted);
  var steps=new List<Step>();
  foreach(var entry in plan.Entries)
  {
   var target=Resolve(entry);
   // For the flat v2 template the project directory precedes every child.
   var parent=Path.GetDirectoryName(target)!;
   if(IsDirectory(parent))RequireDirectory(parent);
   steps.Add(new(entry.Boundary,entry.RelativeDirectory,IsDirectory(target)));
  }
  return new(plan.Fingerprint,Hash(new{plan.Fingerprint,production,restricted,steps}),steps.AsReadOnly());
 }
 public Review Preview(bool correspondence,bool includeRestricted)=>Build(correspondence,includeRestricted);
 public (int Created,int AlreadyPresent) Apply(bool correspondence,bool includeRestricted,
  string expectedReviewFingerprint,bool confirmed,Action<int>? afterCreate=null)
 {
  if(!confirmed)throw new ArgumentException("Explicit synthetic review required.");
  var reviewed=Build(correspondence,includeRestricted);
  if(reviewed.ReviewFingerprint!=expectedReviewFingerprint)throw new ArgumentException("Stale synthetic folder review.");
  var created=0;var existing=0;
  foreach(var step in reviewed.Steps)
  {
   var target=Resolve(new(step.Boundary,step.RelativeDirectory));
   RequireDirectory(Path.GetDirectoryName(target)!);
   var present=IsDirectory(target);
   if(present!=step.Exists)throw new IOException("Synthetic directory changed after review.");
   if(present){existing++;continue;}
   Directory.CreateDirectory(target);
   RequireDirectory(target);
   created++;
   afterCreate?.Invoke(created);
  }
  var final=Build(correspondence,includeRestricted);
  if(final.Steps.Any(s=>!s.Exists))throw new IOException("Synthetic folder verification failed.");
  return(created,existing);
 }
}

internal static class SyntheticFolderProvisioningTests
{
 public static void Run(string ownedRoot,Action<bool,string> check)
 {
  // Only called by the ordinary no-argument smoke suite in its private temporary root.
  var root=Path.Combine(ownedRoot,"folder-provisioning-"+Guid.NewGuid().ToString("N"));
  Directory.CreateDirectory(root);
  var production=Directory.CreateDirectory(Path.Combine(root,"SyntheticProduction")).FullName;
  var restricted=Directory.CreateDirectory(Path.Combine(root,"SyntheticRestricted")).FullName;
  const string number="PRJ-000001";
  var candidate=new SyntheticFolderProvisioning(root,1,number);
  var pp=Path.Combine(production,number);var rp=Path.Combine(restricted,number);
  void Reject<T>(Action action,string label) where T:Exception
  {
   try{action();}catch(T){check(true,label);return;}
   throw new Exception("FAIL: "+label);
  }
  var preview=candidate.Preview(false,false);
  check(preview.Steps.Count==10&&preview.Steps.All(s=>!s.Exists)&&!Directory.Exists(pp)&&!Directory.Exists(rp),
   "folder filesystem preview is nonmutating and production scoped");
  Reject<ArgumentException>(()=>candidate.Apply(false,false,preview.ReviewFingerprint,false),"folder creation requires explicit confirmation");
  Reject<ArgumentException>(()=>candidate.Apply(true,false,preview.ReviewFingerprint,true),"folder selection change invalidates review");
  check(!Directory.Exists(pp)&&!Directory.Exists(rp),"rejected folder operations leave roots unchanged");
  Directory.CreateDirectory(pp);
  Reject<ArgumentException>(()=>candidate.Apply(false,false,preview.ReviewFingerprint,true),"external folder change invalidates review");
  // An ordinary file collision must be found before any directory is created.
  var obstruction=Path.Combine(pp,"QA-QC");File.WriteAllText(obstruction,"synthetic collision");
  Reject<IOException>(()=>candidate.Preview(false,false),"folder file collision fails closed");
  check(File.ReadAllText(obstruction)=="synthetic collision"&&Directory.GetDirectories(pp).Length==0,"collision preserves existing file and directories");
  File.Delete(obstruction); // Test fixture cleanup only; the candidate never deletes anything.
  var retained=Path.Combine(pp,"existing.txt");File.WriteAllText(retained,"retain");
  var reviewed=candidate.Preview(false,false);
  Reject<IOException>(()=>candidate.Apply(false,false,reviewed.ReviewFingerprint,true,n=>{if(n==3)throw new IOException("Synthetic interruption");}),
   "folder interruption returns no success");
  check(Directory.GetDirectories(pp).Length==3&&File.ReadAllText(retained)=="retain","partial folder creation is preserved for reviewed repair");
  var repair=candidate.Preview(false,false);
  var repaired=candidate.Apply(false,false,repair.ReviewFingerprint,true);
  check(repaired.Created==6&&repaired.AlreadyPresent==4&&Directory.GetDirectories(pp).Length==9,
   "fresh review repairs only missing directories");
  check(!Directory.Exists(rp)&&File.ReadAllText(retained)=="retain","production provisioning never creates restricted folders or overwrites files");
  var replay=candidate.Preview(false,false);
  check(candidate.Apply(false,false,replay.ReviewFingerprint,true)==(0,10),"folder replay creates nothing");
  var combined=candidate.Preview(true,true);
  var result=candidate.Apply(true,true,combined.ReviewFingerprint,true);
  check(result.Created==6&&result.AlreadyPresent==10&&Directory.GetDirectories(pp).Length==10&&Directory.GetDirectories(rp).Length==4,
   "optional correspondence and separate restricted synthetic roots");
  check(Directory.Exists(Path.Combine(pp,"Reference Documents"))&&Directory.GetDirectories(Path.Combine(pp,"Reference Documents")).Length==0,
   "physical reference folder contains no automatic MEP subdirectories");
  var other=new SyntheticFolderProvisioning(root,2,"PRJ-000002");
  Reject<ArgumentException>(()=>other.Apply(true,true,combined.ReviewFingerprint,true),"folder review cannot be transferred to another project");
  check(!Directory.Exists(Path.Combine(production,"PRJ-000002")),"other project remains untouched");
  // Reparse qualification uses a real directory symlink only if the test environment permits creation.
  // No privileges or developer mode are changed. Test cleanup removes only the link itself.
  var linkRoot=Path.Combine(production,"PRJ-000003");
  var outside=Directory.CreateDirectory(Path.Combine(root,"SyntheticUnrelated")).FullName;
  bool linkCreated=false;
  try
  {
   try{Directory.CreateSymbolicLink(linkRoot,outside);linkCreated=true;}
   catch(UnauthorizedAccessException){}
   catch(IOException){}
   catch(PlatformNotSupportedException){}
   if(linkCreated)
   {
    var linked=new SyntheticFolderProvisioning(root,3,"PRJ-000003");
    Reject<IOException>(()=>linked.Preview(false,false),"folder reparse-point target denied");
    check(Directory.GetFileSystemEntries(outside).Length==0,"redirected folder target remains untouched");
   }
   else Console.WriteLine("NOT QUALIFIED: directory symlink creation unavailable in test environment.");
  }
  finally{if(linkCreated)Directory.Delete(linkRoot,false);}
 }
}
