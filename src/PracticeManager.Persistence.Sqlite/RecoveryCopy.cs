using Microsoft.Data.Sqlite;
using PracticeManager.Core;
using System.Security.Cryptography;
namespace PracticeManager.Persistence;

public sealed record RecoveryCopyResult(string Path,string Sha256,long Bytes);

// A confidential local recovery copy, not a live restore or a portable accounting enrollment.
public sealed partial class LocalPractice
{
 public RecoveryCopyResult CreateRecoveryCopy(string destination)
 {
  var target=LocalStoragePath.FilePath(destination,mustExist:false,mustBeNew:true,rejectSidecars:true);
  if(!string.Equals(Path.GetExtension(target),".db",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Choose a new .db recovery-copy file.");
  var folder=Path.GetDirectoryName(target)??throw new ArgumentException("Choose an existing folder.");
  if(!Directory.Exists(folder)||File.Exists(target)||Directory.Exists(target))throw new ArgumentException("Choose a new file in an existing folder; existing files are never overwritten.");
  using var c=Open();
  long actor;long schema;EffortBudgetVerification? budget=null;
  using(var t=c.BeginTransaction(deferred:false)){
   actor=Require(c,t,"Administration.RecoveryCopy");
   schema=Convert.ToInt64(Scalar(c,t,"PRAGMA user_version"));
   if(schema is not (47 or 48))throw new ArgumentException("Unsupported recovery schema.");
   Audit(c,t,actor,"RecoveryCopyRequested","Firm",1);
   t.Commit();
  }
  using var stage=new RecoveryStaging(target);var temporary=stage.FilePath;
  using(var file=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)){}
   using(var copy=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=temporary,ForeignKeys=true,Pooling=false,DefaultTimeout=10}.ToString())){
    copy.Open();
    // SQLite backup API takes a consistent database image, including committed journal data.
    c.BackupDatabase(copy);
    using(var t=copy.BeginTransaction(deferred:false)){
     if(Require(copy,t,"Administration.RecoveryCopy")!=actor)throw new UnauthorizedAccessException();
     _=HistoricalRows(copy,t); // Refuse incomplete or inconsistent sealed archive before publishing.
     if(Convert.ToInt64(Scalar(copy,t,"PRAGMA user_version"))!=schema)throw new InvalidOperationException("Recovery schema changed.");
     if(schema==48){EffortBudgetMigrationCandidate.VerifyLayout(copy,t);budget=EffortBudgetStore.VerifyDatabase(copy,t);}
     Exec(copy,t,"PRAGMA secure_delete=ON");
     Exec(copy,t,"DELETE FROM accounting_profiles");
     Exec(copy,t,"INSERT OR IGNORE INTO accounting_lifecycle_hold(id,reason,declared_by,declared_utc) VALUES(1,'Restore',$a,$u)",("$a",actor),("$u",UtcNow()));
     Exec(copy,t,"INSERT INTO owner_approval_events(owner_id,enabled,reason,actor_id,recorded_utc) SELECT owner_id,0,'Recovery copy: explicit reauthorization required',$a,$u FROM owner_approval_events WHERE id=(SELECT max(id) FROM owner_approval_events) AND enabled=1",("$a",actor),("$u",UtcNow()));
     Audit(copy,t,actor,"RecoveryCopyIsolated","Firm",1);
     t.Commit();
    }
    using(var q=copy.CreateCommand()){q.CommandText="VACUUM";q.ExecuteNonQuery();}
    using(var q=copy.CreateCommand()){q.CommandText="PRAGMA integrity_check";using var r=q.ExecuteReader();if(!r.Read()||r.GetString(0)!="ok"||r.Read())throw new InvalidOperationException("Recovery integrity check failed.");}
    using(var q=copy.CreateCommand()){q.CommandText="PRAGMA foreign_key_check";using var r=q.ExecuteReader();if(r.Read())throw new InvalidOperationException("Recovery foreign-key check failed.");}
    if(schema==48){using var t=copy.BeginTransaction(deferred:true);EffortBudgetMigrationCandidate.VerifyLayout(copy,t);if(EffortBudgetStore.VerifyDatabase(copy,t)!=budget)throw new InvalidOperationException("Recovery budget history changed during isolation.");}
   }
   string hash;long bytes;
   using(var stream=new FileStream(temporary,FileMode.Open,FileAccess.ReadWrite,FileShare.None)){
    stream.Flush(true);bytes=stream.Length;stream.Position=0;hash=Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
   }
   using(var t=c.BeginTransaction(deferred:false)){
    if(Require(c,t,"Administration.RecoveryCopy")!=actor)throw new UnauthorizedAccessException();
    if(Convert.ToInt64(Scalar(c,t,"PRAGMA user_version"))!=schema)throw new InvalidOperationException("Recovery source schema changed.");
    Audit(c,t,actor,"RecoveryCopyPrepared","Firm",1);
    t.Commit();
   }
   stage.Publish();
   return new(target,hash,bytes);
 }
}
