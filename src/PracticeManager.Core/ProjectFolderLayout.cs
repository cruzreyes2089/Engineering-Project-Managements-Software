namespace PracticeManager.Core;

// Flat production customization only. Restricted folders remain governed by the fixed boundary.
public sealed class ProjectFolderLayout
{
 public long RevisionId{get;}
 public string Revision=>RevisionId==0?ProjectFolderTemplate.Revision:"Business-Production-"+RevisionId.ToString(System.Globalization.CultureInfo.InvariantCulture);
 public IReadOnlyList<string> Names{get;}
 private ProjectFolderLayout(long revision,IReadOnlyList<string> names){RevisionId=revision;Names=names;}
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
 public IReadOnlyList<ProjectFolderNode> Production(bool includeCorrespondence)
 {
  if(RevisionId==0)return ProjectFolderTemplate.Production(includeCorrespondence);
  var nodes=Names.Select(name=>new ProjectFolderNode(name,Array.Empty<ProjectFolderNode>())).ToList();
  if(includeCorrespondence)nodes.Add(new("Correspondence",Array.Empty<ProjectFolderNode>()));
  return nodes.AsReadOnly();
 }
 public override string ToString()=>"[Business production layout]";
}
