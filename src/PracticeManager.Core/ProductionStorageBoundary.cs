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
  =>ReviewCore(localAppData,projectRoot,backupRoot,true);
 // Existing workspace operation may proceed with the reviewed backup offline.
 // This does not qualify the stored path for writing or claim that it is online.
 public static ProductionStorageBoundary ReviewForDailyUse(string localAppData,string projectRoot,string backupRoot)
  =>ReviewCore(localAppData,projectRoot,backupRoot,false);
 static ProductionStorageBoundary ReviewCore(string localAppData,string projectRoot,string backupRoot,bool requireBackup)
 {
  var profile=LocalStoragePath.DirectoryPath(localAppData);
  var programs=Path.Combine(profile,"Programs");
  var program=LocalStoragePath.Normalize(Path.Combine(programs,"CRC Practice Manager"));
  var data=LocalStoragePath.Normalize(Path.Combine(profile,"CRC Practice Manager DATA"));
  var project=LocalStoragePath.DirectoryPath(projectRoot);
  var backup=requireBackup?LocalStoragePath.DirectoryPath(backupRoot):LocalStoragePath.NormalizeStoredDestination(backupRoot);
  if(string.Equals(backup,Path.GetPathRoot(backup),StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Select a dedicated backup directory, not a drive root.");
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

 // Call only when an actual backup is requested. No replacement location,
 // directory creation or automatic repair is permitted.
 public string VerifyBackupDestination()
 {
  var current=Review(Path.GetDirectoryName(DataRoot)!,ProjectRoot,BackupRoot);
  if(current.DataRoot!=DataRoot||current.BackupRoot!=BackupRoot||
     !OperatingSystem.IsWindows()||!WorkspaceConfidentiality.HasRestrictedDirectoryAcl(current.BackupRoot))
   throw new ArgumentException("Configured backup location is unavailable or requires permissions review.");
  return current.BackupRoot;
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
 public void VerifyExistingPrivateDataLayout()=>VerifyDataDirectory(DataRoot);
 // Fixed data location only; does not discover or adopt alternative workspaces.
 public static string ReviewExistingDataDirectory(string localAppData)
 {
  var profile=LocalStoragePath.DirectoryPath(localAppData);
  var data=Path.Combine(profile,"CRC Practice Manager DATA");
  VerifyDataDirectory(data);
  return data;
 }
 static void VerifyDataDirectory(string dataRoot)
 {
  if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException("Windows production storage required.");
  LocalStoragePath.DirectoryPath(dataRoot);
  if(!WorkspaceConfidentiality.HasRestrictedDirectoryAcl(dataRoot))
   throw new ArgumentException("Practice data directory permissions require review.");
  var marker=LocalStoragePath.FilePath(Path.Combine(dataRoot,"production-workspace.txt"),mustExist:true,rejectSidecars:true);
  var database=LocalStoragePath.FilePath(Path.Combine(dataRoot,"practice.db"),mustExist:true);
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
