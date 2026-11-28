using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using PracticeManager.Core;
namespace PracticeManager.Persistence;

// Validation-only: closed synthetic input, disposable private copy, no
// production startup or automatic restore. Existing migration witnesses are
// applied sequentially rather than changing the ordinary schema-47 constructor.
internal static class InstallerUpgradeRehearsalCandidate
{
 internal sealed record Result(int SourceSchema,int TargetSchema,string SourceSha256,string TargetSha256,bool SourceUnchanged,bool PreservationVerified);
 static void Require(bool ok)
 {if(!ok)throw new ArgumentException("Installer upgrade rehearsal requires review.");}
 static string Hash(Stream stream)
 {stream.Position=0;return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();}
 internal static Result Run(string syntheticSource,string expectedSourceHash,string protectedStagingParent,int targetSchema)
 {
  Require(OperatingSystem.IsWindows()&&targetSchema is >=47 and <=49);
  Require(expectedSourceHash.Length==64&&expectedSourceHash.All(c=>c is >= '0' and <= '9' or >= 'a' and <= 'f'));
  var source=LocalStoragePath.FilePath(syntheticSource,mustExist:true,rejectSidecars:true);
  var parent=LocalStoragePath.DirectoryPath(protectedStagingParent);
  Require(WorkspaceConfidentiality.HasRestrictedDirectoryAcl(parent)&&WorkspaceConfidentiality.HasWorkspaceFileAcl(source));
  Require(!ProjectFolderPolicy.Overlaps(source,parent));
  using var input=new FileStream(source,FileMode.Open,FileAccess.Read,FileShare.Read);
  var length=input.Length;
  Require(length is >=512 and <=4L*1024*1024*1024&&Hash(input)==expectedSourceHash);
  input.Position=0;
  var folder=Path.Combine(parent,"crc-upgrade-rehearsal-"+Guid.NewGuid().ToString("N"));
  Require(!File.Exists(folder)&&!Directory.Exists(folder));
  WorkspaceConfidentiality.EnsureWorkspaceDirectory(folder);
  Require(WorkspaceConfidentiality.HasRestrictedDirectoryAcl(folder)&&!Directory.EnumerateFileSystemEntries(folder).Any());
  var copy=LocalStoragePath.FilePath(Path.Combine(folder,"rehearsal.db"),mustExist:false,mustBeNew:true,rejectSidecars:true);
  // Keep the isolated copy even on failure for review; never clean unknown files.
  using(var output=new FileStream(copy,FileMode.CreateNew,FileAccess.Write,FileShare.None))
  {input.CopyTo(output);output.Flush(true);Require(output.Length==length);}
  Require(WorkspaceConfidentiality.HasWorkspaceFileAcl(copy));
  using(var observed=File.OpenRead(copy))Require(Hash(observed)==expectedSourceHash);
  int initial;
  using(var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=copy,Mode=SqliteOpenMode.ReadWrite,Pooling=false,ForeignKeys=true}.ToString()))
  {
   db.Open();
   using var q=db.CreateCommand();
   q.CommandText="PRAGMA trusted_schema=OFF";q.ExecuteNonQuery();
   q.CommandText="PRAGMA user_version";initial=Convert.ToInt32(q.ExecuteScalar());
   Require(initial is >=47 and <=49&&targetSchema>=initial);
   q.CommandText="PRAGMA integrity_check";using(var rows=q.ExecuteReader())Require(rows.Read()&&rows.GetString(0)=="ok"&&!rows.Read());
   q.CommandText="PRAGMA foreign_key_check";using(var rows=q.ExecuteReader())Require(!rows.Read());
   using(var t=db.BeginTransaction(deferred:false))
   {
    q.Transaction=t;
    q.CommandText="SELECT count(*) FROM firms";Require(Convert.ToInt64(q.ExecuteScalar())==1);
    if(initial>=48){EffortBudgetMigrationCandidate.VerifyLayout(db,t);_=EffortBudgetStore.VerifyDatabase(db,t);}
    if(initial==49){DocumentRegisterMigrationCandidate.VerifyLayout(db,t);_=DocumentRegisterVerification.Verify(db,t);}
    var before=DatabasePreservationWitness.Capture(db,t,initial,false);
    if(initial==47&&targetSchema>=48)_=EffortBudgetMigrationCandidate.Apply(db,t);
    if(initial<=48&&targetSchema==49)_=DocumentRegisterMigrationCandidate.Apply(db,t);
    if(initial==targetSchema)Require(DatabasePreservationWitness.Capture(db,t,initial,false)==before);
    q.CommandText="PRAGMA user_version";Require(Convert.ToInt32(q.ExecuteScalar())==targetSchema);
    q.CommandText="PRAGMA foreign_key_check";using(var rows=q.ExecuteReader())Require(!rows.Read());
    t.Commit();
    q.Transaction=null;
   }
   q.CommandText="PRAGMA integrity_check";using(var rows=q.ExecuteReader())Require(rows.Read()&&rows.GetString(0)=="ok"&&!rows.Read());
  }
  _=LocalStoragePath.FilePath(source,mustExist:true,rejectSidecars:true);
  Require(input.Length==length&&Hash(input)==expectedSourceHash);
  _=LocalStoragePath.FilePath(copy,mustExist:true,rejectSidecars:true);
  using var final=File.OpenRead(copy);
  return new(initial,targetSchema,expectedSourceHash,Hash(final),true,true);
 }
}
