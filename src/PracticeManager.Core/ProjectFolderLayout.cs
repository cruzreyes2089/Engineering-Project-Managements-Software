namespace PracticeManager.Core;

// Flat production customization only. Restricted folders remain governed by the fixed boundary.
public sealed class ProjectFolderLayout
{
 public long RevisionId{get;}
 public string Revision=>RevisionId==0?ProjectFolderTemplate.Revision:(IsNamed?"Named-Production-":"Business-Production-")+RevisionId.ToString(System.Globalization.CultureInfo.InvariantCulture);
 public bool IsNamed{get;}
 public IReadOnlyList<string> Names{get;}
 private ProjectFolderLayout(long revision,IReadOnlyList<string> names,bool named=false){RevisionId=revision;Names=names;IsNamed=named;}
 public static ProjectFolderLayout Default()=>new(0,Array.AsReadOnly(ProjectFolderTemplate.Production().Select(x=>x.Name).ToArray()));
 public static ProjectFolderLayout Custom(long revision,IReadOnlyList<string> names)
 {
  if(revision<=0)throw new ArgumentException("A saved template revision is required.");
  return new(revision,ValidateNames(names));
 }
 public static IReadOnlyList<string> ValidateNames(IReadOnlyList<string> names)
 {
  if(names==null||names.Count is <1 or >30)throw new ArgumentException("Enter between 1 and 30 production folders.");
  var copy=names.ToArray();
  var unique=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
  var reserved=ProjectFolderTemplate.Restricted().Select(x=>x.Name).Append("Correspondence").ToHashSet(StringComparer.OrdinalIgnoreCase);
  foreach(var name in copy)
  {
   ProjectStorageDrafting.ValidateComponent(name);
   if(name.Length>64||reserved.Contains(name)||!unique.Add(name))
    throw new ArgumentException("Use unique production folder names of up to 64 characters, excluding restricted labels and optional Correspondence.");
  }
  return Array.AsReadOnly(copy);
 }
 public static ProjectFolderLayout Named(long revision,IReadOnlyList<string> paths)
 {
  if(revision<=0)throw new ArgumentException("Saved named revision required.");
  return new(revision,ValidatePaths(paths),true);
 }
 public static IReadOnlyList<string> ValidatePaths(IReadOnlyList<string> paths)
 {
  if(paths==null||paths.Count is <1 or >60)throw new ArgumentException("Enter 1–60 production directories.");
  var copy=paths.ToArray();var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
  var exact=new HashSet<string>(StringComparer.Ordinal);
  var reserved=ProjectFolderTemplate.Restricted().Select(x=>x.Name).Append("Correspondence").ToHashSet(StringComparer.OrdinalIgnoreCase);
  foreach(var path in copy)
  {
   if(string.IsNullOrEmpty(path)||path.Length>160)throw new ArgumentException("Invalid relative folder path.");
   var parts=path.Split('/');
   if(parts.Length>4)throw new ArgumentException("Maximum four folder levels.");
   foreach(var part in parts)
   {
    ProjectStorageDrafting.ValidateComponent(part);
    if(part.Length>64||reserved.Contains(part))throw new ArgumentException("Restricted labels and optional Correspondence are reserved.");
   }
   if(parts.Length>1&&!exact.Contains(string.Join("/",parts[..^1])))throw new ArgumentException("List each parent folder before its children, using exact parent spelling.");
   if(!seen.Add(path))throw new ArgumentException("Duplicate folder path.");
   exact.Add(path);
  }
  return Array.AsReadOnly(copy);
 }
 public IReadOnlyList<ProjectFolderNode> Production(bool includeCorrespondence)
 {
  if(RevisionId==0)return ProjectFolderTemplate.Production(includeCorrespondence);
  IReadOnlyList<ProjectFolderNode> Children(string parent)
  {
   var prefix=parent.Length==0?"":parent+"/";
   return Names.Where(path=>path.StartsWith(prefix,StringComparison.Ordinal)&&!path[prefix.Length..].Contains('/'))
    .Select(path=>new ProjectFolderNode(path[prefix.Length..],Children(path))).ToList().AsReadOnly();
  }
  var nodes=IsNamed?Children("").ToList():Names.Select(name=>new ProjectFolderNode(name,Array.Empty<ProjectFolderNode>())).ToList();
  if(includeCorrespondence)nodes.Add(new("Correspondence",Array.Empty<ProjectFolderNode>()));
  return nodes.AsReadOnly();
 }
 public override string ToString()=>"[Business production layout]";
}
