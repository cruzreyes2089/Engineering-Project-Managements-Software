using System.Text.Json;
using Microsoft.Data.Sqlite;
using PracticeManager.Core;
using PracticeManager.Persistence;
internal static class LegacyCrmTests
{
 static byte[] Package(bool orphan=false,bool duplicate=false,bool doublePrimary=false,string phone="8055550100")
 {
  object ClientRow(string id,string number,string status)=>new{legacyId=id,legacyNumber=number,type="Business / Company",company="Synthetic Client",first="",last="",email="",phone,address1="",address2="",city="",state="",zip="",notes="Synthetic notes",status};
  object ContactRow(string id,string parent,bool primary)=>new{legacyId=id,legacyClientId=parent,first="Synthetic",last="Contact",title="",email="",phone="",primary};
  return JsonSerializer.SerializeToUtf8Bytes(new{format="CRC-Legacy-CRM-v1",clients=new[]{ClientRow("A","OLD-1","Active"),ClientRow(duplicate?"a":"B","OLD-1","Inactive")},contacts=new[]{ContactRow("C1",orphan?"Missing":"A",true),ContactRow("C2",doublePrimary?"A":"B",true)}});
 }
 public static void Run(string path,string pw,Action<bool,string> check)
 {
  var data=Package();var s=new LocalPractice(path);s.Bootstrap("Synthetic Migration Firm","admin",pw,"CL");
  void Denied(Action a,string label){try{a();}catch(UnauthorizedAccessException){check(true,label);return;}throw new Exception("FAIL: "+label);}
  void Invalid(Action a,string label){try{a();}catch(ArgumentException){check(true,label);return;}throw new Exception("FAIL: "+label);}
  void Sql(string sql){using var db=new SqliteConnection($"Data Source={path};Pooling=False");db.Open();using var q=db.CreateCommand();q.CommandText=sql;q.ExecuteNonQuery();}
  long Count(string sql){using var db=new SqliteConnection($"Data Source={path};Pooling=False");db.Open();using var q=db.CreateCommand();q.CommandText=sql;return Convert.ToInt64(q.ExecuteScalar());}
  Denied(()=>s.PreviewLegacyCrm(data),"legacy preview requires authenticated administrator");
  check(s.Login("admin",pw),"legacy migration admin logged in");
  Invalid(()=>s.PreviewLegacyCrm(Package(orphan:true)),"orphan legacy contact rejected");
  Invalid(()=>s.PreviewLegacyCrm(Package(duplicate:true)),"case-insensitive duplicate legacy ID rejected");
  Invalid(()=>s.PreviewLegacyCrm(Package(doublePrimary:true)),"multiple primary contacts rejected");
  Invalid(()=>s.PreviewLegacyCrm(Package(phone:"bad")),"invalid legacy phone not silently dropped");
  Invalid(()=>s.PreviewLegacyCrm("{\"format\":\"CRC-Legacy-CRM-v1\",\"clients\":[],\"contacts\":[],\"passwords\":[]}"u8.ToArray()),"unexpected fields rejected");
  var p=s.PreviewLegacyCrm(data);
  check(p.TargetStatus=="ReadyForExplicitReview"&&p.Clients==2&&p.Contacts==2&&p.DuplicateNameGroups==1&&p.DuplicateNumberGroups==1,"legacy preview counts and duplicate warnings");
  check(s.SearchClients("").Count==0,"legacy preview does not write clients");
  Invalid(()=>s.ImportLegacyCrm(data,p.Sha256,false,"Synthetic"),"explicit migration confirmation required");
  Invalid(()=>s.ImportLegacyCrm(data,"wrong",true,"Synthetic"),"exact transfer hash required");
  Sql("CREATE TRIGGER test_abort_contact BEFORE INSERT ON contacts BEGIN SELECT RAISE(ABORT,'Synthetic failure'); END");
  try{s.ImportLegacyCrm(data,p.Sha256,true,"Synthetic");throw new Exception("FAIL: injected failure");}catch(SqliteException){check(true,"legacy contact failure injected");}
  check(Count("SELECT count(*) FROM clients")==0&&Count("SELECT count(*) FROM legacy_crm_batches")==0&&Count("SELECT next_value FROM sequences WHERE name='Client'")==1,"legacy import and numbering roll back together");
  Sql("DROP TRIGGER test_abort_contact");
  var imported=s.ImportLegacyCrm(data,p.Sha256,true,"Synthetic reviewed mapping and counts");
  check(imported.Outcome=="Imported"&&s.SearchClients("").Count==2&&Count("SELECT count(*) FROM contacts")==2,"legacy CRM atomic import");
  check(Count("SELECT count(*) FROM legacy_client_map WHERE legacy_number='OLD-1'")==2&&Count("SELECT count(*) FROM legacy_contact_map")==2,"original IDs and numbers retained without merging");
  check(s.SearchClients("").Count(x=>x.Status=="Inactive")==1&&s.SearchClients("").Count(x=>x.Status=="Active")==1,"legacy client statuses preserved");
  check(s.SearchClients("").All(x=>x.Phone=="(805)555-0100")&&Count("SELECT count(*) FROM clients WHERE phone='(805)555-0100'")==2,"legacy phones match native storage and display format");
  var inactive=s.SearchClients("").Single(x=>x.Status=="Inactive");check(s.Contacts(inactive.Id).Single().Primary,"historical contact for inactive client preserved");
  check(s.PreviewLegacyCrm(data).TargetStatus=="AlreadyImported"&&s.ImportLegacyCrm(data,p.Sha256,true,"Synthetic repeat").Outcome=="AlreadyImported"&&Count("SELECT count(*) FROM clients")==2,"legacy import cannot duplicate rows");
  var history=s.ReadLegacyCrmHistory()!;
  check(history.Clients==2&&history.Contacts==2&&history.Mappings.Count==4&&history.Sha256==p.Sha256,"migration history reconciles imported mapping");
  var second=history.Mappings.Single(x=>x.Kind=="Client"&&x.LegacyId=="B");
  check(history.Mappings.Single(x=>x.Kind=="Contact"&&x.LegacyId=="C2").NativeClientNumber==second.NativeClientNumber,"legacy mapping preserves contact parent relationship");
  Invalid(()=>s.ExportLegacyCrmMapping("wrong"),"mapping export requires exact batch identity");
  var csv=s.ExportLegacyCrmMapping(p.Sha256);
  check(csv.Contains("CONFIDENTIAL")&&csv.Contains("OLD-1")&&csv.Contains("CL-000002")&&!csv.Contains("Synthetic Contact"),"mapping export contains identifiers but no contact names");
  var changed=Package(phone:"8055550101");var p2=s.PreviewLegacyCrm(changed);check(p2.TargetStatus=="DifferentPackageAlreadyImported","changed package blocked");
  Invalid(()=>s.ImportLegacyCrm(changed,p2.Sha256,true,"Synthetic"),"different legacy package cannot merge");
  Sql("UPDATE role_permissions SET allowed=0 WHERE permission_code='Administration.LegacyCrmImport'");Denied(()=>s.PreviewLegacyCrm(data),"revoked migration permission enforced");Denied(()=>s.ReadLegacyCrmHistory(),"revoked mapping history permission enforced");
 }
}
