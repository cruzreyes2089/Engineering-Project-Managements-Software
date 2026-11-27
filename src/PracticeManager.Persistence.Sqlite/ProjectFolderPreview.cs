using PracticeManager.Core;
namespace PracticeManager.Persistence;

public sealed record ProjectFolderPreview(long ProjectId,string ProjectNumber,string TemplateRevision,
 IReadOnlyList<ProjectFolderNode> Production,IReadOnlyList<ProjectFolderNode> Restricted,ProjectFolderPlan Plan)
{
 public bool Provisioned=>false;
 public bool FilesystemInspected=>false;
 public override string ToString()=>"[Project folder preview]";
}
public sealed partial class LocalPractice
{
 public ProjectStorageDraft ReviewProjectStorageDraft(long project,string productionRoot,string restrictedRoot,bool includeCorrespondence=false)
 {
  using var c=Open();using var t=c.BeginTransaction();
  RequireAdministration(c,t);RequireDocuments(c,t,false);Require(c,t,"Documents.Restricted");
  var row=ProjectRow(c,t,project);
  return ProjectStorageDrafting.Build(productionRoot,restrictedRoot,project,row.Number,includeCorrespondence);
 }
 public ProjectFolderPreview ReadProjectFolderPreview(long project,bool includeCorrespondence=false)
 {
  using var c=Open();using var t=c.BeginTransaction();
  RequireDocuments(c,t,false);
  var row=ProjectRow(c,t,project);
  // An ordinary production viewer does not receive the restricted structure.
  var allowed=CanInTransaction(c,t,"Documents.Restricted");
  var restricted=allowed?ProjectFolderTemplate.Restricted():Array.Empty<ProjectFolderNode>();
  var plan=ProjectFolderPlanning.Build(project,row.Number,includeCorrespondence,allowed);
  return new(project,row.Number,ProjectFolderTemplate.Revision,ProjectFolderTemplate.Production(includeCorrespondence),restricted,plan);
 }
}
