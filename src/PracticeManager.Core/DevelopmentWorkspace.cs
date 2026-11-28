using System.Text;
namespace PracticeManager.Core;

// Prevent accidental selection of the normal development store during acceptance.
// The local profile and filesystem must be trusted; this is not a security sandbox.
public sealed record DevelopmentWorkspace(string DatabasePath,string? AcceptanceId)
{
 const string MarkerName="acceptance-session.txt";
 const string DatabaseName="practice-acceptance.db";
 public bool IsAcceptance=>AcceptanceId!=null;
 public bool IsBudgetAcceptance{get;init;}
 public bool IsDocumentAcceptance{get;init;}
 static string ProtectedDirectory(string path,bool create)
 {
  var full=create?WorkspaceConfidentiality.EnsureWorkspaceDirectory(path):LocalStoragePath.DirectoryPath(path);
  if(OperatingSystem.IsWindows()&&!WorkspaceConfidentiality.HasRestrictedDirectoryAcl(full))throw new ArgumentException("Workspace directory permissions require review. No existing permissions were changed and no database was opened.");
  return full;
 }
 static string ProtectedFile(string path,bool mustExist,bool mustBeNew=false,bool rejectSidecars=false)
 {
  var full=LocalStoragePath.FilePath(path,mustExist,mustBeNew,rejectSidecars);
  if(!OperatingSystem.IsWindows())return full;
  void Inspect(string entry,bool required)
  {
   try{_=File.GetAttributes(entry);}
   catch(FileNotFoundException) when(!required){return;}
   if(!WorkspaceConfidentiality.HasWorkspaceFileAcl(entry))throw new ArgumentException("Workspace file permissions require review. No existing permissions were changed and no database was opened.");
  }
  Inspect(full,mustExist);
  if(!rejectSidecars)foreach(var suffix in new[]{"-wal","-shm","-journal"})Inspect(full+suffix,false);
  return full;
 }
 public DevelopmentWorkspace RevalidateExisting(string localAppData)
 {
  var root=LocalStoragePath.DirectoryPath(localAppData);
  DevelopmentWorkspace current;
  if(IsAcceptance){
   var mode=IsDocumentAcceptance?"--document-acceptance-session":IsBudgetAcceptance?"--budget-acceptance-session":"--acceptance-session";
   current=Resolve(root,new[]{mode,AcceptanceId!});
  }else{
   var dev=ProtectedDirectory(Path.Combine(root,"CRC Practice Manager DEV"),false);
   current=new(ProtectedFile(Path.Combine(dev,"practice-dev.db"),mustExist:true),null);
  }
  if(current!=this)throw new ArgumentException("Workspace identity changed. No fallback database opened.");
  return current;
 }
 public static DevelopmentWorkspace Resolve(string localAppData,IReadOnlyList<string> arguments)=>Resolve(localAppData,arguments,out _);
 public static DevelopmentWorkspace Resolve(string localAppData,IReadOnlyList<string> arguments,out bool newDefaultWorkspace)
 {
  newDefaultWorkspace=false;
  if(string.IsNullOrWhiteSpace(localAppData)||!Path.IsPathFullyQualified(localAppData))throw new ArgumentException("Trusted local application data folder required.");
  var root=LocalStoragePath.DirectoryPath(localAppData);
  if(arguments.Count==0)
  {
   var directory=Path.Combine(root,"CRC Practice Manager DEV");
   // An existing workspace directory with a missing DB is a recovery case,
   // not a fresh start. Never replace or initialize it automatically.
   var exists=true;
   try{_=File.GetAttributes(directory);}catch(FileNotFoundException){exists=false;}catch(DirectoryNotFoundException){exists=false;}
   var dev=ProtectedDirectory(directory,!exists);
   var defaultDatabase=ProtectedFile(Path.Combine(dev,"practice-dev.db"),mustExist:exists,mustBeNew:!exists,rejectSidecars:!exists);
   newDefaultWorkspace=!exists;
   return new(defaultDatabase,null);
  }
  var budget=arguments.Count>0&&arguments[0] is "--budget-acceptance-new" or "--budget-acceptance-session";
  var document=arguments.Count>0&&arguments[0] is "--document-acceptance-new" or "--document-acceptance-session";
  var create=arguments.Count==1&&(arguments[0]=="--acceptance-new"||arguments[0]=="--budget-acceptance-new"||arguments[0]=="--document-acceptance-new");
  var reopen=arguments.Count==2&&(arguments[0]=="--acceptance-session"||arguments[0]=="--budget-acceptance-session"||arguments[0]=="--document-acceptance-session");
  if(!create&&!reopen)throw new ArgumentException("Unsupported startup arguments. No default workspace opened.");
  var id=create?Guid.NewGuid().ToString("N"):arguments[1];
  if(!Guid.TryParseExact(id,"N",out var parsed)||parsed==Guid.Empty||parsed.ToString("N")!=id)throw new ArgumentException("Exact lowercase acceptance session ID required.");
  var acceptanceRoot=Path.Combine(root,document?"CRC Practice Manager DOCUMENT ACCEPTANCE":budget?"CRC Practice Manager BUDGET ACCEPTANCE":"CRC Practice Manager ACCEPTANCE");
  var folder=Path.Combine(acceptanceRoot,id);
  var marker=Path.Combine(folder,document?"document-acceptance-session.txt":budget?"budget-acceptance-session.txt":MarkerName);
  var db=Path.Combine(folder,document?"practice-document-acceptance.db":budget?"practice-budget-acceptance.db":DatabaseName);
  if(create)
  {
   ProtectedDirectory(acceptanceRoot,true);
   if(Directory.Exists(folder)||File.Exists(folder))throw new ArgumentException("Acceptance session already exists; no existing workspace opened.");
   ProtectedDirectory(folder,true);
   using(var stream=new FileStream(marker,FileMode.CreateNew,FileAccess.Write,FileShare.None)){var bytes=Encoding.ASCII.GetBytes(id);stream.Write(bytes);stream.Flush(true);}
   ProtectedFile(marker,mustExist:true,rejectSidecars:true);
   ProtectedFile(db,mustExist:false,mustBeNew:true,rejectSidecars:true);
  }
  else
  {
   ProtectedDirectory(acceptanceRoot,false);ProtectedDirectory(folder,false);ProtectedFile(marker,mustExist:true,rejectSidecars:true);ProtectedFile(db,mustExist:true);
   using var stream=new FileStream(marker,FileMode.Open,FileAccess.Read,FileShare.Read);
   if(stream.Length!=32)throw new ArgumentException("Acceptance session marker mismatch.");
   var bytes=new byte[32];stream.ReadExactly(bytes);
   if(Encoding.ASCII.GetString(bytes)!=id)throw new ArgumentException("Acceptance session marker mismatch.");
  }
  return new DevelopmentWorkspace(db,id){IsBudgetAcceptance=budget,IsDocumentAcceptance=document};
 }
}
