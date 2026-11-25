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
