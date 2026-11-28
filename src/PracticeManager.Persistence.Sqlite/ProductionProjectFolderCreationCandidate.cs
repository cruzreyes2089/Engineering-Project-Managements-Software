using System.Text;
using PracticeManager.Core;

namespace PracticeManager.Persistence;

// Unwired candidate: exercised only with disposable synthetic workspaces.
// Future caller must bind the project number, approved root, template, actor
// and project state to the current authorized production transaction.
internal static class ProductionProjectFolderCreationCandidate
{
 const string MarkerName=".crc-project-identity";
 static bool Exists(string path)
 {
  try{_=File.GetAttributes(path);return true;}
  catch(FileNotFoundException){return false;}
  catch(DirectoryNotFoundException){return false;}
 }
 static void VerifyDirectory(string path)
 {
  _=LocalStoragePath.DirectoryPath(path);
  if(!OperatingSystem.IsWindows()||!WorkspaceConfidentiality.HasRestrictedDirectoryAcl(path))
   throw new ArgumentException("Project directory permissions require review.");
 }
 static void VerifyMarker(string directory,byte[] expected)
 {
  VerifyDirectory(directory);
  var path=LocalStoragePath.FilePath(Path.Combine(directory,MarkerName),mustExist:true,rejectSidecars:true);
  if(!WorkspaceConfidentiality.HasWorkspaceFileAcl(path))throw new ArgumentException("Project identity permissions require review.");
  using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
  if(stream.Length!=expected.Length)throw new ArgumentException("Project directory identity requires review.");
  var actual=new byte[expected.Length];stream.ReadExactly(actual);
  if(!actual.SequenceEqual(expected))throw new ArgumentException("Project directory identity requires review.");
 }
 internal static (string ProjectDirectory,int Created,int Existing) CreateFixtureFolders(
  ProductionStorageBoundary layout,string expectedWorkspaceId,string projectNumber,
  IReadOnlyList<ProjectFolderTemplateItem> selection)
 {
  var frozen=selection.ToArray();
  var allowed=ProjectFolderTemplate.Defaults.Select(x=>x.Name).ToHashSet(StringComparer.Ordinal);
  if(frozen.Any(x=>!allowed.Contains(x.Name)))throw new ArgumentException("Select only approved project folder names.");
  void VerifyWorkspace()
  {
   var current=ProductionStorageBoundary.Review(Path.GetDirectoryName(layout.DataRoot)!,layout.ProjectRoot,layout.BackupRoot);
   if(current.DatabasePath!=layout.DatabasePath||
      ProductionWorkspaceIdentityCandidate.VerifyExisting(current)!=expectedWorkspaceId)
    throw new ArgumentException("Workspace identity changed; folder setup stopped.");
  }
  VerifyWorkspace();
  var target=ProductionProjectFolderPolicy.ResolveProjectDirectory(layout,projectNumber);
  var children=ProductionProjectFolderPolicy.ResolveTemplateDirectories(layout,projectNumber,frozen);
  var expected=Encoding.ASCII.GetBytes("CRC-PROJECT-V1:"+expectedWorkspaceId+":"+projectNumber);
  if(Exists(target))VerifyMarker(target,expected);
  else
  {
   VerifyWorkspace();VerifyDirectory(layout.ProjectRoot);
   if(Exists(target))throw new ArgumentException("Project directory appeared; review before retrying.");
   WorkspaceConfidentiality.EnsureWorkspaceDirectory(target);
   VerifyDirectory(target);
   if(Directory.EnumerateFileSystemEntries(target).Any())throw new ArgumentException("New project directory is not empty; review before retrying.");
   var marker=LocalStoragePath.FilePath(Path.Combine(target,MarkerName),mustExist:false,mustBeNew:true,rejectSidecars:true);
   using(var stream=new FileStream(marker,FileMode.CreateNew,FileAccess.Write,FileShare.None))
   {stream.Write(expected);stream.Flush(true);}
   VerifyMarker(target,expected);
  }
  int created=0,existing=0;
  foreach(var child in children)
  {
   VerifyWorkspace();VerifyDirectory(layout.ProjectRoot);VerifyMarker(target,expected);
   if(Exists(child)){VerifyDirectory(child);existing++;continue;}
   WorkspaceConfidentiality.EnsureWorkspaceDirectory(child);
   VerifyDirectory(child);created++;
  }
  VerifyWorkspace();VerifyMarker(target,expected);
  // A partial failure deliberately leaves completed folders and marker intact.
  // No rollback deletes user files. A retry rechecks the complete current plan.
  return(target,created,existing);
 }
}
