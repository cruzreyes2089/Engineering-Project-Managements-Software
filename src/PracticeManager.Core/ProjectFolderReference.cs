namespace PracticeManager.Core;

// Metadata-only folder reference under an explicitly reviewed local project root.
// No enumeration, directory creation, shell launch, automatic import or export.
public sealed record ProjectFolderReference(string RelativePath,string CanonicalPath);
public static class ProjectFolderPolicy
{
 static readonly StringComparison Comparison=OperatingSystem.IsWindows()?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal;
 static bool Within(string root,string path)=>path.StartsWith(Path.TrimEndingDirectorySeparator(root)+Path.DirectorySeparatorChar,Comparison);
 public static ProjectFolderReference Review(string approvedRoot,string relativePath,string databasePath)
 {
  if(string.IsNullOrWhiteSpace(relativePath)||relativePath.Length>240||relativePath!=relativePath.Trim()||
     Path.IsPathRooted(relativePath)||relativePath.Contains(':')||relativePath.Any(char.IsControl))
   throw new ArgumentException("Use a relative project folder within the reviewed root.");
  // Reject ambiguous aliases rather than normalizing them away.
  var parts=relativePath.Split(new[]{'/','\\'},StringSplitOptions.None);
  if(parts.Any(p=>p.Length==0||p=="."||p==".."||p.EndsWith('.')||p.EndsWith(' ')))
   throw new ArgumentException("Unsafe project folder component.");
  var root=LocalStoragePath.DirectoryPath(approvedRoot);
  if(string.Equals(root,Path.GetPathRoot(root),Comparison))throw new ArgumentException("Select a dedicated project root, not a drive root.");
  var db=LocalStoragePath.Normalize(databasePath);var dbParent=Path.GetDirectoryName(db)!;
  if(string.Equals(root,dbParent,Comparison)||Within(root,db)||Within(dbParent,root))
   throw new ArgumentException("External project root must be separate from database storage.");
  var full=LocalStoragePath.DirectoryPath(Path.Combine(root,Path.Combine(parts)));
  if(!Within(root,full))throw new ArgumentException("Project folder outside reviewed root.");
  return new(string.Join("/",parts),full);
 }
 public static bool Overlaps(string first,string second)
 {
  var a=LocalStoragePath.Normalize(first);var b=LocalStoragePath.Normalize(second);
  return string.Equals(a,b,Comparison)||Within(a,b)||Within(b,a);
 }
}
