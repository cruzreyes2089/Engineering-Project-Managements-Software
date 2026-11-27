using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
namespace PracticeManager.Persistence;

public sealed record UpgradeRehearsalResult(int SourceSchema,int TargetSchema,string SourceSha256,long Clients,long Contacts,long Users,bool SourceUnchanged);

// Source-only qualification helper. Never open the original with SQLite and never
// publish the disposable copy as a working database. The local folder must be trusted.
public static class UpgradeRehearsal
{
 const int CurrentSchema=52;
 const long MaximumBytes=4L*1024*1024*1024;
 static readonly string[] Sidecars=["-wal","-shm","-journal"];
 private sealed record Snapshot(int Version,long Clients,long Contacts,long Users,string IdentityHash);
 private static void RequireNoSidecars(string path)
 {
  if(Sidecars.Any(s=>File.Exists(path+s)||Directory.Exists(path+s)))throw new ArgumentException("Closed source without database sidecars required.");
 }
 private static string Hash(FileStream stream)
 {
  stream.Position=0;return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
 }
 private static Snapshot Inspect(string path)
 {
  using var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=path,Mode=SqliteOpenMode.ReadOnly,Pooling=false,ForeignKeys=true,DefaultTimeout=10}.ToString());
  db.Open();using var q=db.CreateCommand();
  q.CommandText="PRAGMA trusted_schema=OFF; PRAGMA query_only=ON";q.ExecuteNonQuery();
  q.CommandText="PRAGMA user_version";var version=Convert.ToInt32(q.ExecuteScalar());
  if(version<1||version>CurrentSchema)throw new ArgumentException("Unsupported source schema for isolated rehearsal.");
  q.CommandText="PRAGMA integrity_check";using(var r=q.ExecuteReader()){if(!r.Read()||r.GetString(0)!="ok"||r.Read())throw new ArgumentException("Database integrity check failed.");}
  q.CommandText="PRAGMA foreign_key_check";using(var r=q.ExecuteReader()){if(r.Read())throw new ArgumentException("Database foreign-key check failed.");}
  // Stable v1 columns only; migrations may legitimately add grants or new evidence.
  // Length-delimited JSON arrays are hashed locally, never returned or logged.
  using var h=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
  long Scan(string sql)
  {
   h.AppendData(Encoding.UTF8.GetBytes(sql+"\n"));q.CommandText=sql;
   using var r=q.ExecuteReader();long rows=0;
   while(r.Read()){var fields=new object[r.FieldCount];r.GetValues(fields);h.AppendData(JsonSerializer.SerializeToUtf8Bytes(fields));h.AppendData(new byte[]{10});rows++;}
   return rows;
  }
  var firms=Scan("SELECT id,name,client_prefix FROM firms ORDER BY id");if(firms!=1)throw new ArgumentException("Initialized single firm required.");
  var clients=Scan("SELECT id,client_no FROM clients ORDER BY id");
  var contacts=Scan("SELECT id,client_id FROM contacts ORDER BY id");
  var users=Scan("SELECT id,username,normalized_username,role_id,active FROM users ORDER BY id");
  Scan("SELECT id,actor_id,action,entity,entity_id,occurred_at FROM audit_events ORDER BY id");
  return new(version,clients,contacts,users,Convert.ToHexString(h.GetHashAndReset()));
 }
 public static UpgradeRehearsalResult Run(string source,string expectedSha256,bool confirmed)
 {
  if(!confirmed)throw new ArgumentException("Confirm a trusted closed synthetic source copy.");
  if(expectedSha256==null||expectedSha256.Length!=64||!expectedSha256.All(Uri.IsHexDigit))throw new ArgumentException("Exact independently retained source SHA256 required.");
  var input=Path.GetFullPath(source);
  if(!string.Equals(Path.GetExtension(input),".db",StringComparison.OrdinalIgnoreCase)||!File.Exists(input)||(File.GetAttributes(input)&FileAttributes.ReparsePoint)!=0)throw new ArgumentException("Regular .db source copy required.");
  RequireNoSidecars(input);var root=Path.GetDirectoryName(input)!;
  if((File.GetAttributes(root)&FileAttributes.ReparsePoint)!=0||input.StartsWith(@"\\",StringComparison.Ordinal))throw new ArgumentException("Trusted local source directory required.");
  var folder=Path.Combine(root,".crc-upgrade-"+Guid.NewGuid().ToString("N"));
  var staging=Path.Combine(folder,"rehearsal.db");
  bool owned=false;
  using var src=new FileStream(input,FileMode.Open,FileAccess.Read,FileShare.Read);
  if(src.Length<512||src.Length>MaximumBytes)throw new ArgumentException("Source size outside supported range.");
  var length=src.Length;var hash=Hash(src);
  if(!string.Equals(hash,expectedSha256,StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Source hash mismatch.");
  src.Position=0;var header=new byte[16];src.ReadExactly(header);
  if(!header.SequenceEqual(Encoding.ASCII.GetBytes("SQLite format 3\0")))throw new ArgumentException("SQLite database header required.");
  try
  {
   if(Directory.Exists(folder)||File.Exists(folder))throw new ArgumentException("Rehearsal directory already exists.");
   Directory.CreateDirectory(folder);owned=true;
   if((File.GetAttributes(folder)&FileAttributes.ReparsePoint)!=0)throw new ArgumentException("Unexpected redirected rehearsal directory.");
   src.Position=0;
   using(var dst=new FileStream(staging,FileMode.CreateNew,FileAccess.Write,FileShare.None)){src.CopyTo(dst);dst.Flush(true);if(dst.Length!=length)throw new InvalidOperationException("Source copy length changed.");}
   using(var copied=new FileStream(staging,FileMode.Open,FileAccess.Read,FileShare.Read)){if(Hash(copied)!=hash)throw new InvalidOperationException("Disposable copy hash mismatch.");}
   Snapshot before;
   try{before=Inspect(staging);}catch(SqliteException e){throw new ArgumentException("Source SQLite preflight failed.",e);}
   _=new LocalPractice(staging);
   var after=Inspect(staging);
   if(after.Version!=CurrentSchema||before.Clients!=after.Clients||before.Contacts!=after.Contacts||before.Users!=after.Users||before.IdentityHash!=after.IdentityHash)throw new InvalidOperationException("Upgrade identity reconciliation failed.");
   RequireNoSidecars(input);
   if(src.Length!=length||Hash(src)!=hash)throw new InvalidOperationException("Source copy changed during rehearsal.");
   return new(before.Version,after.Version,hash,after.Clients,after.Contacts,after.Users,true);
  }
  finally
  {
   if(owned)
   {
    foreach(var file in new[]{staging}.Concat(Sidecars.Select(s=>staging+s)))if(File.Exists(file))File.Delete(file);
    Directory.Delete(folder,false);
   }
  }
 }
}
