using System.Text.RegularExpressions;
namespace PracticeManager.Core;

// Point-in-time checks for a trusted local filesystem. Not a sandbox, ACL
// guarantee, file-handle identity proof or defense against concurrent redirection.
public static class LocalStoragePath
{
 static readonly string[] Sidecars=["-wal","-shm","-journal"];
 static readonly Regex Reserved=new(@"^(CON|PRN|AUX|NUL|COM[1-9¹²³]|LPT[1-9¹²³])$",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
 public static string Normalize(string path)
 {
  if(string.IsNullOrWhiteSpace(path)||!Path.IsPathFullyQualified(path))throw new ArgumentException("Use a fully qualified local storage path.");
  var root=Path.GetPathRoot(path)??throw new ArgumentException("Local storage root required.");
  if(OperatingSystem.IsWindows()&&(root.Length!=3||root[1]!=':'||!char.IsAsciiLetter(root[0])||new DriveInfo(root).DriveType!=DriveType.Fixed))
   throw new ArgumentException("Use a local fixed drive; network and device paths are not supported.");
  foreach(var part in path[root.Length..].Split(new[]{Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar},StringSplitOptions.RemoveEmptyEntries))
   if(part is "." or ".."||part.EndsWith(' ')||part.EndsWith('.')||part.Contains(':')||Reserved.IsMatch(part.Split('.')[0])||part.IndexOfAny(Path.GetInvalidFileNameChars())>=0)
    throw new ArgumentException("Unsafe local storage path component.");
  return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
 }
 static FileAttributes? Attributes(string path)
 {
  try{return File.GetAttributes(path);}
  catch(FileNotFoundException){return null;}
  catch(DirectoryNotFoundException){return null;}
 }
 static void Plain(FileAttributes a,bool directory)
 {
  if((a&FileAttributes.ReparsePoint)!=0||((a&FileAttributes.Directory)!=0)!=directory)
   throw new ArgumentException("Redirected or unexpected local storage entry.");
 }
 public static string DirectoryPath(string path,bool create=false)
 {
  var full=Normalize(path);var root=Path.GetPathRoot(full)!;
  var current=root;
  var attrs=Attributes(current)??throw new ArgumentException("Local storage root unavailable.");
  Plain(attrs,true);
  foreach(var part in full[root.Length..].Split(new[]{Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar},StringSplitOptions.RemoveEmptyEntries))
  {
   current=Path.Combine(current,part);var next=Attributes(current);
   if(next==null)
   {
    if(!create)throw new ArgumentException("Local storage directory missing.");
    Directory.CreateDirectory(current);
    next=Attributes(current)??throw new ArgumentException("Local storage directory unavailable.");
   }
   Plain(next.Value,true);
  }
  return full;
 }
 public static string FilePath(string path,bool mustExist,bool mustBeNew=false,bool rejectSidecars=false)
 {
  if(mustExist&&mustBeNew)throw new ArgumentException("Inconsistent file requirements.");
  var full=Normalize(path);
  DirectoryPath(Path.GetDirectoryName(full)??throw new ArgumentException("Local storage parent required."));
  var a=Attributes(full);
  if(a!=null){Plain(a.Value,false);if(mustBeNew)throw new ArgumentException("Choose a new file; existing files are never overwritten.");}
  else if(mustExist)throw new ArgumentException("Local storage file missing.");
  foreach(var suffix in Sidecars)
  {
   var side=Attributes(full+suffix);
   if(side==null)continue;
   if(rejectSidecars||a==null)throw new ArgumentException("Unexpected database sidecar; use a closed source and empty destination.");
   Plain(side.Value,false);
  }
  return full;
 }
}
