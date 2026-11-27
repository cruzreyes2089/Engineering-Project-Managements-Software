using System.Security.Cryptography;
using System.Text.Json;

namespace PracticeManager.Core;

// A logical plan only: names are relative to separately approved storage roots.
// No disk paths, directory creation, filesystem inspection or authorization claims.
public sealed record ProjectFolderPlanEntry(string Boundary,string RelativeDirectory);
public sealed record ProjectFolderPlan(long ProjectId,string ProjectNumber,string TemplateRevision,
 bool IncludeCorrespondence,IReadOnlyList<ProjectFolderPlanEntry> Entries,string Fingerprint)
{
 public bool Provisioned=>false;
 public bool StorageApproved=>false;
 public override string ToString()=>"[Logical project folder plan]";
}
public static class ProjectFolderPlanning
{
 public static ProjectFolderPlan Build(long projectId,string projectNumber,bool includeCorrespondence,bool includeRestricted,ProjectFolderLayout? layout=null)
 {
  layout??=ProjectFolderLayout.Default();
  if(projectId<=0||string.IsNullOrEmpty(projectNumber)||projectNumber.Length>64||
      !char.IsAsciiLetterOrDigit(projectNumber[0])||!char.IsAsciiLetterOrDigit(projectNumber[^1])||
      projectNumber.Any(c=>!char.IsAsciiLetterOrDigit(c)&&c!='-')||
      !projectNumber.Contains('-'))
   throw new ArgumentException("Native project number is not a supported folder component.");
  var entries=new List<ProjectFolderPlanEntry>();
  void Collect(string boundary,IReadOnlyList<ProjectFolderNode> nodes)
  {
   entries.Add(new(boundary,projectNumber));
   void Walk(ProjectFolderNode node,string parent)
   {
    // Even fixed template labels must satisfy the relative Windows component contract.
    var name=node.Name;
    if(string.IsNullOrWhiteSpace(name)||name!=name.Trim()||name.Length>100||
       name is "." or ".."||name.EndsWith('.')||name.Any(c=>char.IsControl(c)||c is '/' or '\\' or ':' or '<' or '>' or '"' or '|' or '?' or '*'))
     throw new InvalidOperationException("Invalid fixed folder template component.");
    var path=parent+"/"+name;
    entries.Add(new(boundary,path));
    foreach(var child in node.Children)Walk(child,path);
   }
   foreach(var node in nodes)Walk(node,projectNumber);
  }
  Collect("Production",layout.Production(includeCorrespondence));
  if(includeRestricted)Collect("Restricted",ProjectFolderTemplate.Restricted());
  if(entries.GroupBy(x=>x.Boundary+"/"+x.RelativeDirectory,StringComparer.OrdinalIgnoreCase).Any(g=>g.Count()!=1))
   throw new InvalidOperationException("Duplicate logical folder path.");
  var bytes=JsonSerializer.SerializeToUtf8Bytes(new{projectId,projectNumber,revision=layout.Revision,
   includeCorrespondence,includeRestricted,entries});
  return new(projectId,projectNumber,layout.Revision,includeCorrespondence,
   entries.AsReadOnly(),Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
 }
}
