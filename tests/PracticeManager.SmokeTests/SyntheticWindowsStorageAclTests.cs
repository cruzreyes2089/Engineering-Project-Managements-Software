using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;

// Strict owner-only synthetic policy. No actual firm user/group policy or ACL changes
// outside the test-created roots. Descriptor inspection is not an effective access test.
[SupportedOSPlatform("windows")]
internal static class SyntheticWindowsStorageAclTests
{
 const InheritanceFlags Inherit=InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit;
 static SecurityIdentifier CurrentUser()
 {
  using var identity=WindowsIdentity.GetCurrent();
  return identity.User??throw new InvalidOperationException("Synthetic Windows identity unavailable.");
 }
 static SecurityIdentifier SystemSid()=>new(WellKnownSidType.LocalSystemSid,null);
 static SecurityIdentifier[] Principals(SecurityIdentifier owner)=>new[]{owner,SystemSid()}.Distinct().ToArray();
 static DirectorySecurity NewPolicy(SecurityIdentifier owner)
 {
  var security=new DirectorySecurity();
  security.SetOwner(owner);
  security.SetAccessRuleProtection(true,false);
  foreach(var sid in Principals(owner))
   security.AddAccessRule(new FileSystemAccessRule(sid,FileSystemRights.FullControl,Inherit,
    PropagationFlags.None,AccessControlType.Allow));
  return security;
 }
 static DirectorySecurity Read(string path)=>new DirectoryInfo(path).GetAccessControl(AccessControlSections.Owner|AccessControlSections.Access);
 static void Set(string path,DirectorySecurity acl)=>new DirectoryInfo(path).SetAccessControl(acl);
 static string Verify(string path,SecurityIdentifier owner,bool root)
 {
  using var pin=SyntheticWindowsDirectoryIdentity.Open(path);
  var descriptor=Read(path).GetSecurityDescriptorBinaryForm();
  var raw=new RawSecurityDescriptor(descriptor,0);
  var dacl=raw.DiscretionaryAcl;
  if(dacl==null||(raw.ControlFlags&ControlFlags.DiscretionaryAclPresent)==0||
    ((raw.ControlFlags&ControlFlags.DiscretionaryAclProtected)!=0)!=root||
    (root&&!owner.Equals(raw.Owner)))
   throw new UnauthorizedAccessException("Synthetic storage descriptor policy mismatch.");
  var expected=Principals(owner).ToHashSet();
  var seen=new HashSet<SecurityIdentifier>();
  var expectedFlags=AceFlags.ContainerInherit|AceFlags.ObjectInherit|(root?AceFlags.None:AceFlags.Inherited);
  foreach(GenericAce ace in dacl)
  {
   if(ace is not CommonAce common||common.AceQualifier!=AceQualifier.AccessAllowed||
     common.IsCallback||common.AceFlags!=expectedFlags||
     common.AccessMask!=(int)FileSystemRights.FullControl||
     !expected.Contains(common.SecurityIdentifier)||!seen.Add(common.SecurityIdentifier))
    throw new UnauthorizedAccessException("Unexpected synthetic storage access entry.");
  }
  if(!seen.SetEquals(expected))throw new UnauthorizedAccessException("Required synthetic storage access missing.");
  return Convert.ToHexString(SHA256.HashData(descriptor)).ToLowerInvariant();
 }
 static string Review(string root,SyntheticFolderProvisioning folders,SecurityIdentifier owner)
 {
  var production=Path.Combine(root,"SyntheticProduction");
  var restricted=Path.Combine(root,"SyntheticRestricted");
  var evidence=new List<string>{Verify(production,owner,true),Verify(restricted,owner,true)};
  var observation=folders.Preview(false,true);
  foreach(var step in observation.Steps.Where(x=>x.Exists))
  {
   var boundary=step.Boundary=="Production"?production:restricted;
   var path=Path.Combine(boundary,Path.Combine(step.RelativeDirectory.Split('/')));
   evidence.Add(Verify(path,owner,false));
  }
  return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new{observation.ReviewFingerprint,evidence}))).ToLowerInvariant();
 }
 public static void Run(string ownedRoot,Action<bool,string> check)
 {
  var root=Path.Combine(ownedRoot,"synthetic-acl-"+Guid.NewGuid().ToString("N"));
  Directory.CreateDirectory(root);
  var prod=Directory.CreateDirectory(Path.Combine(root,"SyntheticProduction")).FullName;
  var restricted=Directory.CreateDirectory(Path.Combine(root,"SyntheticRestricted")).FullName;
  var owner=CurrentUser();
  var world=new SecurityIdentifier(WellKnownSidType.WorldSid,null);
  void Reject(Action action,string label)
  {
   try{action();}catch(UnauthorizedAccessException){check(true,label);return;}
   throw new Exception("FAIL: "+label);
  }
  // Apply only to two new empty test roots; owner and SYSTEM retain full access.
  Set(prod,NewPolicy(owner));Set(restricted,NewPolicy(owner));
  var folders=new SyntheticFolderProvisioning(root,41,"PRJ-000041");
  var first=Review(root,folders,owner);
  check(first.Length==64&&first==Review(root,folders,owner),"Windows synthetic protected ACL readback stable");
  var permissive=Read(restricted);
  permissive.AddAccessRule(new FileSystemAccessRule(world,FileSystemRights.ReadAndExecute,Inherit,
   PropagationFlags.None,AccessControlType.Allow));
  Set(restricted,permissive);
  Reject(()=>Review(root,folders,owner),"Windows broad read grant rejected in restricted root");
  check(!Directory.Exists(Path.Combine(prod,"PRJ-000041"))&&!Directory.Exists(Path.Combine(restricted,"PRJ-000041")),
   "ACL review does not create project directories");
  Set(restricted,NewPolicy(owner));
  check(Review(root,folders,owner)==first,"restored restricted ACL reconciles review");
  var preview=folders.Preview(false,true);
  var result=folders.Apply(false,true,preview.ReviewFingerprint,true);
  check(result==(15,0),"synthetic secured fixture creates separate complete trees");
  var confirmed=Review(root,folders,owner);
  check(confirmed!=first&&confirmed==Review(root,folders,owner),"Windows inherited ACL verified on every created directory");
  var child=Path.Combine(restricted,"PRJ-000041","Billing Administration");
  var childAcl=Read(child);
  childAcl.AddAccessRule(new FileSystemAccessRule(world,FileSystemRights.ReadAndExecute,Inherit,
   PropagationFlags.None,AccessControlType.Allow));
  Set(child,childAcl);
  Reject(()=>Review(root,folders,owner),"Windows child explicit access expansion rejected");
  // Restore the original inherited-only policy on this synthetic child for cleanup.
  var repaired=Read(child);
  repaired.PurgeAccessRules(world);
  Set(child,repaired);
  check(Review(root,folders,owner)==confirmed,"synthetic ACL repair restores reviewed descriptor");
  var productionChild=Path.Combine(prod,"PRJ-000041","QA-QC");
  var protectedChild=Read(productionChild);
  protectedChild.SetAccessRuleProtection(true,true);
  Set(productionChild,protectedChild);
  Reject(()=>Review(root,folders,owner),"Windows unexpected protected child ACL rejected");
  // Re-enable inheritance and clear explicit rules copied from inherited rules.
  var inherited=Read(productionChild);
  foreach(var sid in Principals(owner))inherited.PurgeAccessRules(sid);
  inherited.SetAccessRuleProtection(false,false);
  Set(productionChild,inherited);
  check(Review(root,folders,owner)==confirmed,"synthetic child inheritance restored");
 }
}
