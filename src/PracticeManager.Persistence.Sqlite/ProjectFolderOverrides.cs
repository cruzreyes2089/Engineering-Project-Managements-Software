using System.Text.Json;
using PracticeManager.Core;
namespace PracticeManager.Persistence;

public sealed record ProjectFolderCustomization(long SelectionId,long BaseTemplateRevision,bool IncludeCorrespondence,IReadOnlyList<string> Paths)
{
 public override string ToString()=>"[Project folder customization draft]";
}
public sealed partial class LocalPractice
{
 public ProjectFolderCustomization ReadProjectFolderCustomization(long project)
 {
  using var c=Open();using var t=c.BeginTransaction();RequireStorageSettings(c,t);RequireDocuments(c,t,false);
  _=ProjectRow(c,t,project);var selected=CurrentFolderSelection(c,t,project);
  if(!selected.Assigned)throw new ArgumentException("Select a project template before customizing its folders.");
  var layout=SelectedLayout(c,t,selected);
  return new(selected.Id,selected.TemplateRevision,selected.IncludeCorrespondence,layout.Names);
 }
 public ProjectFolderSelection SaveProjectFolderCustomization(long project,long expectedSelection,IReadOnlyList<string> paths,
  bool correspondence,bool confirmed)
 {
  using var c=Open();using var t=c.BeginTransaction(deferred:false);var actor=RequireStorageSettings(c,t);RequireDocuments(c,t,false);
  var row=ProjectRow(c,t,project);
  if(!confirmed)throw new ArgumentException("Confirm the project-specific folder plan.");
  var selected=CurrentFolderSelection(c,t,project);
  if(!selected.Assigned||selected.Id!=expectedSelection)throw new ArgumentException("Project selection changed or is missing. Reload.");
  var valid=ProjectFolderLayout.ValidatePaths(paths);
  var current=SelectedLayout(c,t,selected);
  if(current.Names.SequenceEqual(valid)&&selected.IncludeCorrespondence==correspondence)return selected;
  // Validate the full logical plan, retaining the originally selected template as provenance.
  _=ProjectFolderPlanning.Build(project,row.Number,correspondence,true,ProjectFolderLayout.ProjectOverride(selected.Id,valid));
  Exec(c,t,"INSERT INTO project_folder_selections(project_id,template_id,correspondence,recorded_by,recorded_utc,override_paths) VALUES($p,$i,$c,$a,$u,$n)",
   ("$p",project),("$i",selected.TemplateRevision==0?(object?)null:selected.TemplateRevision),("$c",correspondence?1:0),("$a",actor),("$u",UtcNow()),("$n",JsonSerializer.Serialize(valid)));
  var id=Last(c,t);Audit(c,t,actor,"CustomizeProjectFolders","Project",project);t.Commit();
  return new ProjectFolderSelection(id,selected.TemplateRevision,correspondence){Customized=true};
 }
}
