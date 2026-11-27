using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PracticeManager.Core;
namespace PracticeManager.Persistence;

internal sealed record FolderRegisterEntry(long ProjectId,int Revision,string Operation,string? PreviousHash,string Hash,string State,string PathKey,string ConfigurationHash,string RelativePath,string DisplayPath,string Reason,long Actor,string Utc,long AuditId);
// Inactive native store. A future application service must supply current permissions
// and independently reviewed root configuration within the same transaction.
internal sealed class ProjectFolderRegisterStore(SqliteConnection db,Func<SqliteTransaction,long,bool,long> authorize,
 string databasePath)
{
 static string Digest(FolderRegisterEntry e)=>Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new{
  Format="CRC-FolderRegister-v1",e.ProjectId,e.Revision,e.Operation,e.PreviousHash,e.State,e.PathKey,e.ConfigurationHash,e.RelativePath,e.DisplayPath,e.Reason,e.Actor,e.Utc,e.AuditId}))).ToLowerInvariant();
 static string Key(Guid id)=>id==Guid.Empty?throw new ArgumentException("Operation required."):id.ToString("N");
 static bool ValidPathKey(string key)=>key.Length is >=4 and <=512&&key==key.ToUpperInvariant()&&
  key.All(ch=>ch is >= 'A' and <= 'Z' or >= '0' and <= '9' or ' ' or '_' or '.' or '/' or ':' or '-')&&!key.EndsWith('/')&&!key.Contains("//",StringComparison.Ordinal);
 static string Reason(string reason)=>string.IsNullOrWhiteSpace(reason)||reason.Length>1000||reason!=reason.Trim()||reason.Any(char.IsControl)?
  throw new ArgumentException("Explicit review reason required."):reason;
 SqliteCommand Q(SqliteTransaction t,string sql,params (string,object?)[] values)
 {
  var q=db.CreateCommand();q.Transaction=t;q.CommandText=sql;foreach(var (name,value) in values)q.Parameters.AddWithValue(name,value??DBNull.Value);return q;
 }
 long Count(SqliteTransaction t,string sql,params (string,object?)[] args){using var q=Q(t,sql,args);return Convert.ToInt64(q.ExecuteScalar());}
 internal IReadOnlyList<FolderRegisterEntry> ReadCore(SqliteTransaction t,long project)
 {
  var configs=new FolderConfigurationStore(db,(_,_)=>throw new UnauthorizedAccessException(),databasePath).ReadCore(t).ToDictionary(x=>x.Hash,StringComparer.Ordinal);
  var rows=new List<FolderRegisterEntry>();
  using(var q=Q(t,@"SELECT f.revision,f.operation_key,f.previous_hash,f.record_hash,f.state,f.path_key,f.configuration_hash,f.relative_path,f.display_path,f.reason,f.actor_id,f.recorded_utc,f.audit_id,a.id
   FROM project_folder_revisions f LEFT JOIN audit_events a ON a.id=f.audit_id AND a.actor_id=f.actor_id AND a.entity='Project' AND a.entity_id=f.project_id
   AND a.action='RecordProjectFolderReference' WHERE f.project_id=$p ORDER BY f.revision",("$p",project)))
  using(var r=q.ExecuteReader())while(r.Read())
  {
   if(rows.Count==500||r.IsDBNull(13))throw new InvalidOperationException("Folder history or audit missing.");
   var e=new FolderRegisterEntry(project,r.GetInt32(0),r.GetString(1),r.IsDBNull(2)?null:r.GetString(2),r.GetString(3),r.GetString(4),r.GetString(5),r.GetString(6),r.GetString(7),r.GetString(8),r.GetString(9),r.GetInt64(10),r.GetString(11),r.GetInt64(12));
   var prior=rows.LastOrDefault();
   if(!configs.TryGetValue(e.ConfigurationHash,out var config)||ProjectFolderPolicy.ResolveReference(config.RootPath,e.RelativePath)!=e.DisplayPath||e.DisplayPath.Replace('\\','/').ToUpperInvariant()!=e.PathKey)throw new InvalidOperationException("Folder configuration binding invalid.");
   if(e.Revision!=rows.Count+1||e.PreviousHash!=prior?.Hash||!Guid.TryParseExact(e.Operation,"N",out var key)||key==Guid.Empty||key.ToString("N")!=e.Operation||
      !ValidPathKey(e.PathKey)||e.Actor<=0||!DateTimeOffset.TryParseExact(e.Utc,"O",CultureInfo.InvariantCulture,DateTimeStyles.None,out var utc)||utc.Offset!=TimeSpan.Zero||
      (e.State!="Registered"&&e.State!="Retired")||
      (e.State=="Retired"&&(prior?.State!="Registered"||prior.PathKey!=e.PathKey||prior.ConfigurationHash!=e.ConfigurationHash||prior.RelativePath!=e.RelativePath||prior.DisplayPath!=e.DisplayPath))||
      (e.State=="Registered"&&prior?.State=="Registered"&&prior.PathKey==e.PathKey)||Digest(e)!=e.Hash)
    throw new InvalidOperationException("Folder history integrity failed.");
   _=Reason(e.Reason);rows.Add(e);
  }
  if(Count(t,"SELECT count(*) FROM audit_events WHERE action='RecordProjectFolderReference' AND entity='Project' AND entity_id=$p",("$p",project))!=rows.Count)
   throw new InvalidOperationException("Unpaired folder audit.");
  return rows.AsReadOnly();
 }
 public IReadOnlyList<FolderRegisterEntry> Read(long project)
 {
  using var t=db.BeginTransaction(deferred:true);if(authorize(t,project,false)<=0)throw new UnauthorizedAccessException();return ReadCore(t,project);
 }
 public (FolderRegisterEntry Entry,bool Replayed) Record(long project,Guid operation,string? expected,string expectedConfiguration,string state,string pathOrRetirementKey,string reason,bool confirmed)
 {
  if(!confirmed||state is not ("Registered" or "Retired"))throw new ArgumentException("Explicit folder review required.");
  var key=Key(operation);reason=Reason(reason);
  using var t=db.BeginTransaction(deferred:false);var actor=authorize(t,project,true);
  if(actor<=0)throw new UnauthorizedAccessException();
  var history=ReadCore(t,project);var prior=history.LastOrDefault();
  var configs=new FolderConfigurationStore(db,(_,_)=>throw new UnauthorizedAccessException(),databasePath).ReadCore(t);
  var config=configs.SingleOrDefault(x=>x.Hash==expectedConfiguration)??throw new ArgumentException("Reviewed configuration unavailable.");
  var old=history.SingleOrDefault(x=>x.Operation==key);
  // Retirement retains the original binding; retry can use historical metadata.
  var binding=old??prior;
  var reference=state=="Registered"?ProjectFolderPolicy.Review(config.RootPath,pathOrRetirementKey,databasePath):
   binding!=null&&binding.ConfigurationHash==expectedConfiguration&&binding.PathKey==pathOrRetirementKey?
   new ProjectFolderReference(binding.RelativePath,binding.DisplayPath):throw new ArgumentException("Current matching folder required.");
  var pathKey=reference.CanonicalPath.Replace('\\','/').ToUpperInvariant();
  if(!ValidPathKey(pathKey))throw new ArgumentException("Candidate folder register supports canonical ASCII paths only.");
  if(old!=null)
  {
   if(old.Actor!=actor||old.PreviousHash!=expected||old.State!=state||old.PathKey!=pathKey||old.Reason!=reason||
     old.ConfigurationHash!=expectedConfiguration||old.RelativePath!=reference.RelativePath||old.DisplayPath!=reference.CanonicalPath)throw new ArgumentException("Changed folder operation replay.");
   return(old,true);
  }
  if(state=="Registered"&&configs.Last().Hash!=expectedConfiguration)throw new ArgumentException("Folder configuration changed; review again.");
  if(history.Count>=500||expected!=prior?.Hash||
    (state=="Retired"&&(prior?.State!="Registered"||prior.PathKey!=pathKey))||
    (state=="Registered"&&prior?.State=="Registered"&&prior.PathKey==pathKey))
   throw new ArgumentException("Current folder predecessor and changed reference required.");
  using var audit=Q(t,"INSERT INTO audit_events(actor_id,action,entity,entity_id,occurred_at) VALUES($a,'RecordProjectFolderReference','Project',$p,$u) RETURNING id",
   ("$a",actor),("$p",project),("$u",DateTimeOffset.UtcNow.ToString("O",CultureInfo.InvariantCulture)));
  var auditId=Convert.ToInt64(audit.ExecuteScalar());
  var entry=new FolderRegisterEntry(project,history.Count+1,key,expected,"",state,pathKey,expectedConfiguration,reference.RelativePath,reference.CanonicalPath,reason,actor,DateTimeOffset.UtcNow.ToString("O",CultureInfo.InvariantCulture),auditId);
  entry=entry with{Hash=Digest(entry)};
  using(var q=Q(t,@"INSERT INTO project_folder_revisions(project_id,revision,operation_key,previous_hash,record_hash,state,path_key,configuration_hash,relative_path,display_path,reason,actor_id,recorded_utc,audit_id)
   VALUES($p,$r,$k,$prev,$h,$s,$path,$config,$relative,$display,$n,$a,$u,$audit)",("$p",project),("$r",entry.Revision),("$k",key),("$prev",expected),("$h",entry.Hash),("$s",state),("$path",pathKey),("$config",expectedConfiguration),("$relative",reference.RelativePath),("$display",reference.CanonicalPath),("$n",reason),("$a",actor),("$u",entry.Utc),("$audit",auditId)))q.ExecuteNonQuery();
  var read=ReadCore(t,project)[^1];if(read!=entry)throw new InvalidOperationException("Folder history read-back failed.");
  t.Commit();return(read,false);
 }
}
