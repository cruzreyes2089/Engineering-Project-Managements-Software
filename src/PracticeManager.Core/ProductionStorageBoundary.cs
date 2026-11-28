namespace PracticeManager.Core;

// Candidate path contract only. No directory creation, data opening, installation,
// ACL modification or production startup. Caller must separately qualify encryption,
// effective access, workspace identity and release authorization.
public sealed class ProductionStorageBoundary
{
 public string ProgramRoot{get;}
 public string DataRoot{get;}
 public string DatabasePath=>Path.Combine(DataRoot,"practice.db");
 public string IdentityMarkerPath=>Path.Combine(DataRoot,"production-workspace.txt");
 public string ProjectRoot{get;}
 public string BackupRoot{get;}
 ProductionStorageBoundary(string program,string data,string project,string backup)
 {ProgramRoot=program;DataRoot=data;ProjectRoot=project;BackupRoot=backup;}

 // Fixed program/data namespaces; project and recovery destinations are separate
 // administrative choices. Existing directories must be plain local paths.
 public static ProductionStorageBoundary Review(string localAppData,string projectRoot,string backupRoot)
 {
  var profile=LocalStoragePath.DirectoryPath(localAppData);
  var programs=Path.Combine(profile,"Programs");
  var program=LocalStoragePath.Normalize(Path.Combine(programs,"CRC Practice Manager"));
  var data=LocalStoragePath.Normalize(Path.Combine(profile,"CRC Practice Manager DATA"));
  var project=LocalStoragePath.DirectoryPath(projectRoot);
  var backup=LocalStoragePath.DirectoryPath(backupRoot);
  var roots=new[]{program,data,project,backup,
    Path.Combine(profile,"CRC Practice Manager DEV"),
    Path.Combine(profile,"CRC Practice Manager ACCEPTANCE"),
    Path.Combine(profile,"CRC Practice Manager BUDGET ACCEPTANCE"),
    Path.Combine(profile,"CRC Practice Manager DOCUMENT ACCEPTANCE")};
  for(int i=0;i<roots.Length;i++)
   for(int j=i+1;j<roots.Length;j++)
    if(Overlaps(roots[i],roots[j]))
     throw new ArgumentException("Program, practice data, project, recovery and test locations must be separate.");
  // Check any preexisting application location and all its ancestors; absent
  // locations remain absent. Never use this review to claim creation or access.
  InspectExistingAncestors(program);
  InspectExistingAncestors(data);
  return new(program,data,project,backup);
 }

 static bool Overlaps(string first,string second)
 {
  static string Prefix(string p)=>Path.EndsInDirectorySeparator(p)?p:p+Path.DirectorySeparatorChar;
  return string.Equals(first,second,StringComparison.OrdinalIgnoreCase)
   ||first.StartsWith(Prefix(second),StringComparison.OrdinalIgnoreCase)
   ||second.StartsWith(Prefix(first),StringComparison.OrdinalIgnoreCase);
 }

 static void InspectExistingAncestors(string path)
 {
  var current=path;
  while(true)
  {
   try{_=File.GetAttributes(current);}
   catch(FileNotFoundException){current=Path.GetDirectoryName(current)??throw new ArgumentException("Missing local root.");continue;}
   catch(DirectoryNotFoundException){current=Path.GetDirectoryName(current)??throw new ArgumentException("Missing local root.");continue;}
   LocalStoragePath.DirectoryPath(current);
   return;
  }
 }

 // This is deliberately read-only. It does not authenticate a workspace by
 // filename alone, inspect SQLite, or certify the selected backup media.
 public void VerifyExistingPrivateDataLayout()
 {
  if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException("Windows production storage required.");
  LocalStoragePath.DirectoryPath(DataRoot);
  if(!WorkspaceConfidentiality.HasRestrictedDirectoryAcl(DataRoot))
   throw new ArgumentException("Practice data directory permissions require review.");
  var marker=LocalStoragePath.FilePath(IdentityMarkerPath,mustExist:true,rejectSidecars:true);
  var database=LocalStoragePath.FilePath(DatabasePath,mustExist:true);
  foreach(var entry in new[]{marker,database})
   if(!WorkspaceConfidentiality.HasWorkspaceFileAcl(entry))
    throw new ArgumentException("Practice data file permissions require review.");
  foreach(var suffix in new[]{"-wal","-shm","-journal"})
  {
   var entry=database+suffix;
   try{_=File.GetAttributes(entry);}
   catch(FileNotFoundException){continue;}
   if(!WorkspaceConfidentiality.HasWorkspaceFileAcl(entry))
    throw new ArgumentException("Practice data sidecar permissions require review.");
  }
 }
}
