using System.Text;
using Microsoft.Data.Sqlite;
using PracticeManager.Core;

namespace PracticeManager.Persistence;

// Unwired production identity candidate. Only synthetic native tests call it.
// No desktop switch, release authorization or existing-workspace migration.
internal static class ProductionWorkspaceIdentityCandidate
{
 const string MarkerPrefix="CRC-PRACTICE-PRODUCTION-V1:";
 const int ApplicationId=0x43524350; // CRCP
 const int BaseSchema=47;
 const string IdentityTable="production_workspace_identity";
 static string Marker(Guid id)=>MarkerPrefix+id.ToString("N");
 static string ReadMarker(ProductionStorageBoundary layout)
 {
  using var stream=new FileStream(layout.IdentityMarkerPath,FileMode.Open,FileAccess.Read,FileShare.Read);
  if(stream.Length!=MarkerPrefix.Length+32)throw new ArgumentException("Production workspace identity marker is invalid.");
  var bytes=new byte[(int)stream.Length];stream.ReadExactly(bytes);
  var value=Encoding.ASCII.GetString(bytes);
  if(!value.StartsWith(MarkerPrefix,StringComparison.Ordinal)||
    !Guid.TryParseExact(value[MarkerPrefix.Length..],"N",out var id)||id==Guid.Empty||Marker(id)!=value)
   throw new ArgumentException("Production workspace identity marker is invalid.");
  return id.ToString("N");
 }
 static SqliteConnection Connection(string path,SqliteOpenMode mode)
 {
  var c=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=path,Mode=mode,Pooling=false,ForeignKeys=true,DefaultTimeout=10}.ToString());
  try{c.Open();return c;}catch{c.Dispose();throw;}
 }
 static object? Scalar(SqliteConnection c,SqliteTransaction? tx,string sql)
 {
  using var command=c.CreateCommand();command.Transaction=tx;command.CommandText=sql;return command.ExecuteScalar();
 }
 static void Execute(SqliteConnection c,SqliteTransaction tx,string sql,string? identity=null)
 {
  using var command=c.CreateCommand();command.Transaction=tx;command.CommandText=sql;
  if(identity!=null)command.Parameters.AddWithValue("$id",identity);
  command.ExecuteNonQuery();
 }
 internal static string VerifyExisting(ProductionStorageBoundary layout)
 {
  layout.VerifyExistingPrivateDataLayout();
  var id=ReadMarker(layout);
  // Refuse interrupted/unclean candidate instead of implicitly recovering a journal.
  LocalStoragePath.FilePath(layout.DatabasePath,mustExist:true,rejectSidecars:true);
  using var c=Connection(layout.DatabasePath,SqliteOpenMode.ReadOnly);
  using var tx=c.BeginTransaction(deferred:true);
  if(Convert.ToInt64(Scalar(c,tx,"PRAGMA application_id"))!=ApplicationId||
     Convert.ToInt64(Scalar(c,tx,"PRAGMA user_version"))!=BaseSchema)
   throw new ArgumentException("Production database format or schema requires separate review.");
  if(Convert.ToInt64(Scalar(c,tx,"SELECT count(*) FROM sqlite_master WHERE type='table' AND name='production_workspace_identity'"))!=1||
     Convert.ToInt64(Scalar(c,tx,"SELECT count(*) FROM production_workspace_identity"))!=1||
     Scalar(c,tx,"SELECT workspace_id FROM production_workspace_identity WHERE singleton=1") is not string actual||actual!=id)
   throw new ArgumentException("Production marker and database identity do not match.");
  tx.Commit();
  layout.VerifyExistingPrivateDataLayout();
  if(ReadMarker(layout)!=id)throw new ArgumentException("Production workspace identity changed during review.");
  return id;
 }
 // For a newly reserved blank fixture database only. This adds a proposed
 // production-only identity overlay to the base schema, not an active migration.
 internal static string InitializeNewFixture(ProductionStorageBoundary layout)
 {
  if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException("Windows production storage required.");
  // Require a completely absent data directory. Never adopt an existing one.
  try{_=File.GetAttributes(layout.DataRoot);throw new ArgumentException("Existing data location requires recovery review.");}
  catch(FileNotFoundException){}catch(DirectoryNotFoundException){}
  WorkspaceConfidentiality.EnsureWorkspaceDirectory(layout.DataRoot);
  if(!WorkspaceConfidentiality.HasRestrictedDirectoryAcl(layout.DataRoot)||
     Directory.EnumerateFileSystemEntries(layout.DataRoot).Any())
   throw new ArgumentException("New data location is not empty or protected.");
  var path=LocalStoragePath.FilePath(layout.DatabasePath,mustExist:false,mustBeNew:true,rejectSidecars:true);
  using(var reserve=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None)){}
  if(!WorkspaceConfidentiality.HasWorkspaceFileAcl(path))throw new ArgumentException("New database permissions require review.");
  LocalPractice.InitializeReservedProductionFixture(path);
  var identity=Guid.NewGuid();
  using(var c=Connection(path,SqliteOpenMode.ReadWrite))
  using(var tx=c.BeginTransaction(deferred:false))
  {
   if(Convert.ToInt64(Scalar(c,tx,"PRAGMA user_version"))!=BaseSchema||
      Convert.ToInt64(Scalar(c,tx,"PRAGMA application_id"))!=0||
      Convert.ToInt64(Scalar(c,tx,"SELECT count(*) FROM firms"))!=0)
    throw new ArgumentException("Reserved database is not a fresh base schema.");
   Execute(c,tx,"CREATE TABLE production_workspace_identity(singleton INTEGER PRIMARY KEY CHECK(singleton=1),workspace_id TEXT NOT NULL CHECK(length(workspace_id)=32))");
   Execute(c,tx,"INSERT INTO production_workspace_identity(singleton,workspace_id) VALUES(1,$id)",identity.ToString("N"));
   Execute(c,tx,"PRAGMA application_id=1129464656");
   tx.Commit();
  }
  // Marker is written last. Any interruption before this point is a recovery
  // case. Never retry initialization, delete or overwrite partial evidence.
  LocalStoragePath.FilePath(layout.IdentityMarkerPath,mustExist:false,mustBeNew:true,rejectSidecars:true);
  using(var stream=new FileStream(layout.IdentityMarkerPath,FileMode.CreateNew,FileAccess.Write,FileShare.None))
  {stream.Write(Encoding.ASCII.GetBytes(Marker(identity)));stream.Flush(true);}
  return VerifyExisting(layout);
 }
}
