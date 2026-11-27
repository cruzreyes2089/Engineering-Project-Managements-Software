using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PracticeManager.Core;
namespace PracticeManager.Persistence;

internal sealed record DocumentRegisterWitness(int Configurations,int FolderRevisions,int Transmittals,int Documents,int Events,string Digest);
// Internal read-only history verification. Does not access external directories
// or infer authorization from historical actors. Caller owns the read transaction.
internal static class DocumentRegisterVerification
{
 static long Count(SqliteConnection db,SqliteTransaction t,string sql)
 {using var q=db.CreateCommand();q.Transaction=t;q.CommandText=sql;return Convert.ToInt64(q.ExecuteScalar());}
 internal static DocumentRegisterWitness Verify(SqliteConnection db,SqliteTransaction t)
 {
  if(Count(db,t,"PRAGMA user_version")!=49)throw new ArgumentException("Candidate document schema required.");
  var config=new FolderConfigurationStore(db,(_,_)=>throw new UnauthorizedAccessException(),db.DataSource).ReadCore(t);
  var folder=new ProjectFolderRegisterStore(db,(_,_,_)=>throw new UnauthorizedAccessException(),_=>throw new InvalidOperationException(),db.DataSource);
  var transmittals=new TransmittalRegisterStore(db,(_,_,_)=>throw new UnauthorizedAccessException(),(_,_)=>throw new InvalidOperationException());
  if(Count(db,t,"SELECT count(*) FROM project_folder_revisions")>10000||
     Count(db,t,"SELECT count(*) FROM project_transmittal_intents")>500||
     Count(db,t,"SELECT count(*) FROM project_transmittal_documents")>25000||
     Count(db,t,"SELECT count(*) FROM project_transmittal_events")>2000)
   throw new ArgumentException("Document register witness exceeds review limits.");
  var projects=new List<long>();
  using(var q=db.CreateCommand())
  {
   q.Transaction=t;q.CommandText="SELECT project_id FROM project_folder_revisions UNION SELECT entity_id FROM audit_events WHERE action='RecordProjectFolderReference' AND entity='Project' ORDER BY 1";
   using var r=q.ExecuteReader();while(r.Read())
   {
    if(projects.Count>=500||r.IsDBNull(0))throw new InvalidOperationException("Invalid folder project census.");
    projects.Add(r.GetInt64(0));
   }
  }
  var folderHashes=new List<object>();var current=new List<string>();int revisions=0;
  foreach(var p in projects)
  {
   var rows=folder.ReadCore(t,p);
   if(rows.Count==0)throw new InvalidOperationException("Missing folder history.");
   revisions+=rows.Count;folderHashes.Add(new{ProjectId=p,Hashes=rows.Select(x=>x.Hash).ToArray()});
   if(rows.Last().State=="Registered")
   {
    var key=rows.Last().PathKey;
    if(current.Any(x=>x==key||x.StartsWith(key+"/",StringComparison.Ordinal)||key.StartsWith(x+"/",StringComparison.Ordinal)))
     throw new InvalidOperationException("Overlapping current folder references.");
    current.Add(key);
   }
  }
  var ids=new List<(long Id,long Project)>();
  using(var q=db.CreateCommand())
  {
   q.Transaction=t;q.CommandText="SELECT id,project_id FROM project_transmittal_intents ORDER BY id";
   using var r=q.ExecuteReader();while(r.Read())ids.Add((r.GetInt64(0),r.GetInt64(1)));
  }
  var transmittalHashes=new List<object>();int documents=0,events=0;
  long totalBytes=0;
  foreach(var id in ids)
  {
   var row=transmittals.ReadCore(t,id.Project,id.Id);
   documents+=row.Intent.Documents.Count;events+=row.History.Count;
   totalBytes+=row.Intent.Documents.Sum(x=>x.Bytes);
   if(totalBytes>1024L*1024*1024)throw new ArgumentException("Document witness exceeds total byte limit.");
   transmittalHashes.Add(new{row.Id,row.Operation,row.Intent.ReviewHash,Events=row.History.Select(x=>x.MetadataHash).ToArray()});
  }
  if(revisions!=Count(db,t,"SELECT count(*) FROM project_folder_revisions")||
     documents!=Count(db,t,"SELECT count(*) FROM project_transmittal_documents")||
     events!=Count(db,t,"SELECT count(*) FROM project_transmittal_events")||
     revisions!=Count(db,t,"SELECT count(*) FROM audit_events WHERE action='RecordProjectFolderReference'")||
     events!=Count(db,t,"SELECT count(*) FROM audit_events WHERE action='RecordTransmittalEvent'"))
   throw new InvalidOperationException("Document register record/audit census mismatch.");
  // Every linked record verifies its own audit; additionally disallow reused
  // audit identities across the three stores and orphan project relationships.
  if(Count(db,t,"SELECT EXISTS(SELECT audit_id FROM (SELECT audit_id FROM project_folder_configurations UNION ALL SELECT audit_id FROM project_folder_revisions UNION ALL SELECT audit_id FROM project_transmittal_events) GROUP BY audit_id HAVING count(*)>1)")!=0)
   throw new InvalidOperationException("Duplicate document audit identity.");
  using(var q=db.CreateCommand())
  {q.Transaction=t;q.CommandText="PRAGMA foreign_key_check";using var r=q.ExecuteReader();if(r.Read())throw new InvalidOperationException("Database foreign-key check failed.");}
  var hash=Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new{
   Format="CRC-DocumentRegisterWitness-v1",Configurations=config.Select(x=>x.Hash).ToArray(),Folders=folderHashes,Transmittals=transmittalHashes}))).ToLowerInvariant();
  return new(config.Count,revisions,ids.Count,documents,events,hash);
 }
}
