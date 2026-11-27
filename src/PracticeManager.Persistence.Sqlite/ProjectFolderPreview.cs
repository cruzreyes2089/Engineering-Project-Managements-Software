using PracticeManager.Core;
namespace PracticeManager.Persistence;

public sealed record ProjectFolderPreview(long ProjectId,string ProjectNumber,string TemplateRevision,
 IReadOnlyList<ProjectFolderNode> Production,IReadOnlyList<ProjectFolderNode> Restricted)
{
 public bool Provisioned=>false;
 public bool FilesystemInspected=>false;
 public override string ToString()=>"[Project folder preview]";
}
public sealed partial class LocalPractice
{
 public ProjectFolderPreview ReadProjectFolderPreview(long project,bool includeCorrespondence=false)
 {
  using var c=Open();using var t=c.BeginTransaction();
  RequireDocuments(c,t,false);
  var row=ProjectRow(c,t,project);
  // An ordinary production viewer does not receive the restricted structure.
  var restricted=CanInTransaction(c,t,"Documents.Restricted")?ProjectFolderTemplate.Restricted():Array.Empty<ProjectFolderNode>();
  return new(project,row.Number,ProjectFolderTemplate.Revision,ProjectFolderTemplate.Production(includeCorrespondence),restricted);
 }
}
