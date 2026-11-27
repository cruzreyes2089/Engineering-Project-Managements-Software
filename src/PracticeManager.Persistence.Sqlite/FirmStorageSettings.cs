using Microsoft.Data.Sqlite;
using PracticeManager.Core;

namespace PracticeManager.Persistence;

// A firm-local, versioned storage preference; never an approved storage registration.
public sealed record FirmStoragePreference(long Revision,string ProductionRoot,string RestrictedRoot,string RecordedUtc)
{
 public bool Configured=>Revision>0;
 public bool FilesystemVerified=>false;
 public bool ProvisioningAuthorized=>false;
 public override string ToString()=>"[Firm storage preference]";
}
public sealed partial class LocalPractice
{
 private static void UpgradeToV48(SqliteConnection c,SqliteTransaction t)
 {
  Exec(c,t,@"CREATE TABLE firm_storage_preferences(
   id INTEGER PRIMARY KEY,firm_id INTEGER NOT NULL REFERENCES firms(id) CHECK(firm_id=1),
   production_root TEXT NOT NULL CHECK(length(production_root) BETWEEN 4 AND 180),
   restricted_root TEXT NOT NULL CHECK(length(restricted_root) BETWEEN 4 AND 180),
   recorded_by INTEGER NOT NULL REFERENCES users(id),recorded_utc TEXT NOT NULL,
   CHECK(production_root<>restricted_root COLLATE NOCASE));
   CREATE TRIGGER firm_storage_preference_no_update BEFORE UPDATE ON firm_storage_preferences
    BEGIN SELECT RAISE(ABORT,'Storage preference retained'); END;
   CREATE TRIGGER firm_storage_preference_no_delete BEFORE DELETE ON firm_storage_preferences
    BEGIN SELECT RAISE(ABORT,'Storage preference retained'); END;
   PRAGMA user_version=48;");
 }
 private long RequireStorageSettings(SqliteConnection c,SqliteTransaction t)
 {
  var actor=RequireAdministration(c,t);
  Require(c,t,"Documents.Restricted");return actor;
 }
 private static FirmStoragePreference CurrentStoragePreference(SqliteConnection c,SqliteTransaction t)
 {
  using var q=Command(c,t,"SELECT id,production_root,restricted_root,recorded_utc FROM firm_storage_preferences WHERE firm_id=1 ORDER BY id DESC LIMIT 1");
  using var reader=q.ExecuteReader();
  if(!reader.Read())return new(0,"","","");
  var (p,r)=ProjectStorageDrafting.ValidateRoots(reader.GetString(1),reader.GetString(2));
  return new(reader.GetInt64(0),p,r,reader.GetString(3));
 }
 public FirmStoragePreference ReadFirmStoragePreference()
 {
  using var c=Open();using var t=c.BeginTransaction();
  RequireStorageSettings(c,t);return CurrentStoragePreference(c,t);
 }
 public FirmStoragePreference SaveFirmStoragePreference(string productionRoot,string restrictedRoot,long expectedRevision,bool explicitlyConfirmed)
 {
  using var c=Open();using var t=c.BeginTransaction(deferred:false);
  var actor=RequireStorageSettings(c,t);
  if(!explicitlyConfirmed)throw new ArgumentException("Confirm saving the unverified storage preferences.");
  var (p,r)=ProjectStorageDrafting.ValidateRoots(productionRoot,restrictedRoot);
  var current=CurrentStoragePreference(c,t);
  if(current.Revision!=expectedRevision)throw new ArgumentException("Storage settings changed. Refresh before saving.");
  if(current.Configured&&current.ProductionRoot==p&&current.RestrictedRoot==r)return current;
  var now=UtcNow();
  Exec(c,t,"INSERT INTO firm_storage_preferences(firm_id,production_root,restricted_root,recorded_by,recorded_utc) VALUES(1,$p,$r,$a,$u)",
   ("$p",p),("$r",r),("$a",actor),("$u",now));
  var id=Last(c,t);Audit(c,t,actor,"RecordStoragePreference","Firm",1);
  t.Commit();return new(id,p,r,now);
 }
}
