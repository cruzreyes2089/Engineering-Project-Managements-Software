using System.Text;
namespace PracticeManager.Core;

// Prevent accidental selection of the normal development store during acceptance.
// The local profile and filesystem must be trusted; this is not a security sandbox.
public sealed record DevelopmentWorkspace(string DatabasePath,string? AcceptanceId)
{
 const string MarkerName="acceptance-session.txt";
 const string DatabaseName="practice-acceptance.db";
 public bool IsAcceptance=>AcceptanceId!=null;
 private static void PlainDirectory(string path)
 {
  if(!Directory.Exists(path)||(File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new ArgumentException("Acceptance workspace directory missing or redirected.");
 }
 private static void PlainFile(string path)
 {
  if(!File.Exists(path)||(File.GetAttributes(path)&(FileAttributes.ReparsePoint|FileAttributes.Directory))!=0)throw new ArgumentException("Acceptance workspace file missing or redirected.");
 }
 public static DevelopmentWorkspace Resolve(string localAppData,IReadOnlyList<string> arguments)
 {
  if(string.IsNullOrWhiteSpace(localAppData)||!Path.IsPathFullyQualified(localAppData))throw new ArgumentException("Trusted local application data folder required.");
  var root=Path.GetFullPath(localAppData);
  if(arguments.Count==0)return new(Path.Combine(root,"CRC Practice Manager DEV","practice-dev.db"),null);
  var create=arguments.Count==1&&arguments[0]=="--acceptance-new";
  var reopen=arguments.Count==2&&arguments[0]=="--acceptance-session";
  if(!create&&!reopen)throw new ArgumentException("Unsupported startup arguments. No default workspace opened.");
  var id=create?Guid.NewGuid().ToString("N"):arguments[1];
  if(!Guid.TryParseExact(id,"N",out var parsed)||parsed==Guid.Empty||parsed.ToString("N")!=id)throw new ArgumentException("Exact lowercase acceptance session ID required.");
  var acceptanceRoot=Path.Combine(root,"CRC Practice Manager ACCEPTANCE");
  var folder=Path.Combine(acceptanceRoot,id);
  var marker=Path.Combine(folder,MarkerName);
  var db=Path.Combine(folder,DatabaseName);
  if(create)
  {
   Directory.CreateDirectory(acceptanceRoot);PlainDirectory(acceptanceRoot);
   if(Directory.Exists(folder)||File.Exists(folder))throw new ArgumentException("Acceptance session already exists; no existing workspace opened.");
   Directory.CreateDirectory(folder);PlainDirectory(folder);
   using(var stream=new FileStream(marker,FileMode.CreateNew,FileAccess.Write,FileShare.None)){var bytes=Encoding.ASCII.GetBytes(id);stream.Write(bytes);stream.Flush(true);}
   if(File.Exists(db)||Directory.Exists(db))throw new ArgumentException("Unexpected existing acceptance database.");
  }
  else
  {
   PlainDirectory(acceptanceRoot);PlainDirectory(folder);PlainFile(marker);PlainFile(db);
   using var stream=new FileStream(marker,FileMode.Open,FileAccess.Read,FileShare.Read);
   if(stream.Length!=32)throw new ArgumentException("Acceptance session marker mismatch.");
   var bytes=new byte[32];stream.ReadExactly(bytes);
   if(Encoding.ASCII.GetString(bytes)!=id)throw new ArgumentException("Acceptance session marker mismatch.");
  }
  return new(db,id);
 }
}
