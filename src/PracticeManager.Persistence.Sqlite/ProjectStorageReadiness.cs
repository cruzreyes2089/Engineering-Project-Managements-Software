using PracticeManager.Core;
namespace PracticeManager.Persistence;
public sealed partial class LocalPractice
{
 // Read-only inspection of an explicitly confirmed saved assignment. No DB audit write or
 // filesystem mutation. Permissions and exact assignment are checked again after inspection.
 public ProjectStorageObservation ObserveProjectStorage(long project,long expectedBinding,bool explicitlyConfirmed)
 {
  ProjectStorageBinding binding;ProjectFolderPlan plan;
  using(var c=Open())
  using(var t=c.BeginTransaction())
  {
   RequireStorageSettings(c,t);RequireDocuments(c,t,false);
   var row=ProjectRow(c,t,project);
   if(!explicitlyConfirmed)throw new ArgumentException("Confirm read-only inspection of the saved locations.");
   binding=ReadBinding(c,t,project)??throw new ArgumentException("Save an inactive storage assignment first.");
   if(binding.Revision!=expectedBinding)throw new ArgumentException("Storage assignment changed. Reload.");
   var selected=CurrentFolderSelection(c,t,project);
   if(!binding.CurrentSelection||!binding.CurrentPreference)throw new ArgumentException("Review the changed storage assignment first.");
   plan=ProjectFolderPlanning.Build(project,row.Number,selected.IncludeCorrespondence,true,SelectedLayout(c,t,selected));
  }
  var observation=WindowsStorageObservation.Read(binding,plan);
  using(var c=Open())
  using(var t=c.BeginTransaction())
  {
   RequireStorageSettings(c,t);RequireDocuments(c,t,false);
   if(ReadBinding(c,t,project)!=binding)throw new ArgumentException("Storage assignment changed during inspection. Review again.");
  }
  return observation;
 }
}
