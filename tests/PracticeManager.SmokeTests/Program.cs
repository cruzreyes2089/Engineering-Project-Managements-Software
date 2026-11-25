using PracticeManager.Core;
using PracticeManager.Persistence;
using Microsoft.Data.Sqlite;
var path = Path.Combine(Path.GetTempPath(), "CRC-test-" + Guid.NewGuid().ToString("N"), "test.db");
Directory.CreateDirectory(Path.GetDirectoryName(path)!);
int passed=0;
void Check(bool condition,string name){if(!condition)throw new Exception("FAIL: "+name); Console.WriteLine("PASS: "+name); passed++;}
void Denied(Action a,string name){try{a();}catch(UnauthorizedAccessException){Check(true,name);return;} throw new Exception("FAIL: "+name);}
void Invalid(Action a,string name){try{a();}catch(ArgumentException){Check(true,name);return;} throw new Exception("FAIL: "+name);}
try{
 var s=new LocalPractice(path);
 using(var db=new SqliteConnection($"Data Source={path}")){db.Open(); using var version=db.CreateCommand(); version.CommandText="SELECT sqlite_version()"; var native=Version.Parse(Convert.ToString(version.ExecuteScalar())!); Console.WriteLine($"SQLite runtime: {native}"); Check(native>=new Version(3,50,2),"native SQLite meets GHSA-2m69-gcr7-jv3q fixed-version floor");}
  const string pw="Synthetic-test-passphrase-0001";
 Check(!s.IsInitialized(),"empty first run");
 Denied(()=>s.SearchClients(""),"unauthenticated list denied");
 Invalid(()=>s.Bootstrap("", "admin",pw,"CL"),"blank firm rejected");
 Check(!s.IsInitialized(),"failed bootstrap leaves no firm");
 s.Bootstrap("Synthetic Firm","admin",pw,"CL");
 Check(s.IsInitialized(),"bootstrap persisted");
 Invalid(()=>s.Bootstrap("Other","admin",pw,"CL"),"repeat bootstrap rejected");
 Check(!s.Login("admin","wrong"),"invalid password denied");
 Check(s.Login("ADMIN",pw),"normalized username login");
 Check(s.SearchClients("").Count==0,"empty register");
 var id=s.SaveClient(new Client(0,"","Business / Company","Synthetic Client","","","","","","","","","","","Active"));
 var list=s.SearchClients("Synthetic"); Check(list.Count==1 && list[0].Id==id && list[0].Number=="CL-000001","client create and search");
 Check(s.SearchClients("' OR 1=1 --").Count==0,"parameterized search");
 var cid=s.SaveContact(new Contact(0,id,"A","Person","","","",true));
 var cid2=s.SaveContact(new Contact(0,id,"B","Person","","","",true));
 Check(s.Contacts(id).Count==2 && s.Contacts(id).Single(x=>x.Primary).Id==cid2,"primary replacement");
 s.SaveClient(list[0] with {Phone="8055550101"});
 Check(s.SearchClients("Synthetic").Single().Phone=="(805)555-0101","client phone normalized on edit");
 s.SaveContact(s.Contacts(id).Single(x=>x.Id==cid) with {Phone="805.555.0102"});
 Check(s.Contacts(id).Single(x=>x.Id==cid).Phone=="(805)555-0102","contact phone normalized on edit");
 var phoneClient=s.SearchClients("Synthetic").Single();
 var phoneContact=s.Contacts(id).Single(x=>x.Id==cid);
 s.SaveClient(phoneClient with {Phone="+1 (805) 555-0103"});
 Check(s.SearchClients("Synthetic").Single().Phone=="(805)555-0103","country code one normalized");
 s.SaveContact(phoneContact with {Phone=""});
 Check(s.Contacts(id).Single(x=>x.Id==cid).Phone=="","optional contact phone");
 s.SaveClient(phoneClient with {Phone=""});
 Check(s.SearchClients("Synthetic").Single().Phone=="","optional client phone");
 Invalid(()=>s.SaveClient(phoneClient with {Phone="80555501"}),"incomplete client phone rejected");
 Invalid(()=>s.SaveContact(phoneContact with {Phone="abc8055550102"}),"invalid contact phone rejected");
 using(var db=new SqliteConnection($"Data Source={path}")){db.Open();using var q=db.CreateCommand();q.CommandText="UPDATE clients SET phone='8055550104' WHERE id=$id";q.Parameters.AddWithValue("$id",id);q.ExecuteNonQuery();q.CommandText="UPDATE contacts SET phone='8055550105' WHERE id=$id";q.Parameters["$id"].Value=cid;q.ExecuteNonQuery();}
 Check(s.SearchClients("Synthetic").Single().Phone=="(805)555-0104","existing unformatted client phone displayed");
 Check(s.Contacts(id).Single(x=>x.Id==cid).Phone=="(805)555-0105","existing unformatted contact phone displayed");
 Check(Validation.Phone("(805)555-0106")=="(805)555-0106","canonical phone is idempotent");
 Check(Validation.PhoneForDisplay("+44 20 7946 0958")=="+44 20 7946 0958","unsupported legacy phone preserved");
 Invalid(()=>Validation.Phone("+44 20 7946 0958"),"international phone rejected rather than truncated");
 Invalid(()=>Validation.Phone("+8055550106"),"unsupported plus prefix rejected");
 var newPhoneClient=s.SaveClient(phoneClient with {Id=0,Number="",Company="Synthetic Other",Phone="805-555-0107"});
 var newPhoneContact=s.SaveContact(new Contact(0,newPhoneClient,"Synthetic","Contact","","","805 555 0108",false));
 using(var db=new SqliteConnection($"Data Source={path}")){db.Open();using var q=db.CreateCommand();q.CommandText="SELECT phone FROM clients WHERE id=$id";q.Parameters.AddWithValue("$id",newPhoneClient);Check((string?)q.ExecuteScalar()=="(805)555-0107","client creation stores canonical phone");q.CommandText="SELECT phone FROM contacts WHERE id=$id";q.Parameters["$id"].Value=newPhoneContact;Check((string?)q.ExecuteScalar()=="(805)555-0108","contact creation stores canonical phone");}

 s.SaveClient(list[0] with {Status="Inactive"});
 Invalid(()=>s.SaveContact(new Contact(0,id,"C","Person","","","",false)),"inactive client contact guard");
 s.Logout(); Denied(()=>s.Contacts(id),"logout clears session");
 using(var c=new SqliteConnection($"Data Source={path}")){c.Open(); using var q=c.CreateCommand(); q.CommandText="UPDATE role_permissions SET allowed=0 WHERE permission_code='Clients.View'";q.ExecuteNonQuery();}
 Check(s.Login("admin",pw),"login after permission change"); Denied(()=>s.SearchClients(""),"revoked permission denies administrator");
 s.Logout(); using(var c=new SqliteConnection($"Data Source={path}")){c.Open(); using var q=c.CreateCommand();q.CommandText="UPDATE users SET active=0";q.ExecuteNonQuery();}
 Check(!s.Login("admin",pw),"inactive user denied");
 Check(PasswordCodec.Verify(pw,PasswordCodec.Hash(pw)),"password hash roundtrip"); Check(!PasswordCodec.Verify(pw,"invalid"),"bad hash denied");
 Console.WriteLine($"{passed} checks passed"); return 0;
}catch(Exception e){Console.Error.WriteLine(e); return 1;}finally{SqliteConnection.ClearAllPools();Directory.Delete(Path.GetDirectoryName(path)!,true);}
