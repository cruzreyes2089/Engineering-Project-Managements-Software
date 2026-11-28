using PracticeManager.Core;
namespace PracticeManager.Persistence;

// Explicit acceptance-only wrappers. Native stores recheck live permissions.
public sealed partial class LocalPractice
{
 void RequireDocumentAcceptance()
 {if(!documentAcceptance)throw new UnauthorizedAccessException("Synthetic document acceptance only.");}
 public IReadOnlyList<FolderConfiguration> ReviewAcceptanceFolderConfigurations()
 {RequireDocumentAcceptance();return ReadCandidateFolderConfiguration();}
 public FolderConfiguration SaveAcceptanceFolderConfiguration(Guid operation,string? expected,string root,IReadOnlyList<ProjectFolderTemplateItem> template,string reason,bool confirmed)
 {RequireDocumentAcceptance();return RecordCandidateFolderConfiguration(operation,expected,root,template,reason,confirmed).Entry;}
 public IReadOnlyList<FolderRegisterEntry> ReviewAcceptanceProjectFolders(long project)
 {RequireDocumentAcceptance();return ReadCandidateProjectFolders(project);}
 public FolderRegisterEntry SaveAcceptanceProjectFolder(long project,Guid operation,string? expected,string config,string state,string path,string reason,bool confirmed)
 {RequireDocumentAcceptance();return RecordCandidateProjectFolder(project,operation,expected,config,state,path,reason,confirmed).Entry;}
 public IReadOnlyList<StoredTransmittal> ReviewAcceptanceTransmittals(long project)
 {RequireDocumentAcceptance();using var c=Open();return CandidateTransmittalStore(c).List(project);}
 public StoredTransmittal PrepareAcceptanceTransmittal(Guid operation,TransmittalIntent intent,string evidence,string note,bool confirmed)
 {RequireDocumentAcceptance();return PrepareCandidateTransmittal(operation,intent,evidence,note,confirmed).Record;}
 public StoredTransmittalEvent SaveAcceptanceTransmittalEvent(long project,long id,Guid operation,string? expected,string state,string evidence,string note,bool confirmed)
 {RequireDocumentAcceptance();return RecordCandidateTransmittalEvent(project,id,operation,expected,state,evidence,note,confirmed).Entry;}
}
