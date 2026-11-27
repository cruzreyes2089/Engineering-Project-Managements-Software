using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
namespace PracticeManager.Persistence;

// Internal, bounded semantic witness. No table contents or individual row digests
// leave the rehearsal. Not a signature or a test of pre-existing data authenticity.
internal static class DatabasePreservationWitness
{
 const int MaxObjects=4096,MaxTables=512,MaxColumns=256,MaxRowsPerTable=100000,MaxRows=1000000;
 const long MaxValueBytes=64L*1024*1024;
 const string Codes="('HistoricalProjects.View','Administration.HistoricalProjectImport')";
 static readonly HashSet<string> ArchiveTables=new(StringComparer.Ordinal)
 {"historical_project_batches","historical_project_rows","historical_project_seals","historical_project_register"};
 static readonly HashSet<string> ArchiveObjects=new(StringComparer.Ordinal)
 {"table:historical_project_batches","table:historical_project_rows","table:historical_project_seals","view:historical_project_register",
  "index:sqlite_autoindex_historical_project_rows_1","trigger:historical_batch_crm_guard","trigger:historical_row_guard","trigger:historical_seal_guard",
  "trigger:historical_batches_no_update","trigger:historical_batches_no_delete","trigger:historical_rows_no_update","trigger:historical_rows_no_delete",
  "trigger:historical_seals_no_update","trigger:historical_seals_no_delete"};
 static string Quote(string name)=>"\""+name.Replace("\"","\"\"")+"\"";
 static void Number(IncrementalHash hash,long n){Span<byte> data=stackalloc byte[8];BinaryPrimitives.WriteInt64LittleEndian(data,n);hash.AppendData(data);}
 static void Text(IncrementalHash hash,string value){var bytes=Encoding.UTF8.GetBytes(value);Number(hash,bytes.Length);hash.AppendData(bytes);}
 static long Count(SqliteConnection db,SqliteTransaction t,string sql)
 {using var q=db.CreateCommand();q.Transaction=t;q.CommandText=sql;return Convert.ToInt64(q.ExecuteScalar());}
 internal static string Capture(SqliteConnection db,int version,bool boundaryTarget)
 {
  using var t=db.BeginTransaction(deferred:true);
  using var q=db.CreateCommand();q.Transaction=t;
  var schema=new List<(string Type,string Name,string Table,string Sql)>();
  q.CommandText="SELECT type,name,tbl_name,coalesce(sql,'') FROM sqlite_schema ORDER BY type COLLATE BINARY,name COLLATE BINARY";
  using(var r=q.ExecuteReader())while(r.Read())
  {
   if(schema.Count>=MaxObjects)throw new ArgumentException("Schema witness object limit exceeded.");
   schema.Add((r.GetString(0),r.GetString(1),r.GetString(2),r.GetString(3)));
  }
  var archive=schema.Where(x=>ArchiveTables.Contains(x.Table)||ArchiveObjects.Contains(x.Type+":"+x.Name)).ToArray();
  if(version==46)
  {
   if(archive.Length!=0||Count(db,t,"SELECT count(*) FROM permissions WHERE code IN "+Codes)!=0||
      Count(db,t,"SELECT count(*) FROM role_permissions WHERE permission_code IN "+Codes)!=0)
    throw new ArgumentException("Schema 46 contains unexpected archive evidence.");
  }
  if(boundaryTarget)
  {
   if(version!=47||!archive.Select(x=>x.Type+":"+x.Name).ToHashSet(StringComparer.Ordinal).SetEquals(ArchiveObjects)||
      archive.Any(x=>!ArchiveTables.Contains(x.Table))||
      Count(db,t,"SELECT count(*) FROM permissions WHERE code IN "+Codes)!=2||
      Count(db,t,"SELECT count(*) FROM role_permissions WHERE permission_code IN "+Codes)!=2||
      Count(db,t,"SELECT count(*) FROM role_permissions rp JOIN roles r ON r.id=rp.role_id WHERE r.name='Administrator' AND rp.allowed=1 AND rp.permission_code IN "+Codes)!=2||
      Count(db,t,"SELECT (SELECT count(*) FROM historical_project_batches)+(SELECT count(*) FROM historical_project_rows)+(SELECT count(*) FROM historical_project_seals)")!=0)
    throw new InvalidOperationException("Unexpected schema 47 archive additions or grants.");
  }
  using var full=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
  Text(full,"CRC-full-database-preservation-v1");
  int total=0,tables=0;
  var buffer=new byte[65536];
  foreach(var e in schema)
  {
   if(boundaryTarget&&ArchiveTables.Contains(e.Table))continue;
   Text(full,e.Type);Text(full,e.Name);Text(full,e.Table);Text(full,e.Sql);
   if(e.Type!="table")continue;
   if(++tables>MaxTables||e.Sql.StartsWith("CREATE VIRTUAL TABLE",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Unsupported table witness layout.");
   var columns=new List<string>();q.CommandText="PRAGMA table_xinfo("+Quote(e.Name)+")";
   using(var r=q.ExecuteReader())while(r.Read())
   {
    if(columns.Count>=MaxColumns||r.GetInt64(6)==1)throw new ArgumentException("Unsupported witness column layout.");
    columns.Add(r.GetString(1));
   }
   if(columns.Count==0)throw new ArgumentException("Missing table columns.");
   Number(full,columns.Count);foreach(var col in columns)Text(full,col);
   // SQLite TEXT is hashed as its stored bytes, not a potentially lossy decoded string.
   q.CommandText="SELECT "+string.Join(",",columns.Select(col=>{var id=Quote(col);return "typeof("+id+"),CASE WHEN typeof("+id+")='text' THEN CAST("+id+" AS BLOB) ELSE "+id+" END";}))+" FROM "+Quote(e.Name);
   if(boundaryTarget&&e.Name=="permissions")q.CommandText+=" WHERE code NOT IN "+Codes;
   if(boundaryTarget&&e.Name=="role_permissions")q.CommandText+=" WHERE permission_code NOT IN "+Codes;
   var rows=new List<string>();
   using(var r=q.ExecuteReader())while(r.Read())
   {
    if(rows.Count>=MaxRowsPerTable||++total>MaxRows)throw new ArgumentException("Database witness row limit exceeded.");
    using var row=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    for(int i=0;i<columns.Count;i++)
    {
     var type=r.GetString(2*i);int value=2*i+1;Text(row,type);
     switch(type)
     {
      case "null":break;
      case "integer":Number(row,r.GetInt64(value));break;
      case "real":Number(row,BitConverter.DoubleToInt64Bits(r.GetDouble(value)));break;
      case "text":case "blob":
       var bytes=r.GetBytes(value,0,null,0,0);
       if(bytes<0||bytes>MaxValueBytes)throw new ArgumentException("Database witness value limit exceeded.");
       Number(row,bytes);
       for(long offset=0;offset<bytes;)
       {
        int count=(int)Math.Min(buffer.Length,bytes-offset);
        int read=(int)r.GetBytes(value,offset,buffer,0,count);
        if(read!=count)throw new InvalidOperationException("Database witness value changed.");
        row.AppendData(buffer,0,read);offset+=read;
       }
       break;
      default:throw new ArgumentException("Unsupported SQLite storage type.");
     }
    }
    rows.Add(Convert.ToHexString(row.GetHashAndReset()));
   }
   rows.Sort(StringComparer.Ordinal);
   Number(full,rows.Count);foreach(var row in rows)Text(full,row);
  }
  CryptographicOperations.ZeroMemory(buffer);
  return Convert.ToHexString(full.GetHashAndReset());
 }
}
