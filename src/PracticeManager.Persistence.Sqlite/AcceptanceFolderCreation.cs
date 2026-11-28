using System.Globalization;
using Microsoft.Data.Sqlite;
using PracticeManager.Core;
namespace PracticeManager.Persistence;

public sealed record AcceptanceFolderPreview(long ProjectId,string FolderHash,string ConfigurationHash,string ProjectFolder,
 IReadOnlyList<string> Existing,IReadOnlyList<string> Missing,string PlanHash);
public sealed partial class LocalPractice
{
 // No caller-chosen output root. The filesystem writer is limited to a dedicated
 // sibling of the marked synthetic database directory. Never use real project data.
 string SyntheticFolderRoot()
 {
  RequireDocumentAcceptance();
  var db=LocalStoragePath.FilePath(new SqliteConnectionStringBuilder(connectionString).DataSource,mustExist:true);
  var session=Path.GetDirectoryName(db)!;
  var id=ValidateBudgetId(Path.GetFileName(session));
  var parent=Path.GetDirectoryName(session)!;
  if(Path.GetFileName(parent)!="CRC Practice Manager DOCUMENT ACCEPTANCE"||Path.GetFileName(db)!="practice-document-acceptance.db")
   throw new UnauthorizedAccessException("Synthetic folder workspace unavailable.");
  _=RestrictedSyntheticDirectory(parent,false);_=RestrictedSyntheticDirectory(session,false);
  var marker=LocalStoragePath.FilePath(Path.Combine(session,"document-acceptance-session.txt"),mustExist:true,rejectSidecars:true);
  using(var stream=new FileStream(marker,FileMode.Open,FileAccess.Read,FileShare.Read))
  {
   if(stream.Length!=32)throw new UnauthorizedAccessException("Synthetic session marker invalid.");
   var bytes=new byte[32];stream.ReadExactly(bytes);
   if(System.Text.Encoding.ASCII.GetString(bytes)!=id)throw new UnauthorizedAccessException("Synthetic session marker invalid.");
  }
  return LocalStoragePath.Normalize(Path.Combine(parent,"synthetic-files-"+id));
 }
 static string RestrictedSyntheticDirectory(string path,bool create)
 {
  var full=create?WorkspaceConfidentiality.EnsureWorkspaceDirectory(path):LocalStoragePath.DirectoryPath(path);
  if(OperatingSystem.IsWindows()&&!WorkspaceConfidentiality.HasRestrictedDirectoryAcl(full))
   throw new ArgumentException("Synthetic directory permissions are not qualified. Existing permissions will not be altered.");
  return full;
 }
 // One operator action over the existing synthetic-only stages. Filesystem and
 // database changes are not atomic together; a retry must reload current records.
 public (string ProjectFolder,int Created,int Existing) SetupAcceptanceProjectFolders(long project,string? expectedConfiguration,string? expectedReference,IReadOnlyList<ProjectFolderTemplateItem> selected,bool confirmed)
 {
  if(!confirmed)throw new ArgumentException("Confirm selected synthetic project folders.");
  RequireDocumentAcceptance();var root=SyntheticFolderRoot();var frozen=selected.ToArray();_=ProjectFolderTemplate.Preview(frozen);
  var generation=sessionGeneration;var identity=userId;
  void CheckSession(){if(identity==null||userId!=identity||sessionGeneration!=generation)throw new UnauthorizedAccessException();}
  FolderConfiguration? config;FolderRegisterEntry? folder;
  using(var c=Open())using(var t=c.BeginTransaction(deferred:true)){
   RequireDocuments(c,t,true);RequireAdministration(c,t);RequireDocumentRegisterCandidate(c,t);ProjectRow(c,t,project);
   config=CandidateFolderConfiguration(c).ReadCore(t).LastOrDefault();folder=CandidateProjectFolders(c).ReadCore(t,project).LastOrDefault();
   if(config?.Hash!=expectedConfiguration||folder?.Hash!=expectedReference)throw new ArgumentException("Folder setup changed. Reload before creating folders.");
   if(config!=null&&config.RootPath!=root)throw new ArgumentException("Use advanced folder settings to resolve the current configuration.");
   if(folder!=null&&(folder.State!="Registered"||folder.ConfigurationHash!=config?.Hash||folder.RelativePath!="Synthetic Project "+project.ToString(CultureInfo.InvariantCulture)))throw new ArgumentException("Use advanced folder settings to resolve the existing reference.");
   var allowed=(config?.Template??ProjectFolderTemplate.Defaults).Select(x=>x.Name).ToHashSet(StringComparer.Ordinal);
   if(frozen.Any(x=>!allowed.Contains(x.Name)))throw new ArgumentException("Choose labels from the current folder template.");
  }
  CheckSession();
  if(config==null){
   var path=PrepareAcceptanceFolderRoot(true);CheckSession();
   config=SaveAcceptanceFolderConfiguration(Guid.NewGuid(),null,path,ProjectFolderTemplate.Defaults,"Confirmed standard synthetic project folder template",true);
  }
  CheckSession();
  if(folder==null){
   var relative=PrepareAcceptanceProjectDirectory(project,config.Hash,true);CheckSession();
   folder=SaveAcceptanceProjectFolder(project,Guid.NewGuid(),null,config.Hash,"Registered",relative,"Confirmed synthetic project folder setup",true);
  }
  CheckSession();var preview=PreviewAcceptanceFolderCreation(project,folder.Hash,config.Hash,frozen);
  CheckSession();var created=preview.Missing.Count==0?0:CreateAcceptanceFolders(project,folder.Hash,config.Hash,frozen,preview.PlanHash,true).Count;
  return(preview.ProjectFolder,created,preview.Existing.Count);
 }
 public string PrepareAcceptanceFolderRoot(bool confirmed)
 {
  if(!confirmed)throw new ArgumentException("Explicit synthetic folder setup confirmation required.");
  var root=SyntheticFolderRoot();
  using var c=Open();using var t=c.BeginTransaction(deferred:false);
  var actor=RequireAdministration(c,t);RequireDocumentRegisterCandidate(c,t);
  var result=RestrictedSyntheticDirectory(root,true);
  Audit(c,t,actor,"AcceptanceFolderRootPrepared","Firm",1);t.Commit();return result;
 }
 public string PrepareAcceptanceProjectDirectory(long project,string expectedConfiguration,bool confirmed)
 {
  if(!confirmed)throw new ArgumentException("Explicit synthetic project directory confirmation required.");
  var root=RestrictedSyntheticDirectory(SyntheticFolderRoot(),false);
  using var c=Open();using var t=c.BeginTransaction(deferred:false);
  var actor=RequireDocuments(c,t,true);RequireAdministration(c,t);RequireDocumentRegisterCandidate(c,t);ProjectRow(c,t,project);
  var config=CandidateFolderConfiguration(c).ReadCore(t).LastOrDefault()??throw new ArgumentException("Save a reviewed folder configuration first.");
  if(config.Hash!=expectedConfiguration||config.RootPath!=root)throw new ArgumentException("Use the current dedicated synthetic root configuration.");
  var relative="Synthetic Project "+project.ToString(CultureInfo.InvariantCulture);
  var target=ProjectFolderPolicy.ResolveReference(root,relative);
  var current=CandidateProjectFolders(c).ReadCore(t,project).LastOrDefault();
  if(current?.State=="Registered"&&current.DisplayPath!=target)throw new ArgumentException("Retire or review the existing project reference first.");
  using(var q=Command(c,t,@"SELECT display_path FROM current_project_folder_references WHERE project_id!=$p",("$p",project)))
  using(var r=q.ExecuteReader())while(r.Read())if(ProjectFolderPolicy.Overlaps(target,r.GetString(0)))throw new ArgumentException("Synthetic project directory overlaps another project.");
  _=RestrictedSyntheticDirectory(target,true);
  Audit(c,t,actor,"AcceptanceProjectDirectoryPrepared","Project",project);t.Commit();return relative;
 }
 (FolderConfiguration Config,FolderRegisterEntry Folder,FolderCreationPlan Plan,long Actor) ReviewCreation(
  SqliteConnection c,SqliteTransaction t,long project,string expectedFolder,string expectedConfiguration,IReadOnlyList<ProjectFolderTemplateItem> selected,bool write)
 {
  var actor=RequireDocuments(c,t,write);RequireAdministration(c,t);RequireDocumentRegisterCandidate(c,t);ProjectRow(c,t,project);
  var config=CandidateFolderConfiguration(c).ReadCore(t).LastOrDefault()??throw new ArgumentException("Save a reviewed folder configuration first.");
  var folder=CandidateProjectFolders(c).ReadCore(t,project).LastOrDefault()??throw new ArgumentException("Register a synthetic project folder first.");
  var root=RestrictedSyntheticDirectory(SyntheticFolderRoot(),false);
  if(config.Hash!=expectedConfiguration||config.RootPath!=root||folder.Hash!=expectedFolder||
     folder.State!="Registered"||folder.ConfigurationHash!=config.Hash)
   throw new ArgumentException("Current registered project folder and dedicated synthetic configuration required.");
  // Only the deterministic, application-prepared project parent is writable.
  if(folder.RelativePath!="Synthetic Project "+project.ToString(CultureInfo.InvariantCulture))
   throw new ArgumentException("Use the application-prepared synthetic project directory.");
  _=RestrictedSyntheticDirectory(folder.DisplayPath,false);
  var allowed=config.Template.Select(x=>x.Name).ToHashSet(StringComparer.Ordinal);
  if(selected.Any(x=>!allowed.Contains(x.Name)))throw new ArgumentException("Use labels from the saved template.");
  var plan=FolderCreationRehearsal.Preview(config,folder.RelativePath,c.DataSource,selected);
  if(plan.ProjectFolder!=folder.DisplayPath)throw new InvalidOperationException("Folder binding changed.");
  return(config,folder,plan,actor);
 }
 public AcceptanceFolderPreview PreviewAcceptanceFolderCreation(long project,string expectedFolder,string expectedConfiguration,IReadOnlyList<ProjectFolderTemplateItem> selected)
 {
  RequireDocumentAcceptance();using var c=Open();using var t=c.BeginTransaction(deferred:true);
  var review=ReviewCreation(c,t,project,expectedFolder,expectedConfiguration,selected.ToArray(),false);
  return new(project,review.Folder.Hash,review.Config.Hash,review.Plan.ProjectFolder,
   Array.AsReadOnly(review.Plan.Items.Where(x=>x.AlreadyExists).Select(x=>x.Name).ToArray()),
   Array.AsReadOnly(review.Plan.Items.Where(x=>!x.AlreadyExists).Select(x=>x.Name).ToArray()),review.Plan.Hash);
 }
 public IReadOnlyList<string> CreateAcceptanceFolders(long project,string expectedFolder,string expectedConfiguration,IReadOnlyList<ProjectFolderTemplateItem> selected,string planHash,bool confirmed)
 {
  if(!confirmed)throw new ArgumentException("Explicit directory creation confirmation required.");
  RequireDocumentAcceptance();var frozen=selected.ToArray();var generation=sessionGeneration;using var c=Open();
  long actor;
  using(var request=c.BeginTransaction(deferred:false))
  {
   var review=ReviewCreation(c,request,project,expectedFolder,expectedConfiguration,frozen,true);
   if(review.Plan.Hash!=planHash)throw new ArgumentException("Directory creation plan changed; review again.");
   actor=review.Actor;Audit(c,request,actor,"AcceptanceFolderCreationRequested","Project",project);request.Commit();
  }
  using var t=c.BeginTransaction(deferred:false);
  var current=ReviewCreation(c,t,project,expectedFolder,expectedConfiguration,frozen,true);
  if(generation!=sessionGeneration||current.Actor!=actor||current.Plan.Hash!=planHash)throw new ArgumentException("Creation plan or authorization changed; review again.");
  var created=FolderCreationRehearsal.Execute(current.Config,current.Folder.RelativePath,c.DataSource,frozen,planHash,true);
  if(generation!=sessionGeneration||RequireDocuments(c,t,true)!=actor||RequireAdministration(c,t)!=actor)throw new UnauthorizedAccessException("Creation outcome requires reconciliation.");
  Audit(c,t,actor,"AcceptanceFolderCreationCompleted","Project",project);t.Commit();
  return created;
 }
}
