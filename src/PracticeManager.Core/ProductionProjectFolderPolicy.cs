using System.Globalization;
namespace PracticeManager.Core;

// Production path policy, not an authorization or filesystem writer.
// The future project setup operation must supply the immutable project number
// from its authorized current database transaction, not a user-entered path.
public static class ProductionProjectFolderPolicy
{
 public static string ResolveProjectDirectory(ProductionStorageBoundary layout,string projectNumber)
 {
  if(string.IsNullOrEmpty(projectNumber)||!projectNumber.StartsWith("PRJ-",StringComparison.Ordinal)||
    !long.TryParse(projectNumber.AsSpan(4),NumberStyles.None,CultureInfo.InvariantCulture,out var n)||
    n<=0||n==long.MaxValue||projectNumber!="PRJ-"+n.ToString("D6",CultureInfo.InvariantCulture))
   throw new ArgumentException("Use the current assigned project number.");
  var root=ProjectFolderPolicy.ReviewRoot(layout.ProjectRoot,layout.DatabasePath);
  if(!OperatingSystem.IsWindows()||!WorkspaceConfidentiality.HasRestrictedDirectoryAcl(root))
   throw new ArgumentException("Project root permissions require review.");
  var target=ProjectFolderPolicy.ResolveReference(root,projectNumber);
  return target;
 }
 public static IReadOnlyList<string> ResolveTemplateDirectories(ProductionStorageBoundary layout,string projectNumber,IReadOnlyList<ProjectFolderTemplateItem> template)
 {
  var target=ResolveProjectDirectory(layout,projectNumber);
  var names=ProjectFolderTemplate.Preview(template);
  // Review all existing directories before any future writer is allowed to
  // create missing entries. A file/reparse collision is never adopted.
  InspectDirectoryIfPresent(target);
  var children=names.Select(name=>ProjectFolderPolicy.ResolveReference(target,name)).ToArray();
  foreach(var child in children)InspectDirectoryIfPresent(child);
  return Array.AsReadOnly(children);
 }
 static void InspectDirectoryIfPresent(string path)
 {
  try{_=File.GetAttributes(path);}
  catch(FileNotFoundException){return;}
  catch(DirectoryNotFoundException){return;}
  _=LocalStoragePath.DirectoryPath(path);
  if(!WorkspaceConfidentiality.HasRestrictedDirectoryAcl(path))
   throw new ArgumentException("Existing project folder permissions require review; no permissions changed.");
 }
}
