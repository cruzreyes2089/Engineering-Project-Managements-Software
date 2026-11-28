using PracticeManager.Core;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using PracticeManager.Persistence;
internal static class UpgradeRehearsalTests
{
 internal static void Run(string legacyPath,string pw,Action<bool,string> check)
 {
  var root=Path.GetDirectoryName(legacyPath)!;
  string Hash(string path){using var stream=File.OpenRead(path);return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();}
  void Invalid(Action a,string name){try{a();}catch(ArgumentException){check(true,name);return;}throw new Exception("FAIL: "+name);}
  var hash=Hash(legacyPath);
  Invalid(()=>UpgradeRehearsal.Run(legacyPath,hash,false),"upgrade rehearsal requires explicit trusted closed copy confirmation");
  Invalid(()=>UpgradeRehearsal.Run(legacyPath,new string('0',64),true),"upgrade rehearsal requires exact source hash");
  var result=UpgradeRehearsal.Run(legacyPath,hash,true);
  check(result.SourceSchema==1&&result.TargetSchema==47&&result.SourceSha256==hash&&result.SourceUnchanged,"isolated rehearsal upgrades exact legacy copy only");
  check(result.Clients==1&&result.Contacts==0&&result.Users==2,"upgrade rehearsal preserves baseline identities");
  check(!result.FullSchemaAndRowsPreserved,"older migration does not claim full additive preservation");
  check(Hash(legacyPath)==hash,"legacy source bytes remain unchanged after successful rehearsal");
  using(var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=legacyPath,Mode=SqliteOpenMode.ReadOnly,Pooling=false}.ToString())){db.Open();using var q=db.CreateCommand();q.CommandText="PRAGMA user_version";check(Convert.ToInt32(q.ExecuteScalar())==1,"source remains on original schema");}
  var bad=Path.Combine(root,"upgrade-malformed.db");File.WriteAllBytes(bad,new byte[1024]);Invalid(()=>UpgradeRehearsal.Run(bad,Hash(bad),true),"non-SQLite copy rejected");
  var newer=Path.Combine(root,"upgrade-future.db");File.Copy(legacyPath,newer);
  using(var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=newer,Pooling=false}.ToString())){db.Open();using var q=db.CreateCommand();q.CommandText="PRAGMA user_version=999";q.ExecuteNonQuery();}
  var futureHash=Hash(newer);Invalid(()=>UpgradeRehearsal.Run(newer,futureHash,true),"unsupported future schema rejected");
  check(Hash(newer)==futureHash,"unsupported source unchanged");
  var collision=Path.Combine(root,"upgrade-collision.db");File.Copy(legacyPath,collision);
  using(var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=collision,Pooling=false}.ToString())){db.Open();using var q=db.CreateCommand();q.CommandText="CREATE TABLE accounting_profiles(synthetic INTEGER)";q.ExecuteNonQuery();}
  var collisionHash=Hash(collision);
  try{UpgradeRehearsal.Run(collision,collisionHash,true);throw new Exception("FAIL: injected upgrade collision");}
  catch(SqliteException ex) when(ex.Message.Contains("already exists",StringComparison.OrdinalIgnoreCase)){check(Hash(collision)==collisionHash,"migration failure leaves source bytes unchanged");}
  File.WriteAllText(legacyPath+"-wal","synthetic sidecar");
  try{Invalid(()=>UpgradeRehearsal.Run(legacyPath,hash,true),"closed source with sidecar rejected");}finally{File.Delete(legacyPath+"-wal");}
  check(!Directory.EnumerateDirectories(root,".crc-upgrade-*").Any(),"no owned disposable upgrade database left beside source");
  if(OperatingSystem.IsWindows())
  {
   var synthetic=PracticeManager.Core.WorkspaceConfidentiality.EnsureWorkspaceDirectory(Path.Combine(root,"installer-upgrade-source"));
   var stage=PracticeManager.Core.WorkspaceConfidentiality.EnsureWorkspaceDirectory(Path.Combine(root,"installer-upgrade-stage"));
   var current=Path.Combine(synthetic,"source.db");
   File.Copy(legacyPath,current,false);
   _=new LocalPractice(current);
   var currentHash=Hash(current);
   var candidate=InstallerUpgradeRehearsalCandidate.Run(current,currentHash,stage,49);
   check(candidate.SourceSchema==47&&candidate.TargetSchema==49&&candidate.PreservationVerified&&candidate.SourceUnchanged,"combined schema 47 to 49 isolated rehearsal preserves existing data");
   check(Hash(current)==currentHash,"combined rehearsal preserves original database bytes");
   Invalid(()=>InstallerUpgradeRehearsalCandidate.Run(current,new string('0',64),stage,49),"combined rehearsal refuses incorrect source hash");
   var staged=Directory.EnumerateFiles(stage,"rehearsal.db",SearchOption.AllDirectories).Single();
   check(Hash(staged)==candidate.TargetSha256,"combined rehearsal retains exact candidate evidence");
   var reviewTime=DateTimeOffset.FromUnixTimeSeconds(1900000000);
   var release=new InstallerReleaseEvidence.Evidence(new(InstallerCoordinatorCandidate.Operation.Upgrade,new string('a',64),new string('b',64),new string('c',64)),new string('d',40),new string('e',64),new string('f',64),1899999900,1900000100);
   var binding=new InstallerRecoveryBindingCandidate.Binding(release.Commit,release.Plan.PreviousVersion!,release.Plan.Version,candidate.SourceSchema,candidate.TargetSchema,candidate.SourceSha256,candidate.TargetSha256);
   check(InstallerRecoveryBindingCandidate.Review(release,binding,current,staged,reviewTime)==binding,"rehearsal files match exact reviewed upgrade identity");
   Invalid(()=>InstallerRecoveryBindingCandidate.Review(release,binding with {Commit=new string('e',40)},current,staged,reviewTime),"recovery binding rejects different release revision");
   Invalid(()=>InstallerRecoveryBindingCandidate.Review(release,binding with {PreviousInventory=new string('e',64)},current,staged,reviewTime),"recovery binding rejects different prior version");
   Invalid(()=>InstallerRecoveryBindingCandidate.Review(release,binding with {TargetSchema=48},current,staged,reviewTime),"recovery binding rejects wrong target schema");
   Invalid(()=>InstallerRecoveryBindingCandidate.Review(release,binding with {SourceSha256=new string('0',64)},current,staged,reviewTime),"recovery binding rejects wrong source hash");
   Invalid(()=>InstallerRecoveryBindingCandidate.Review(release,binding,current,staged,reviewTime.AddMinutes(2)),"recovery binding refuses expired release");
   check(Hash(current)==currentHash&&Hash(staged)==candidate.TargetSha256,"recovery binding leaves both database files unchanged");

   Invalid(()=>InstallerUpgradeRehearsalCandidate.Run(staged,candidate.TargetSha256,synthetic,47),"combined rehearsal refuses downgrade");
  }
 }
}
