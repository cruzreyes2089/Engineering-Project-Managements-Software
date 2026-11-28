using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PracticeManager.Core;
using PracticeManager.Persistence;

internal static class SyntheticHistoricalNativeTests
{
 static string Digest(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
 static readonly HistoricalProjectRow[] Expected=[
  new("101","001","OLD-01","Synthetic Alpha Project","Synthetic site A","Active","Approved","REF-01"),
  new("102","002","old-01","Synthetic Beta Project","Synthetic site B","Closed","Unknown","REF-02")
 ];
 public static (byte[] Projects,byte[] Census) Fixture(byte[] crm,string source)
 {
  var project=JsonSerializer.SerializeToUtf8Bytes(new{format="CRC-Legacy-Projects-v1",sourceSha256=source,
   crmTransferSha256=Digest(crm),projects=Expected.Select(x=>new{legacyId=x.LegacyId,legacyClientId=x.LegacyClientId,
   legacyProjectNumber=x.LegacyProjectNumber,title=x.Title,site=x.Site,legacyStatus=x.LegacyStatus,
   legacyAuthorizationStatus=x.LegacyAuthorizationStatus,authorizationEvidenceRef=x.AuthorizationEvidenceRef})});
  var census=JsonSerializer.SerializeToUtf8Bytes(new{format="CRC-Legacy-Project-Census-v1",sourceSha256=source,
   crmTransferSha256=Digest(crm),projectTransferSha256=Digest(project),projects=Expected.Select(x=>new{
   legacyId=x.LegacyId,legacyClientId=x.LegacyClientId,legacyProjectNumber=x.LegacyProjectNumber})});
  return(project,census);
 }
 public static void VerifyFixture(byte[] crm,byte[] projects,byte[] census,string source)
 {
  SyntheticCrmNativeTests.VerifyFixture(crm,false);
  var parsed=HistoricalProjectTransfer.Parse(crm,projects,census,source,Digest(crm),Digest(projects),Digest(census));
  if(!parsed.Rows.OrderBy(x=>x.LegacyId,StringComparer.Ordinal).SequenceEqual(Expected))
   throw new ArgumentException("Synthetic historical fixture values do not match.");
 }
 public static void Run(string root,byte[] crm,byte[] projects,byte[] census,string source,Action<bool,string> check)
 {
  // All externally supplied values must match the fixed synthetic fixture before database creation.
  VerifyFixture(crm,projects,census,source);
  Directory.CreateDirectory(root);
  var path=Path.Combine(root,"synthetic-historical.db");
  const string pw="Synthetic-test-passphrase-0001";
  SyntheticCrmNativeTests.Run(path,crm,false,check);
  var s=new LocalPractice(path);
  check(s.Login("admin",pw),"combined historical fixture administrator login");
  long Count(string sql)
  {
   using var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=path,Pooling=false,Mode=SqliteOpenMode.ReadOnly}.ToString());
   db.Open();using var q=db.CreateCommand();q.CommandText=sql;return Convert.ToInt64(q.ExecuteScalar());
  }
  var ch=Digest(crm);var ph=Digest(projects);var ih=Digest(census);
  var preview=s.PreviewHistoricalProjects(crm,projects,census,source,ch,ph,ih);
  check(preview.TargetStatus=="ReadyForExplicitReview"&&preview.Projects==2&&preview.DuplicateNumberGroups==1&&
   preview.InactiveSourceClientProjects==1&&!preview.WorkAuthorized&&!preview.SourceAuthenticityVerified,"combined exact historical preview");
  check(Count("SELECT count(*) FROM historical_project_rows")==0,"combined preview does not write");
  var result=s.ImportHistoricalProjects(crm,projects,census,source,ch,ph,ih,true,"Fixed synthetic historical fixture review");
  check(result.Outcome=="Imported"&&result.Projects==2&&!result.WorkAuthorized,"combined historical archive commit");
  var rows=s.ReadHistoricalProjects();
  var map=s.ReadLegacyCrmHistory()!.Mappings.Where(x=>x.Kind=="Client").ToDictionary(x=>x.LegacyId,x=>x.NativeId);
  var expected=Expected.Select(x=>new HistoricalArchiveEntry(x.LegacyId,x.LegacyClientId,map[x.LegacyClientId],
   x.LegacyProjectNumber,x.Title,x.Site,x.LegacyStatus,x.LegacyAuthorizationStatus)).ToArray();
  check(rows.SequenceEqual(expected),"combined all historical register values and parent identities");
  using(var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=path,Pooling=false,Mode=SqliteOpenMode.ReadOnly}.ToString()))
  {
   db.Open();using var q=db.CreateCommand();
   q.CommandText="SELECT legacy_id,authorization_evidence_ref FROM historical_project_rows ORDER BY legacy_id";
   using var reader=q.ExecuteReader();
   foreach(var x in Expected)check(reader.Read()&&reader.GetString(0)==x.LegacyId&&reader.GetString(1)==x.AuthorizationEvidenceRef,
    "combined opaque historical evidence retained");
   check(!reader.Read(),"combined exact historical evidence count");
  }
  check(s.ImportHistoricalProjects(crm,projects,census,source,ch,ph,ih,true,"Synthetic replay").Outcome=="AlreadyImported"&&
   Count("SELECT count(*) FROM audit_events WHERE action='CommitHistoricalProjectArchive'")==1,"combined replay creates no additional audit");
  check(Count("SELECT count(*) FROM projects")==0&&Count("SELECT count(*) FROM proposals")==0&&
   Count("SELECT count(*) FROM engagements")==0&&Count("SELECT count(*) FROM invoice_drafts")==0&&
   Count("SELECT count(*) FROM work_submissions")==0&&Count("SELECT next_value FROM sequences WHERE name='Client'")==3,
   "combined archive creates no operational or billing records");
  var recovery=Path.Combine(root,"synthetic-historical-recovery.db");var restored=Path.Combine(root,"synthetic-historical-restored.db");
  var backup=s.CreateRecoveryCopy(recovery);
  var restore=s.RestoreRecoveryCopyToNewFile(recovery,backup.Sha256,restored);
  var copy=new LocalPractice(restored);
  check(restore.Sha256==backup.Sha256&&copy.Login("admin",pw)&&copy.ReadHistoricalProjects().SequenceEqual(expected),
   "combined recovery retains exact historical register");
  check(copy.PreviewHistoricalProjects(crm,projects,census,source,ch,ph,ih).TargetStatus=="AlreadyImported",
   "combined restored archive retains replay evidence");
  copy.Logout();s.Logout();
 }
 public static void RunOffline(string root,Action<bool,string> check)
 {
  var crm=SyntheticCrmNativeTests.Fixture(false);var source=new string('a',64);
  var (projects,census)=Fixture(crm,source);
  var modified=System.Text.Encoding.UTF8.GetBytes(System.Text.Encoding.UTF8.GetString(projects).Replace("Synthetic Alpha Project","Different project"));
  var rebound=System.Text.Encoding.UTF8.GetBytes(System.Text.Encoding.UTF8.GetString(census).Replace(Digest(projects),Digest(modified)));
  bool rejected=false;try{VerifyFixture(crm,modified,rebound,source);}catch(ArgumentException){rejected=true;}
  check(rejected,"combined fixed fixture rejects modified historical values even with rebound digest");
  Run(root,crm,projects,census,source,check);
 }
}
