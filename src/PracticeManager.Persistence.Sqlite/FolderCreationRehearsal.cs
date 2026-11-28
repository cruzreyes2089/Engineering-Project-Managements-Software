using System.Security.Cryptography;
using System.Text.Json;
using PracticeManager.Core;
namespace PracticeManager.Persistence;

internal sealed record FolderCreationItem(string Name,bool AlreadyExists);
internal sealed record FolderCreationPlan(string ConfigurationHash,string ProjectFolder,IReadOnlyList<FolderCreationItem> Items,string Hash);
internal static class FolderCreationRehearsal
{
 // Internal synthetic-only exercise. No public application filesystem writer.
 static FileAttributes? Attributes(string path)
 {
  try{return File.GetAttributes(path);}catch(FileNotFoundException){return null;}catch(DirectoryNotFoundException){return null;}
 }
 static void RequireProtectedDirectory(string path)
 {
  _=LocalStoragePath.DirectoryPath(path);
  if(OperatingSystem.IsWindows()&&!WorkspaceConfidentiality.HasRestrictedDirectoryAcl(path))throw new ArgumentException("Existing folder permissions require review. No existing permissions will be changed.");
 }
 internal static FolderCreationPlan Preview(FolderConfiguration config,string relativeProject,string databasePath,IReadOnlyList<ProjectFolderTemplateItem> selected)
 {
  var project=ProjectFolderPolicy.Review(config.RootPath,relativeProject,databasePath).CanonicalPath;
  RequireProtectedDirectory(config.RootPath);RequireProtectedDirectory(project);
  var names=ProjectFolderTemplate.Preview(selected);
  var items=new List<FolderCreationItem>();
  foreach(var name in names)
  {
   var path=LocalStoragePath.Normalize(Path.Combine(project,name));var attr=Attributes(path);
   if(attr.HasValue)RequireProtectedDirectory(path);
   items.Add(new(name,attr.HasValue));
  }
  var digest=Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new{
   Format="CRC-FolderCreationPlan-v1",ConfigurationHash=config.Hash,ProjectFolder=project,Items=items}))).ToLowerInvariant();
  return new(config.Hash,project,items.AsReadOnly(),digest);
 }
 internal static IReadOnlyList<string> Execute(FolderConfiguration config,string relativeProject,string databasePath,IReadOnlyList<ProjectFolderTemplateItem> selected,string reviewedHash,bool confirmed)
 {
  if(!confirmed)throw new ArgumentException("Explicit directory creation review required.");
  var plan=Preview(config,relativeProject,databasePath,selected);
  if(plan.Hash!=reviewedHash)throw new ArgumentException("Directory creation plan changed; review again.");
  var created=new List<string>();
  foreach(var item in plan.Items)
  {
   RequireProtectedDirectory(config.RootPath);RequireProtectedDirectory(plan.ProjectFolder);
   var target=Path.Combine(plan.ProjectFolder,item.Name);
   if(item.AlreadyExists){RequireProtectedDirectory(target);continue;}
   if(Attributes(target).HasValue)throw new InvalidOperationException("Folder appeared during creation. Refresh and reconcile; earlier folders may remain.");
   // Existing project folder is never modified. New children use the existing
   // restricted creation helper, not inherited broad access. No rollback deletion.
   WorkspaceConfidentiality.EnsureWorkspaceDirectory(target);
   RequireProtectedDirectory(target);
   created.Add(item.Name);
  }
  return created.AsReadOnly();
 }
}
