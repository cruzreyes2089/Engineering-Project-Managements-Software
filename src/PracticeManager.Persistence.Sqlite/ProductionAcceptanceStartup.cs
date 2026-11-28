using System.Text;
using PracticeManager.Core;
namespace PracticeManager.Persistence;

// Explicit isolated acceptance route only. No caller-selected project, data or
// backup paths. Does not enable normal production startup.
public sealed record ProductionAcceptanceSession(string SessionId,LocalPractice Service);
public static class ProductionAcceptanceStartup
{
 const string RootName="CRC Practice Manager PRODUCTION ACCEPTANCE";
 const string MarkerName="production-acceptance-session.txt";
 static string CanonicalId(string id)
 {
  if(!Guid.TryParseExact(id,"N",out var parsed)||parsed==Guid.Empty||parsed.ToString("N")!=id)
   throw new ArgumentException("Exact lowercase acceptance session ID required.");
  return id;
 }
 static string ProtectedDirectory(string path,bool create)
 {
  var full=create?WorkspaceConfidentiality.EnsureWorkspaceDirectory(path):LocalStoragePath.DirectoryPath(path);
  if(!OperatingSystem.IsWindows()||!WorkspaceConfidentiality.HasRestrictedDirectoryAcl(full))
   throw new ArgumentException("Acceptance directory permissions require review.");
  return full;
 }
 static string ReadBinding(string session,string id)
 {
  var path=LocalStoragePath.FilePath(Path.Combine(session,MarkerName),mustExist:true,rejectSidecars:true);
  if(!WorkspaceConfidentiality.HasWorkspaceFileAcl(path))throw new ArgumentException("Acceptance marker permissions require review.");
  using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
  if(stream.Length!=65)throw new ArgumentException("Acceptance binding invalid.");
  var bytes=new byte[65];stream.ReadExactly(bytes);
  var text=Encoding.ASCII.GetString(bytes);
  if(!text.StartsWith(id+":",StringComparison.Ordinal))throw new ArgumentException("Acceptance binding mismatch.");
  return CanonicalId(text[33..]);
 }
 public static ProductionAcceptanceSession Open(string localAppData,IReadOnlyList<string> arguments)
 {
  if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException("Windows acceptance required.");
  var create=arguments.Count==1&&arguments[0]=="--production-acceptance-new";
  var reopen=arguments.Count==2&&arguments[0]=="--production-acceptance-session";
  if(!create&&!reopen)throw new ArgumentException("Explicit production acceptance session required.");
  var id=create?Guid.NewGuid().ToString("N"):CanonicalId(arguments[1]);
  var profile=LocalStoragePath.DirectoryPath(localAppData);
  var root=ProtectedDirectory(Path.Combine(profile,RootName),create);
  var session=Path.Combine(root,id);
  if(create)
  {
   try{_=File.GetAttributes(session);throw new ArgumentException("Acceptance session already exists.");}
   catch(FileNotFoundException){}catch(DirectoryNotFoundException){}
   ProtectedDirectory(session,true);
   if(Directory.EnumerateFileSystemEntries(session).Any())throw new ArgumentException("New acceptance session is not empty.");
   var projects=ProtectedDirectory(Path.Combine(session,"synthetic-projects"),true);
   var backup=ProtectedDirectory(Path.Combine(session,"synthetic-backups"),true);
   var layout=ProductionStorageBoundary.Review(session,projects,backup);
   var identity=ProductionWorkspaceIdentityCandidate.InitializeNewFixture(layout);
   var marker=LocalStoragePath.FilePath(Path.Combine(session,MarkerName),mustExist:false,mustBeNew:true,rejectSidecars:true);
   using(var stream=new FileStream(marker,FileMode.CreateNew,FileAccess.Write,FileShare.None))
   {stream.Write(Encoding.ASCII.GetBytes(id+":"+identity));stream.Flush(true);}
  }
  ProtectedDirectory(session,false);
  var expected=ReadBinding(session,id);
  var stored=ProductionWorkspaceIdentityCandidate.ReadStoredLayout(session,expected);
  if(stored.ProjectRoot!=Path.Combine(session,"synthetic-projects")||
     stored.BackupRoot!=Path.Combine(session,"synthetic-backups"))
   throw new ArgumentException("Acceptance locations changed; no alternate workspace opened.");
  var service=LocalPractice.OpenProductionFixture(stored,expected);
  return new(id,service);
 }
}
