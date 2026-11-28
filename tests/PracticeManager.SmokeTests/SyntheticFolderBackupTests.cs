using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;

// Only a synthetic journal and observed directory state, NOT a backup of document files.
internal static class SyntheticFolderBackupTests
{
 sealed record Witness(string JournalSha256,string PlanFingerprint,string ObservationFingerprint,
  int DirectoryCount,int PresentCount,string? PendingAttempt)
 {
  public bool DocumentContentsBackedUp=>false;
  public bool RestoreToLiveStorageAuthorized=>false;
 }
 static string Digest(string path)
 {
  using var f=File.OpenRead(path);return Convert.ToHexString(SHA256.HashData(f)).ToLowerInvariant();
 }
 static SqliteConnection Open(string path,SqliteOpenMode mode)
 {
  var c=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=path,Mode=mode,ForeignKeys=true,Pooling=false}.ToString());
  c.Open();return c;
 }
 static long Count(SqliteConnection c,string sql)
 {
  using var q=c.CreateCommand();q.CommandText=sql;return Convert.ToInt64(q.ExecuteScalar());
 }
 static void Integrity(SqliteConnection c)
 {
  using var q=c.CreateCommand();q.CommandText="PRAGMA integrity_check";
  if(!Equals(q.ExecuteScalar(),"ok"))throw new IOException("Synthetic journal integrity failure.");
  q.CommandText="PRAGMA foreign_key_check";
  using var reader=q.ExecuteReader();if(reader.Read())throw new IOException("Synthetic journal relationship failure.");
 }
 static Witness Capture(string root,string source,string target,SyntheticFolderProvisioning folders,
  SyntheticFolderJournal journal,bool correspondence,bool restricted)
 {
  using var fence=SyntheticFolderOperationFence.Acquire(root);
  var observation=folders.Preview(correspondence,restricted);
  var pending=journal.Pending();
  if(File.Exists(target))throw new ArgumentException("Backup destination already exists.");
  using(var reserved=new FileStream(target,FileMode.CreateNew,FileAccess.Write,FileShare.None))reserved.Flush(true);
  // Never publish a witness if any backup or verification operation fails.
  using(var db=Open(source,SqliteOpenMode.ReadOnly))
  using(var copy=Open(target,SqliteOpenMode.ReadWrite))
  {
   Integrity(db);db.BackupDatabase(copy);Integrity(copy);
   if(Count(db,"SELECT count(*) FROM attempts")!=Count(copy,"SELECT count(*) FROM attempts")||
      Count(db,"SELECT count(*) FROM outcomes")!=Count(copy,"SELECT count(*) FROM outcomes"))
    throw new IOException("Synthetic backup evidence mismatch.");
  }
  var final=folders.Preview(correspondence,restricted);
  if(final.ReviewFingerprint!=observation.ReviewFingerprint||journal.Pending()!=pending)
   throw new IOException("Synthetic source changed during capture.");
  return new(Digest(target),observation.PlanFingerprint,observation.ReviewFingerprint,
   observation.Steps.Count,observation.Steps.Count(x=>x.Exists),pending);
 }
 static bool Matches(string backup,Witness witness,SyntheticFolderProvisioning folders,
  bool correspondence,bool restricted)
 {
  if(Digest(backup)!=witness.JournalSha256)return false;
  using var db=Open(backup,SqliteOpenMode.ReadOnly);
  Integrity(db);
  var observed=folders.Preview(correspondence,restricted);
  return observed.PlanFingerprint==witness.PlanFingerprint&&observed.ReviewFingerprint==witness.ObservationFingerprint&&
    observed.Steps.Count==witness.DirectoryCount&&observed.Steps.Count(x=>x.Exists)==witness.PresentCount;
 }
 public static void Run(string ownedRoot,Action<bool,string> check)
 {
  var root=Path.Combine(ownedRoot,"folder-backup-"+Guid.NewGuid().ToString("N"));
  Directory.CreateDirectory(root);
  Directory.CreateDirectory(Path.Combine(root,"SyntheticProduction"));
  Directory.CreateDirectory(Path.Combine(root,"SyntheticRestricted"));
  using(var file=new FileStream(Path.Combine(root,SyntheticFolderOperationFence.FileName),FileMode.CreateNew,FileAccess.Write,FileShare.None))file.Flush(true);
  var folders=new SyntheticFolderProvisioning(root,21,"PRJ-000021");
  var source=Path.Combine(root,"synthetic-journal.db");
  var journal=new SyntheticFolderJournal(source,folders,21);
  void Reject<T>(Action action,string label) where T:Exception
  {try{action();}catch(T){check(true,label);return;}throw new Exception("FAIL: "+label);}
  var review=folders.Preview(false,false);
  var backup=Path.Combine(root,"uncertain-backup.db");
  using(var held=SyntheticFolderOperationFence.Acquire(root))
   Reject<IOException>(()=>Capture(root,source,backup,folders,journal,false,false),"folder backup blocked during active workspace operation");
  check(!File.Exists(backup),"blocked folder backup creates no file");
  Reject<IOException>(()=>journal.ExecuteReviewed(false,false,review.ReviewFingerprint,true,
   afterCreate:n=>{if(n==3)throw new IOException("Synthetic interruption");}),"folder backup fixture interruption");
  var pending=journal.Pending()!;
  var witness=Capture(root,source,backup,folders,journal,false,false);
  check(witness.PendingAttempt==pending&&witness.PresentCount==3&&witness.DirectoryCount==10&&
   !witness.DocumentContentsBackedUp&&!witness.RestoreToLiveStorageAuthorized,"folder backup records incomplete directory state without live restore authority");
  using(var db=Open(backup,SqliteOpenMode.ReadOnly))
  {
   check(Count(db,"SELECT count(*) FROM attempts")==1&&Count(db,"SELECT count(*) FROM outcomes")==0,
    "folder backup retains unresolved attempt");
  }
  check(Matches(backup,witness,folders,false,false),"folder backup hash and observation reconcile");
  Reject<ArgumentException>(()=>Capture(root,source,backup,folders,journal,false,false),"folder backup refuses overwrite");
  check(Digest(backup)==witness.JournalSha256,"existing folder backup unchanged");
  var fresh=folders.Preview(false,false);
  var repaired=journal.ExecuteReviewed(false,false,fresh.ReviewFingerprint,true,pending);
  check(journal.State(repaired)=="Confirmed"&&!Matches(backup,witness,folders,false,false),"older backup cannot certify newer physical state");
  var complete=Path.Combine(root,"confirmed-backup.db");
  var second=Capture(root,source,complete,folders,journal,false,false);
  check(second.PendingAttempt==null&&second.PresentCount==10&&Matches(complete,second,folders,false,false),
   "confirmed folder backup paired with observed tree");
  check(!Matches(complete,second,folders,true,false),"folder backup rejects changed template selection");
  var tampered=Path.Combine(root,"changed-backup.db");File.Copy(complete,tampered);
  File.AppendAllText(tampered,"changed");
  check(!Matches(tampered,second,folders,false,false),"folder backup digest rejects modified bytes");
  // No restore of an older journal over the active one. Read-only evidence remains available.
  using(var db=Open(backup,SqliteOpenMode.ReadOnly))
   check(Count(db,"SELECT count(*) FROM outcomes")==0,"older backup retains original uncertainty after repair");
  check(journal.Pending()==null&&journal.State(pending)=="Superseded","backup does not change current journal");
 }
}
