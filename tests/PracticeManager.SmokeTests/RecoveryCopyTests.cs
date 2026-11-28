using Microsoft.Data.Sqlite;
using PracticeManager.Core;
using PracticeManager.Persistence;
using System.Security.Cryptography;
static class RecoveryCopyTests
{
 public static void Run(string path,string password,Action<bool,string> check)
 {
  var s=new LocalPractice(path);
  var target=Path.Combine(Path.GetDirectoryName(path)!,"recovery-copy.db");
  s.Bootstrap("Synthetic recovery firm","admin",password,"CL");
  try{s.CreateRecoveryCopy(target);throw new Exception("Unauthenticated copy allowed");}catch(UnauthorizedAccessException){check(!File.Exists(target),"unauthenticated recovery copy denied");}
  check(s.Login("admin",password)&&s.Can("Administration.RecoveryCopy"),"administrator recovery grant");
  var client=s.SaveClient(new Client(0,"","Business / Company","Synthetic recovery client","","","","","","","","","","","Active"));
  using(var db=new SqliteConnection($"Data Source={path};Pooling=False")){
   db.Open();using var q=db.CreateCommand();
   q.CommandText="INSERT INTO accounting_profiles(user_id,endpoint,protected_value,last_success_utc,revision) SELECT id,'http://127.0.0.1:1/api2',X'010203','2026-11-26T00:00:00Z','synthetic' FROM users WHERE username='admin'";
   q.ExecuteNonQuery();
  }
  var blocked=Path.Combine(Path.GetDirectoryName(path)!,"sidecar-blocked.db");
  Directory.CreateDirectory(blocked+"-journal");
  try{try{s.CreateRecoveryCopy(blocked);throw new Exception("Sidecar destination allowed");}catch(ArgumentException){check(!File.Exists(blocked),"recovery refuses destination sidecar directory");}}
  finally{Directory.Delete(blocked+"-journal");}
  try{s.CreateRecoveryCopy(Path.Combine(Path.GetDirectoryName(path)!,"bad:stream.db"));throw new Exception("ADS destination allowed");}
  catch(ArgumentException){check(true,"recovery rejects alternate stream destination");}
  var publication=Path.Combine(Path.GetDirectoryName(path)!,"synthetic-publication.txt");
  var publicationBytes="Synthetic publication"u8.ToArray();
  var publicationHash=Convert.ToHexString(SHA256.HashData(publicationBytes));
  void Refused(Action action,string label)
  {try{action();}catch(InvalidOperationException){check(true,label);return;}throw new Exception("FAIL: "+label);}
  using(var stage=new RecoveryStaging(publication))
  {
   stage.VerifyPrivateDirectory();
   if(OperatingSystem.IsWindows())check(WorkspaceConfidentiality.HasRestrictedDirectoryAcl(Path.GetDirectoryName(stage.FilePath)!),"staging directory protected before writing confidential bytes");
   File.WriteAllBytes(stage.FilePath,publicationBytes);
   Refused(()=>stage.Publish(publicationBytes.Length+1,publicationHash),"publication refuses mismatched staged length");
   Refused(()=>stage.Publish(publicationBytes.Length,new string('0',64)),"publication refuses mismatched staged hash");
   check(!File.Exists(publication),"failed stage verification publishes no file");
   stage.Publish(publicationBytes.Length,publicationHash);
   check(File.ReadAllBytes(publication).SequenceEqual(publicationBytes),"verified publication retains exact bytes");
  }
  Refused(()=>RecoveryStaging.VerifyIdentity(publication,publicationBytes.Length,new string('0',64)),"destination verification rejects incorrect hash");
  Refused(()=>RecoveryStaging.VerifyIdentity(publication,publicationBytes.Length+1,publicationHash),"destination verification rejects incorrect length");
  check(File.ReadAllBytes(publication).SequenceEqual(publicationBytes),"failed destination verification does not delete published file");
  var collision=Path.Combine(Path.GetDirectoryName(path)!,"synthetic-publication-collision.txt");
  using(var stage=new RecoveryStaging(collision))
  {
   File.WriteAllBytes(stage.FilePath,publicationBytes);
   File.WriteAllText(collision,"Existing destination");
   try{stage.Publish(publicationBytes.Length,publicationHash);throw new Exception("FAIL: publication overwrote destination");}
   catch(ArgumentException){check(File.ReadAllText(collision)=="Existing destination","publication refuses destination created after staging");}
  }
  check(!Directory.EnumerateDirectories(Path.GetDirectoryName(path)!,".crc-recovery-stage-*").Any(),"publication collision removes only application staging");
  var result=s.CreateRecoveryCopy(target);
  if(OperatingSystem.IsWindows())check(WorkspaceConfidentiality.HasRestrictedFileAcl(target),"published recovery copy has protected explicit file ACL");
  check(!Directory.EnumerateDirectories(Path.GetDirectoryName(path)!,".crc-recovery-stage-*").Any(),"successful backup removes private staging directory");
  check(result.Bytes>0&&result.Sha256==Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(target))).ToLowerInvariant(),"recovery copy file hash verified");
  using(var db=new SqliteConnection($"Data Source={target};Mode=ReadOnly;Pooling=False")){
   db.Open();using var q=db.CreateCommand();
   q.CommandText="SELECT count(*) FROM clients WHERE id="+client;check(Convert.ToInt64(q.ExecuteScalar())==1,"recovery copy retains committed client");
   q.CommandText="SELECT count(*) FROM accounting_profiles";check(Convert.ToInt64(q.ExecuteScalar())==0,"recovery copy removes accounting profiles");
   q.CommandText="SELECT reason FROM accounting_lifecycle_hold WHERE id=1";check((string?)q.ExecuteScalar()=="Restore","recovery copy has sticky accounting lifecycle hold");
   q.CommandText="SELECT count(*) FROM audit_events WHERE action='RecoveryCopyIsolated'";check(Convert.ToInt64(q.ExecuteScalar())==1,"recovery copy isolation audited");
  }
  using(var db=new SqliteConnection($"Data Source={path};Pooling=False")){
   db.Open();using var q=db.CreateCommand();
   q.CommandText="SELECT count(*) FROM accounting_profiles";check(Convert.ToInt64(q.ExecuteScalar())==1,"recovery copy does not remove live profiles");
   q.CommandText="SELECT count(*) FROM accounting_lifecycle_hold";check(Convert.ToInt64(q.ExecuteScalar())==0,"recovery copy does not hold source");
  }
  try{s.CreateRecoveryCopy(target);throw new Exception("Overwrite allowed");}catch(ArgumentException){check(result.Sha256==Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(target))).ToLowerInvariant(),"recovery copy never overwrites");}
  var isolated=Path.Combine(Path.GetDirectoryName(path)!,"isolated-restored.db");
  try{s.RestoreRecoveryCopyToNewFile(target,new string('0',64),isolated);throw new Exception("Wrong recovery hash allowed");}catch(ArgumentException){check(!File.Exists(isolated),"restore refuses wrong trusted hash");}
  Directory.CreateDirectory(isolated+"-shm");
  try{try{s.RestoreRecoveryCopyToNewFile(target,result.Sha256,isolated);throw new Exception("Restore sidecar allowed");}catch(ArgumentException){check(!File.Exists(isolated),"restore refuses destination sidecar directory");}}
  finally{Directory.Delete(isolated+"-shm");}
  Directory.CreateDirectory(target+"-wal");
  try{try{s.RestoreRecoveryCopyToNewFile(target,result.Sha256,isolated);throw new Exception("Source sidecar allowed");}catch(ArgumentException){check(!File.Exists(isolated),"restore refuses source sidecar directory");}}
  finally{Directory.Delete(target+"-wal");}
  check(!Directory.EnumerateDirectories(Path.GetDirectoryName(path)!,".crc-recovery-stage-*").Any(),"rejected restore removes private staging directory");
  var restored=s.RestoreRecoveryCopyToNewFile(target,result.Sha256,isolated);
  if(OperatingSystem.IsWindows())check(WorkspaceConfidentiality.HasRestrictedFileAcl(isolated),"published isolated restore has protected explicit file ACL");
  check(!Directory.EnumerateDirectories(Path.GetDirectoryName(path)!,".crc-recovery-stage-*").Any(),"successful restore removes private staging directory");
  check(restored.Clients==1&&restored.Projects==0&&restored.Documents==0&&restored.Sha256==result.Sha256,"isolated restore verified counts and exact bytes");
  var reopened=new LocalPractice(isolated);check(reopened.Login("admin",password)&&reopened.SearchClients("").Single().Id==client,"isolated restored copy opens independently");
  try{s.RestoreRecoveryCopyToNewFile(target,result.Sha256,isolated);throw new Exception("Restore overwrite allowed");}catch(ArgumentException){check(true,"restore refuses overwrite");}
  try{s.RestoreRecoveryCopyToNewFile(path,result.Sha256,Path.Combine(Path.GetDirectoryName(path)!,"live-copy.db"));throw new Exception("Live database allowed as recovery copy");}catch(ArgumentException){check(true,"restore rejects active database path");}
  check(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(target))).ToLowerInvariant()==result.Sha256,"restore leaves recovery original intact");
  using(var db=new SqliteConnection($"Data Source={path};Pooling=False")){
   db.Open();using var q=db.CreateCommand();q.CommandText="UPDATE role_permissions SET allowed=0 WHERE permission_code='Administration.RecoveryCopy'";q.ExecuteNonQuery();
  }
  var other=Path.Combine(Path.GetDirectoryName(path)!,"denied-copy.db");
  try{s.CreateRecoveryCopy(other);throw new Exception("Revoked grant allowed");}catch(UnauthorizedAccessException){check(!File.Exists(other),"revoked recovery grant denied");}
  try{s.RestoreRecoveryCopyToNewFile(target,result.Sha256,other);throw new Exception("Revoked restore grant allowed");}catch(UnauthorizedAccessException){check(!File.Exists(other),"revoked restore grant denied");}
 }
}
