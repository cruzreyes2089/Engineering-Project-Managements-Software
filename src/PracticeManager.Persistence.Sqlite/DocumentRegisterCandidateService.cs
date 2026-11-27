using Microsoft.Data.Sqlite;
using PracticeManager.Core;
namespace PracticeManager.Persistence;

public sealed partial class LocalPractice
{
 // Internal schema-49 test integration only. No constructor/bootstrap migration,
 // public API, desktop command, real issue or actual transmission.
 private static void RequireDocumentRegisterCandidate(SqliteConnection c,SqliteTransaction t)
 {
  if(Convert.ToInt64(Scalar(c,t,"PRAGMA user_version"))!=49)throw new UnauthorizedAccessException("Document register candidate is not activated.");
 }
 private TransmittalRegisterStore CandidateTransmittalStore(SqliteConnection c)=>new(c,
  (t,project,action)=>{
   var actor=RequireDocuments(c,t,action!="Read");RequireAdministration(c,t);
   RequireDocumentRegisterCandidate(c,t);ProjectRow(c,t,project);
   return actor;
  },(t,project)=>RequireReleaseReviewEligibility(c,t,project));
 private FolderConfigurationStore CandidateFolderConfiguration(SqliteConnection c)=>new(c,
  (t,write)=>{var actor=RequireAdministration(c,t);RequireDocumentRegisterCandidate(c,t);return actor;},c.DataSource);
 internal IReadOnlyList<FolderConfiguration> ReadCandidateFolderConfiguration()
 {
  using var c=Open();return CandidateFolderConfiguration(c).Read();
 }
 internal (FolderConfiguration Entry,bool Replayed) RecordCandidateFolderConfiguration(Guid operation,string? expected,string root,
  IReadOnlyList<ProjectFolderTemplateItem> template,string reason,bool confirmed)
 {
  using var c=Open();return CandidateFolderConfiguration(c).Record(operation,expected,root,template,reason,confirmed);
 }
 internal StoredTransmittal ReadCandidateTransmittal(long project,long id)
 {
  using var c=Open();return CandidateTransmittalStore(c).Read(project,id);
 }
 internal (StoredTransmittal Record,bool Replayed) PrepareCandidateTransmittal(Guid operation,TransmittalIntent reviewed,string evidence,string note,bool confirmed)
 {
  using var c=Open();return CandidateTransmittalStore(c).Prepare(operation,reviewed,evidence,note,confirmed);
 }
 internal (StoredTransmittalEvent Entry,bool Replayed) RecordCandidateTransmittalEvent(long project,long id,Guid operation,string? expected,string state,string evidence,string note,bool confirmed)
 {
  using var c=Open();return CandidateTransmittalStore(c).Append(project,id,operation,expected,state,evidence,note,confirmed);
 }
 private ProjectFolderRegisterStore CandidateProjectFolders(SqliteConnection c)=>new(c,(t,project,write)=>{
  var actor=RequireDocuments(c,t,write);RequireAdministration(c,t);RequireDocumentRegisterCandidate(c,t);
  ProjectRow(c,t,project);return actor;
 },c.DataSource);
 internal IReadOnlyList<FolderRegisterEntry> ReadCandidateProjectFolders(long project)
 {
  using var c=Open();return CandidateProjectFolders(c).Read(project);
 }
 internal (FolderRegisterEntry Entry,bool Replayed) RecordCandidateProjectFolder(long project,Guid operation,string? expected,string configHash,string state,string path,string reason,bool confirmed)
 {
  using var c=Open();return CandidateProjectFolders(c).Record(project,operation,expected,configHash,state,path,reason,confirmed);
 }

}
