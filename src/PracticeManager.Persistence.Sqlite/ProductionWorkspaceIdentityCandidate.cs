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
 static string Marker(Guid id)=>MarkerPrefix+id.ToString("N");
 static string ReadMarker(ProductionStorageBoundary layout)=>ReadMarkerFile(layout.IdentityMarkerPath);
 static string ReadMarkerFile(string path)
 {
  using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
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
  try{c.Open();using var q=c.CreateCommand();q.CommandText="PRAGMA trusted_schema=OFF";q.ExecuteNonQuery();if(Convert.ToInt64(Scalar(c,null,"PRAGMA trusted_schema"))!=0||Convert.ToInt64(Scalar(c,null,"PRAGMA foreign_keys"))!=1)throw new InvalidOperationException("Required SQLite safeguards unavailable.");return c;}catch{c.Dispose();throw;}
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
 internal static void VerifyMarkerAndLayout(ProductionStorageBoundary layout,string expected)
 {
  layout.VerifyExistingPrivateDataLayout();
  if(ReadMarker(layout)!=expected)throw new ArgumentException("Production workspace marker changed.");
 }
 internal static void VerifyConnectionIdentity(SqliteConnection c,SqliteTransaction? tx,string expected)
 {
  if(Convert.ToInt64(Scalar(c,tx,"PRAGMA application_id"))!=ApplicationId||
     Convert.ToInt64(Scalar(c,tx,"PRAGMA user_version"))!=BaseSchema||
     Convert.ToInt64(Scalar(c,tx,"SELECT count(*) FROM sqlite_master WHERE type='table' AND name='production_workspace_identity'"))!=1||
     Convert.ToInt64(Scalar(c,tx,"SELECT count(*) FROM production_workspace_identity"))!=1||
     Scalar(c,tx,"SELECT workspace_id FROM production_workspace_identity WHERE singleton=1") is not string actual||actual!=expected)
   throw new ArgumentException("Production database identity or schema changed.");
 }
 // The roots are fixed when the fixture is created. No caller-provided override,
 // silent path adoption, or implicit settings repair during reopen.
 internal static void VerifyConfiguredRoots(SqliteConnection c,SqliteTransaction? tx,ProductionStorageBoundary layout)
 {
  if(Convert.ToInt64(Scalar(c,tx,"SELECT count(*) FROM production_storage_settings"))!=1||
    Scalar(c,tx,"SELECT project_root FROM production_storage_settings WHERE singleton=1") is not string projects||projects!=layout.ProjectRoot||
    Scalar(c,tx,"SELECT backup_root FROM production_storage_settings WHERE singleton=1") is not string backup||backup!=layout.BackupRoot)
   throw new ArgumentException("Stored workspace locations require review.");
 }
 internal static ProductionStorageBoundary ReadStoredLayout(string localAppData,string expectedIdentity)
 {
  var data=ProductionStorageBoundary.ReviewExistingDataDirectory(localAppData);
  var marker=Path.Combine(data,"production-workspace.txt");
  if(ReadMarkerFile(marker)!=expectedIdentity)throw new ArgumentException("Unexpected workspace identity.");
  var path=LocalStoragePath.FilePath(Path.Combine(data,"practice.db"),mustExist:true,rejectSidecars:true);
  using var c=Connection(path,SqliteOpenMode.ReadOnly);
  using var tx=c.BeginTransaction(deferred:true);
  VerifyConnectionIdentity(c,tx,expectedIdentity);
  if(Convert.ToInt64(Scalar(c,tx,"SELECT count(*) FROM production_storage_settings"))!=1||
     Scalar(c,tx,"SELECT project_root FROM production_storage_settings WHERE singleton=1") is not string projects||
     Scalar(c,tx,"SELECT backup_root FROM production_storage_settings WHERE singleton=1") is not string backup)
   throw new ArgumentException("Stored workspace locations require review.");
  var layout=ProductionStorageBoundary.Review(localAppData,projects,backup);
  VerifyConfiguredRoots(c,tx,layout);
  tx.Commit();
  VerifyMarkerAndLayout(layout,expectedIdentity);
  return layout;
 }
 internal static string VerifyExisting(ProductionStorageBoundary layout)
 {
  layout.VerifyExistingPrivateDataLayout();
  var id=ReadMarker(layout);
  // Refuse interrupted/unclean candidate instead of implicitly recovering a journal.
  LocalStoragePath.FilePath(layout.DatabasePath,mustExist:true,rejectSidecars:true);
  using var c=Connection(layout.DatabasePath,SqliteOpenMode.ReadOnly);
  using var tx=c.BeginTransaction(deferred:true);
  VerifyConnectionIdentity(c,tx,id);
  VerifyConfiguredRoots(c,tx,layout);
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
   Execute(c,tx,"CREATE TABLE production_storage_settings(singleton INTEGER PRIMARY KEY CHECK(singleton=1),project_root TEXT NOT NULL CHECK(length(project_root)>0),backup_root TEXT NOT NULL CHECK(length(backup_root)>0))");
   using(var settings=c.CreateCommand())
   {
    settings.Transaction=tx;settings.CommandText="INSERT INTO production_storage_settings(singleton,project_root,backup_root) VALUES(1,$projects,$backup)";
    settings.Parameters.AddWithValue("$projects",layout.ProjectRoot);settings.Parameters.AddWithValue("$backup",layout.BackupRoot);
    settings.ExecuteNonQuery();
   }
   Execute(c,tx,$"PRAGMA application_id={ApplicationId}");
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
