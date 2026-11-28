using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PracticeManager.Core;
namespace PracticeManager.Persistence;

public sealed record FolderConfiguration(int Revision,string Operation,string? PreviousHash,string Hash,string RootPath,string TemplateJson,string Reason,long Actor,string Utc,long AuditId)
{
 public IReadOnlyList<ProjectFolderTemplateItem> Template=>Array.AsReadOnly(JsonSerializer.Deserialize<ProjectFolderTemplateItem[]>(TemplateJson)??throw new InvalidOperationException("Missing template."));
}
internal sealed class FolderConfigurationStore(SqliteConnection db,Func<SqliteTransaction,bool,long> authorize,string databasePath)
{
 static string Digest(FolderConfiguration e)=>Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new{
  Format="CRC-FolderConfiguration-v1",e.Revision,e.Operation,e.PreviousHash,e.RootPath,e.TemplateJson,e.Reason,e.Actor,e.Utc,e.AuditId}))).ToLowerInvariant();
 static string TemplateJson(IReadOnlyList<ProjectFolderTemplateItem> items)
 {
  var frozen=items.ToArray();_=ProjectFolderTemplate.Preview(frozen);
  return JsonSerializer.Serialize(frozen);
 }
 SqliteCommand Q(SqliteTransaction t,string sql,params (string,object?)[] args)
 {
  var q=db.CreateCommand();q.Transaction=t;q.CommandText=sql;
  foreach(var (key,value) in args)q.Parameters.AddWithValue(key,value??DBNull.Value);return q;
 }
 internal IReadOnlyList<FolderConfiguration> ReadCore(SqliteTransaction t)
 {
  var rows=new List<FolderConfiguration>();
  using(var q=Q(t,@"SELECT f.revision,f.operation_key,f.previous_hash,f.record_hash,f.root_path,f.template_json,f.reason,f.actor_id,f.recorded_utc,f.audit_id,a.id
  FROM project_folder_configurations f LEFT JOIN audit_events a ON a.id=f.audit_id AND a.actor_id=f.actor_id AND a.action='RecordFolderConfiguration'
  AND a.entity='Firm' AND a.entity_id=1 ORDER BY f.revision"))
  using(var r=q.ExecuteReader())while(r.Read())
  {
   if(rows.Count==500||r.IsDBNull(10))throw new InvalidOperationException("Configuration audit missing.");
   var e=new FolderConfiguration(r.GetInt32(0),r.GetString(1),r.IsDBNull(2)?null:r.GetString(2),r.GetString(3),r.GetString(4),r.GetString(5),r.GetString(6),r.GetInt64(7),r.GetString(8),r.GetInt64(9));
   if(e.Revision!=rows.Count+1||e.PreviousHash!=rows.LastOrDefault()?.Hash||!Guid.TryParseExact(e.Operation,"N",out var op)||op==Guid.Empty||op.ToString("N")!=e.Operation||
     e.Actor<=0||!DateTimeOffset.TryParseExact(e.Utc,"O",CultureInfo.InvariantCulture,DateTimeStyles.None,out var utc)||utc.Offset!=TimeSpan.Zero||
     e.RootPath!=LocalStoragePath.Normalize(e.RootPath)||TemplateJson(e.Template)!=e.TemplateJson||
     string.IsNullOrWhiteSpace(e.Reason)||e.Reason.Length>1000||e.Reason!=e.Reason.Trim()||e.Reason.Any(char.IsControl)||Digest(e)!=e.Hash)
    throw new InvalidOperationException("Folder configuration history failed integrity verification.");
   if(rows.LastOrDefault() is FolderConfiguration prior&&prior.RootPath==e.RootPath&&prior.TemplateJson==e.TemplateJson)
    throw new InvalidOperationException("Unchanged configuration in history.");
   rows.Add(e);
  }
  using var census=Q(t,"SELECT count(*) FROM audit_events WHERE action='RecordFolderConfiguration'");
  if(Convert.ToInt64(census.ExecuteScalar())!=rows.Count)throw new InvalidOperationException("Unpaired configuration audit.");
  return rows.AsReadOnly();
 }
 public IReadOnlyList<FolderConfiguration> Read()
 {
  using var t=db.BeginTransaction(deferred:true);if(authorize(t,false)<=0)throw new UnauthorizedAccessException();return ReadCore(t);
 }
 public (FolderConfiguration Entry,bool Replayed) Record(Guid operation,string? expected,string root,IReadOnlyList<ProjectFolderTemplateItem> template,string reason,bool confirmed)
 {
  if(!confirmed||operation==Guid.Empty||string.IsNullOrWhiteSpace(reason)||reason.Length>1000||reason!=reason.Trim()||reason.Any(char.IsControl))
   throw new ArgumentException("Explicit configuration review required.");
  var key=operation.ToString("N");var json=TemplateJson(template);
  using var t=db.BeginTransaction(deferred:false);var actor=authorize(t,true);if(actor<=0)throw new UnauthorizedAccessException();
  var path=ProjectFolderPolicy.ReviewRoot(root,databasePath);
  if(path.Length>512)throw new ArgumentException("Root path exceeds candidate bound.");
  var rows=ReadCore(t);var prior=rows.LastOrDefault();
  if(rows.SingleOrDefault(x=>x.Operation==key) is FolderConfiguration old)
  {
   if(old.Actor!=actor||old.PreviousHash!=expected||old.RootPath!=path||old.TemplateJson!=json||old.Reason!=reason)throw new ArgumentException("Changed configuration replay.");
   return(old,true);
  }
  if(rows.Count>=500||prior?.Hash!=expected||(prior?.RootPath==path&&prior.TemplateJson==json))
   throw new ArgumentException("Current configuration predecessor and changed selection required.");
  var utc=DateTimeOffset.UtcNow.ToString("O",CultureInfo.InvariantCulture);
  using var audit=Q(t,"INSERT INTO audit_events(actor_id,action,entity,entity_id,occurred_at) VALUES($a,'RecordFolderConfiguration','Firm',1,$u) RETURNING id",("$a",actor),("$u",utc));
  var e=new FolderConfiguration(rows.Count+1,key,expected,"",path,json,reason,actor,utc,Convert.ToInt64(audit.ExecuteScalar()));
  e=e with{Hash=Digest(e)};
  using(var q=Q(t,"INSERT INTO project_folder_configurations VALUES($r,$k,$prev,$h,$path,$json,$n,$a,$u,$audit)",
   ("$r",e.Revision),("$k",key),("$prev",expected),("$h",e.Hash),("$path",path),("$json",json),("$n",reason),("$a",actor),("$u",utc),("$audit",e.AuditId)))q.ExecuteNonQuery();
  var read=ReadCore(t)[^1];if(read!=e)throw new InvalidOperationException("Configuration read-back failed.");
  t.Commit();return(read,false);
 }
}
