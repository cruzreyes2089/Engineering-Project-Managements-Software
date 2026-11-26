using Microsoft.Data.Sqlite;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
namespace PracticeManager.Persistence;

public sealed record IsolatedRestoreResult(string Path,string Sha256,long Bytes,long Clients,long Projects,long Documents);

// Restore only a previously generated recovery copy to a separate new file.
// No changes to startup settings, active database, credentials or holds.
public sealed partial class LocalPractice
{
 public IsolatedRestoreResult RestoreRecoveryCopyToNewFile(string source,string expectedSha256,string destination)
 {
  using var active=Open();long actor;var generation=sessionGeneration;
  using(var t=active.BeginTransaction()){actor=RequireAdministration(active,t);Require(active,t,"Administration.RecoveryCopy");}
  if(!Regex.IsMatch(expectedSha256??"",@"\A[0-9A-Fa-f]{64}\z"))throw new ArgumentException("Enter the original recorded SHA-256.");
  var input=Path.GetFullPath(source);var target=Path.GetFullPath(destination);
  var live=Path.GetFullPath(new SqliteConnectionStringBuilder(connectionString).DataSource);
  bool Same(string? a,string? b)=>string.Equals(a,b,StringComparison.OrdinalIgnoreCase);
  if(!Same(Path.GetExtension(input),".db")||!Same(Path.GetExtension(target),".db")||Same(input,target)||Same(input,live)||Same(target,live))throw new ArgumentException("Choose separate recovery and new destination database files.");
  var folder=Path.GetDirectoryName(target)??throw new ArgumentException("Destination folder required.");
  if(!Directory.Exists(folder)||File.Exists(target)||Directory.Exists(target))throw new ArgumentException("Choose a new file; no existing file will be replaced.");
  foreach(var suffix in new[]{"-wal","-shm","-journal"})if(File.Exists(input+suffix)||File.Exists(target+suffix))throw new ArgumentException("Database sidecar files present; use a closed recovery copy and empty destination.");
  var staging=Path.Combine(folder,".crc-restore-"+Guid.NewGuid().ToString("N")+".partial");
  bool owned=false;
  try{
   long bytes;string hash;
   using(var src=new FileStream(input,FileMode.Open,FileAccess.Read,FileShare.Read)){
    bytes=src.Length;if(bytes<512||bytes>4L*1024*1024*1024)throw new ArgumentException("Recovery copy outside supported size (512 bytes–4 GiB).");
    using var dst=new FileStream(staging,FileMode.CreateNew,FileAccess.ReadWrite,FileShare.None);owned=true;
    using var h=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);var buffer=new byte[65536];long count=0;
    while(true){var n=src.Read(buffer,0,buffer.Length);if(n==0)break;count+=n;if(count>bytes)throw new InvalidOperationException("Recovery file changed.");h.AppendData(buffer,0,n);dst.Write(buffer,0,n);}
    if(count!=bytes||src.Length!=bytes)throw new InvalidOperationException("Recovery file changed.");
    hash=Convert.ToHexString(h.GetHashAndReset()).ToLowerInvariant();
    if(!Same(hash,expectedSha256))throw new ArgumentException("Recovery hash mismatch. No restored copy created.");
    dst.Flush(true);
   }
   long clients,projects,documents;
   using(var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=staging,Mode=SqliteOpenMode.ReadOnly,ForeignKeys=true,Pooling=false,DefaultTimeout=10}.ToString())){
    db.Open();Exec(db,null,"PRAGMA trusted_schema=OFF; PRAGMA query_only=ON");
    if(Convert.ToInt64(Scalar(db,null,"PRAGMA user_version"))!=26)throw new ArgumentException("Only current schema 26 recovery copies are supported; older copies require a reviewed migration.");
    using(var q=Command(db,null,"PRAGMA integrity_check"))using(var r=q.ExecuteReader()){if(!r.Read()||r.GetString(0)!="ok"||r.Read())throw new InvalidOperationException("Recovery database integrity failed.");}
    using(var q=Command(db,null,"PRAGMA foreign_key_check"))using(var r=q.ExecuteReader()){if(r.Read())throw new InvalidOperationException("Recovery database foreign-key check failed.");}
    long Count(string sql)=>Convert.ToInt64(Scalar(db,null,sql));
    if(Count("SELECT count(*) FROM firms WHERE id=1")!=1||Count("SELECT count(*) FROM audit_events WHERE action='RecoveryCopyIsolated' AND entity='Firm' AND entity_id=1")<1||Count("SELECT count(*) FROM accounting_profiles")!=0||Count("SELECT count(*) FROM accounting_lifecycle_hold WHERE id=1")!=1||Count("SELECT coalesce((SELECT enabled FROM owner_approval_events ORDER BY id DESC LIMIT 1),0)")!=0)throw new ArgumentException("Recovery isolation evidence missing or invalid.");
    clients=Count("SELECT count(*) FROM clients");projects=Count("SELECT count(*) FROM projects");documents=Count("SELECT count(*) FROM project_documents")+Count("SELECT count(*) FROM project_agreements");
   }
   using(var fs=new FileStream(staging,FileMode.Open,FileAccess.Read,FileShare.Read)){
    if(fs.Length!=bytes||!Same(Convert.ToHexString(SHA256.HashData(fs)),hash))throw new InvalidOperationException("Validated recovery copy changed.");
   }
   using(var t=active.BeginTransaction(deferred:false)){
    if(generation!=sessionGeneration||RequireAdministration(active,t)!=actor)throw new UnauthorizedAccessException();
    Require(active,t,"Administration.RecoveryCopy");Audit(active,t,actor,"IsolatedRestorePrepared","Firm",1);t.Commit();
   }
   foreach(var suffix in new[]{"-wal","-shm","-journal"})if(File.Exists(staging+suffix)||File.Exists(target+suffix))throw new InvalidOperationException("Unexpected database sidecar files.");
   File.Move(staging,target,false);owned=false;
   return new(target,hash,bytes,clients,projects,documents);
  }finally{
   if(owned)foreach(var file in new[]{staging,staging+"-wal",staging+"-shm",staging+"-journal"})if(File.Exists(file))File.Delete(file);
  }
 }
}
