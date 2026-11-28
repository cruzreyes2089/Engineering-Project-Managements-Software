using PracticeManager.Core;
namespace PracticeManager.Persistence;

public sealed partial class LocalPractice
{
 // No desktop route. The fixture opener fixes the reviewed root and workspace
 // identity; no path or project number is supplied by the operator.
 internal (string ProjectDirectory,int Created,int Existing) SetupProductionFixtureFolders(long project,IReadOnlyList<ProjectFolderTemplateItem> selection)
 {
  var layout=productionFixtureLayout??throw new UnauthorizedAccessException("Production fixture workspace required.");
  var identity=productionFixtureIdentity!;
  var frozen=selection.ToArray();
  _=ProjectFolderTemplate.Preview(frozen);
  var allowed=ProjectFolderTemplate.Defaults.Select(x=>x.Name).ToHashSet(StringComparer.Ordinal);
  if(frozen.Any(x=>!allowed.Contains(x.Name)))throw new ArgumentException("Select approved project folders.");
  var generation=sessionGeneration;
  string number;long actor;
  using(var c=Open())using(var t=c.BeginTransaction(deferred:false))
  {
   actor=Require(c,t,"Projects.Setup");RequireProjectView(c,t);
   var p=ProjectRow(c,t,project);ActiveClient(c,t,p.Client);
   if(p.State is not ("Pending" or "Active")||OperationalState(c,t,project)=="OperationalClosed")
    throw new ArgumentException("Current open project required.");
   number=p.Number;
   _=ProductionProjectFolderPolicy.ResolveTemplateDirectories(layout,number,frozen);
   Audit(c,t,actor,"ProductionFixtureFolderSetupRequested","Project",project);t.Commit();
  }
  using var connection=Open();using var transaction=connection.BeginTransaction(deferred:false);
  if(sessionGeneration!=generation||Require(connection,transaction,"Projects.Setup")!=actor)
   throw new UnauthorizedAccessException("Project setup authorization changed.");
  RequireProjectView(connection,transaction);
  var current=ProjectRow(connection,transaction,project);ActiveClient(connection,transaction,current.Client);
  if(current.Number!=number||current.State is not ("Pending" or "Active")||OperationalState(connection,transaction,project)=="OperationalClosed")
   throw new ArgumentException("Project changed before folder setup.");
  var result=ProductionProjectFolderCreationCandidate.CreateFixtureFolders(layout,identity,number,frozen);
  if(sessionGeneration!=generation||Require(connection,transaction,"Projects.Setup")!=actor)
   throw new UnauthorizedAccessException("Folder outcome requires reconciliation.");
  Audit(connection,transaction,actor,"ProductionFixtureFolderSetupCompleted","Project",project);
  transaction.Commit();
  return result;
 }
}
