using PracticeManager.Core;
using PracticeManager.Persistence;
using Microsoft.Data.Sqlite;
if(args.Length==2&&args[0]=="--synthetic-folder-lock-probe")return SyntheticFolderOperationFence.Probe(args[1]);
if(args.Length==2&&args[0]=="--synthetic-folder-crash-worker")return SyntheticFolderCrashTests.Worker(args[1]);
var path = Path.Combine(Path.GetTempPath(), "CRC-test-" + Guid.NewGuid().ToString("N"), "test.db");
Directory.CreateDirectory(Path.GetDirectoryName(path)!);
int passed=0;
void Check(bool condition,string name){if(!condition)throw new Exception("FAIL: "+name); Console.WriteLine("PASS: "+name); passed++;}
void Denied(Action a,string name){try{a();}catch(UnauthorizedAccessException){Check(true,name);return;} throw new Exception("FAIL: "+name);}
void Invalid(Action a,string name){try{a();}catch(ArgumentException){Check(true,name);return;} throw new Exception("FAIL: "+name);}
try{
 if(args.Length==8&&args[0]=="--synthetic-historical-transfer"){
  // The only destination is this run's owned temporary folder. No external database accepted.
  var source=args[1];
  var crm=HistoricalArchivePreviewFiles.Read(args[2],args[3]);
  var projects=HistoricalArchivePreviewFiles.Read(args[4],args[5]);
  var census=HistoricalArchivePreviewFiles.Read(args[6],args[7]);
  try{
   SyntheticHistoricalNativeTests.Run(Path.Combine(Path.GetDirectoryName(path)!,"historical-dao"),crm,projects,census,source,Check);
   Console.WriteLine($"Synthetic historical native checks passed: {passed}; CRM SHA-256: {args[3]}; project SHA-256: {args[5]}; census SHA-256: {args[7]}");
  }finally{
   System.Security.Cryptography.CryptographicOperations.ZeroMemory(crm);
   System.Security.Cryptography.CryptographicOperations.ZeroMemory(projects);
   System.Security.Cryptography.CryptographicOperations.ZeroMemory(census);
  }
  return 0;
 }
 if(args.Length!=0){
  if(args.Length!=4||args[0]!="--synthetic-crm-transfer"||args[1] is not ("populated" or "empty")||
      args[3].Length!=64||args[3].Any(c=>!char.IsAsciiHexDigit(c)))throw new ArgumentException("Invalid synthetic smoke-test arguments.");
  byte[] bytes;
  using(var file=new FileStream(args[2],FileMode.Open,FileAccess.Read,FileShare.Read)){
   if(file.Length is <1 or >LegacyCrmTransfer.MaxBytes)throw new ArgumentException("Synthetic transfer size rejected.");
   bytes=new byte[checked((int)file.Length)];file.ReadExactly(bytes);
   if(file.ReadByte()!=-1)throw new ArgumentException("Synthetic transfer length changed.");
  }
  var digest=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
  if(!string.Equals(digest,args[3],StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Synthetic transfer digest mismatch.");
  SyntheticCrmNativeTests.Run(path,bytes,args[1]=="empty",Check);
  Console.WriteLine($"Synthetic CRM native checks passed: {passed}; transfer SHA-256: {digest.ToLowerInvariant()}");
  return 0;
 }
 DevelopmentWorkspaceTests.Run(Path.GetDirectoryName(path)!,Check);
 var s=new LocalPractice(path);
 using(var db=new SqliteConnection($"Data Source={path}")){db.Open(); using var version=db.CreateCommand(); version.CommandText="SELECT sqlite_version()"; var native=Version.Parse(Convert.ToString(version.ExecuteScalar())!); Console.WriteLine($"SQLite runtime: {native}"); Check(native>=new Version(3,50,2),"native SQLite meets GHSA-2m69-gcr7-jv3q fixed-version floor");}
  const string pw="Synthetic-test-passphrase-0001";
 Check(!s.IsInitialized(),"empty first run");
 Check(!s.CanTestAccountingConnection(),"unauthenticated accounting diagnostic denied");
 Denied(()=>s.SearchClients(""),"unauthenticated list denied");
 Invalid(()=>s.Bootstrap("", "admin",pw,"CL"),"blank firm rejected");
 Check(!s.IsInitialized(),"failed bootstrap leaves no firm");
 s.Bootstrap("Synthetic Firm","admin",pw,"CL");
 Check(s.IsInitialized(),"bootstrap persisted");
 Invalid(()=>s.Bootstrap("Other","admin",pw,"CL"),"repeat bootstrap rejected");
 Check(!s.Login("admin","wrong"),"invalid password denied");
 Check(s.Login("ADMIN",pw),"normalized username login");
 Check(s.CanTestAccountingConnection(),"active administrator can test accounting");
 Check(s.Can("Accounting.Connection.Test"),"dedicated accounting test grant seeded");
 Check(s.Can("Accounting.Connection.Manage"),"dedicated accounting manage grant seeded");
 using(var db=new SqliteConnection($"Data Source={path}")){db.Open();using var q=db.CreateCommand();q.CommandText="UPDATE role_permissions SET allowed=0 WHERE permission_code='Accounting.Connection.Test' AND role_id=1";q.ExecuteNonQuery();}
 Check(!s.CanTestAccountingConnection(),"revoked accounting test grant is enforced");
 using(var db=new SqliteConnection($"Data Source={path}")){db.Open();using var q=db.CreateCommand();q.CommandText="UPDATE role_permissions SET allowed=1 WHERE permission_code='Accounting.Connection.Test' AND role_id=1";q.ExecuteNonQuery();}
 Check(s.CanTestAccountingConnection(),"restored accounting test grant is enforced");

 Check((await s.TestAccountingConnectionAsync("http://example.invalid:12345/api2","synthetic-key")).Outcome=="InvalidEndpoint","service dispatches validated accounting probe");
 using(var db=new SqliteConnection($"Data Source={path}")){db.Open();using var q=db.CreateCommand();q.CommandText="INSERT INTO roles(id,name) VALUES(100,'Employee'); UPDATE users SET role_id=100";q.ExecuteNonQuery();}
 Check(!s.CanTestAccountingConnection(),"nonadministrator accounting diagnostic denied");
 try{await s.TestAccountingConnectionAsync("http://example.invalid:12345/api2","synthetic-key");throw new Exception("FAIL: unauthorized accounting request");}catch(UnauthorizedAccessException){Check(true,"accounting service rejects unauthorized request");}
 using(var db=new SqliteConnection($"Data Source={path}")){db.Open();using var q=db.CreateCommand();q.CommandText="UPDATE users SET role_id=1";q.ExecuteNonQuery();}

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
 Check(!s.CanTestAccountingConnection(),"logout denies accounting diagnostic");
 using(var c=new SqliteConnection($"Data Source={path}")){c.Open(); using var q=c.CreateCommand(); q.CommandText="UPDATE role_permissions SET allowed=0 WHERE permission_code='Clients.View'";q.ExecuteNonQuery();}
 Check(s.Login("admin",pw),"login after permission change"); Denied(()=>s.SearchClients(""),"revoked permission denies administrator");
 s.Logout(); using(var c=new SqliteConnection($"Data Source={path}")){c.Open(); using var q=c.CreateCommand();q.CommandText="UPDATE users SET active=0";q.ExecuteNonQuery();}
 Check(!s.Login("admin",pw),"inactive user denied");
 Check(PasswordCodec.Verify(pw,PasswordCodec.Hash(pw)),"password hash roundtrip"); Check(!PasswordCodec.Verify(pw,"invalid"),"bad hash denied");
 await AccountingProbeTests.Run(Check);
 var legacyPath=Path.Combine(Path.GetDirectoryName(path)!,"legacy-v1.db");
 using(var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=legacyPath,Pooling=false}.ToString())){
  db.Open();using var q=db.CreateCommand();using var schema=typeof(LocalPractice).Assembly.GetManifestResourceStream("PracticeManager.schema.sql")!;using var reader=new StreamReader(schema);
  q.CommandText=reader.ReadToEnd();q.ExecuteNonQuery();
  q.CommandText="INSERT INTO firms VALUES(1,'Migration Test','CL'); INSERT INTO roles VALUES(1,'Administrator'),(2,'Employee'); INSERT INTO permissions VALUES('Clients.View'); INSERT INTO role_permissions VALUES(1,'Clients.View',1); INSERT INTO users(id,username,normalized_username,password_hash,role_id,active) VALUES(1,'admin','ADMIN',$h,1,1),(2,'employee','EMPLOYEE',$h,2,1); INSERT INTO clients(client_no,client_type,company_name) VALUES('CL-000001','Business / Company','Migration Test Client');";
  q.Parameters.AddWithValue("$h",PasswordCodec.Hash(pw));q.ExecuteNonQuery();
 }
 UpgradeRehearsalTests.Run(legacyPath,pw,Check);
 var migrated=new LocalPractice(legacyPath);
 Check(migrated.Login("admin",pw),"v1 upgrade preserves administrator login");
 Check(migrated.SearchClients("").Count==1,"v1 upgrade preserves client records");
 Check(migrated.CanTestAccountingConnection()&&migrated.CanManageAccountingConnection(),"v1 upgrade seeds accounting grants");
 Check(migrated.Can("Projects.Closeout"),"v1 upgrade seeds administrator lifecycle grant");
 Check(migrated.Login("employee",pw),"v1 upgrade preserves employee login");
 Check(!migrated.CanTestAccountingConnection()&&!migrated.CanManageAccountingConnection(),"v1 upgrade does not grant employees accounting access");
 Check(!migrated.Can("Projects.Closeout"),"v1 upgrade leaves employee lifecycle grant disabled");
 using(var db=new SqliteConnection($"Data Source={legacyPath}")){db.Open();using var q=db.CreateCommand();q.CommandText="PRAGMA user_version";Check(Convert.ToInt32(q.ExecuteScalar())==54,"v1 upgrade advances schema version");q.CommandText="UPDATE role_permissions SET allowed=0 WHERE role_id=1 AND permission_code='Accounting.Connection.Test'";q.ExecuteNonQuery();}
 migrated=new LocalPractice(legacyPath);
 Check(migrated.Login("admin",pw)&&!migrated.CanTestAccountingConnection(),"reopen preserves revoked permission");

 await AccountingProfileTests.Run(migrated,legacyPath,pw,Check);
 await AccountingCustomerPageTests.Run(Check);
 await AccountingLinkTests.Run(legacyPath,pw,Check);
 await AccountingDiscoveryTests.Run(legacyPath,pw,Check);
 await AccountingAccountTests.Run(legacyPath,pw,Check);
 await AccountingMarkerTests.Run(Path.Combine(Path.GetDirectoryName(path)!,"marker.db"),pw,Check);
 await AccountingFinancialTests.Run(Path.Combine(Path.GetDirectoryName(path)!,"financial.db"),pw,Check);
 await AccountingDirectoryTests.Run(Path.Combine(Path.GetDirectoryName(path)!,"directory.db"),pw,Check);
 await AccountingDetailTests.Run(Path.Combine(Path.GetDirectoryName(path)!,"detail.db"),pw,Check);
 TestLedgerReconciliationTests.Run(Check);
  SyntheticReceiptCapacityTests.Run(Check);
 await SyntheticConcurrentReceiptTests.Run(Check);
 await SyntheticAccountingRestoreTests.Run(Path.GetDirectoryName(path)!,pw,Check);
 SyntheticRecoveryReviewTests.Run(Check);
 SyntheticWireCompilerTests.Run(Check);
 SyntheticWireReadbackTests.Run(Check);
 SyntheticDispatchReviewTests.Run(Check);
 await SyntheticAttemptWitnessTests.Run(Path.GetDirectoryName(path)!,Check);
 await SyntheticPostingPipelineTests.Run(Path.GetDirectoryName(path)!,pw,Check);
 SyntheticDetailValidatorTests.Run(Check);
 await SyntheticAcquisitionTests.Run(Check);
 await NativeWireFixtureReviewTests.Run(Check);
 await CollectionsReviewTests.Run(Check);
 CollectionDueAgingTests.Run(Check);
 SyntheticInvoiceSettlementTests.Run(Check);
 SyntheticAdvanceTests.Run(Check);
 SyntheticFinancialCorrectionTests.Run(Check);
 await DueFixtureTests.Run(Path.Combine(Path.GetDirectoryName(path)!,"due-fixture.db"),pw,Check);
 await PostingRehearsalTests.Run(Path.Combine(Path.GetDirectoryName(path)!,"rehearsal.db"),pw,Check);
 await SyntheticWireDraftTests.Run(Path.Combine(Path.GetDirectoryName(path)!,"wire-draft.db"),pw,Check);
 await SyntheticWireAttemptTests.Run(Path.GetDirectoryName(path)!,pw,Check);
 await EngagementTests.Run(Path.Combine(Path.GetDirectoryName(path)!,"engagement.db"),pw,Check);
 await ProjectHandoffTests.Run(Path.Combine(Path.GetDirectoryName(path)!,"project-handoff.db"),pw,Check);
 StaffAdministrationTests.Run(Path.Combine(Path.GetDirectoryName(path)!,"staff.db"),pw,Check);
 WorkPlanningTests.Run(Path.Combine(Path.GetDirectoryName(path)!,"work-planning.db"),pw,Check);
 await WorkEntriesTests.Run(Path.Combine(Path.GetDirectoryName(path)!,"work-entries.db"),pw,Check);
 ProjectEvidenceTests.Run(Path.Combine(Path.GetDirectoryName(path)!,"project-evidence.db"),pw,Check);
  SyntheticFolderProvisioningTests.Run(Path.GetDirectoryName(path)!,Check);
  SyntheticFolderJournalTests.Run(Path.GetDirectoryName(path)!,Check);
  SyntheticNestedFolderTests.Run(Path.GetDirectoryName(path)!,Check);
  SyntheticFolderBackupTests.Run(Path.GetDirectoryName(path)!,Check);
  SyntheticFolderCrashTests.Run(Check);
  FirmStoragePreferenceTests.Run(Path.GetDirectoryName(path)!,pw,Check);
  FirmFolderTemplateTests.Run(Path.GetDirectoryName(path)!,pw,Check);
  NamedFolderTemplateTests.Run(Path.GetDirectoryName(path)!,pw,Check);
  PreferredFolderTemplateTests.Run(Path.GetDirectoryName(path)!,pw,Check);
  ProjectStorageBindingTests.Run(Path.GetDirectoryName(path)!,pw,Check);
  ProjectStorageReadinessTests.Run(Path.GetDirectoryName(path)!,pw,Check);
  if(OperatingSystem.IsWindows())SyntheticWindowsStorageAclTests.Run(Path.GetDirectoryName(path)!,Check);
  else Console.WriteLine("NOT QUALIFIED: Windows storage ACL checks unavailable.");
 await InvoiceDraftTests.Run(Path.Combine(Path.GetDirectoryName(path)!,"invoice-drafts.db"),pw,Check);
 RecoveryCopyTests.Run(Path.Combine(Path.GetDirectoryName(path)!,"recovery-source.db"),pw,Check);
 OwnerApprovalTests.Run(Path.Combine(Path.GetDirectoryName(path)!,"owner-approval.db"),pw,Check);
 ConflictReviewTests.Run(Path.Combine(Path.GetDirectoryName(path)!,"conflict-review.db"),pw,Check);
 ReviewDocxTests.Run(Check);
 LegacyCrmTests.Run(Path.Combine(Path.GetDirectoryName(path)!,"legacy-crm.db"),pw,Check);
 HistoricalProjectTransferTests.Run(Check);
  HistoricalArchivePreviewFileTests.Run(Path.Combine(Path.GetDirectoryName(path)!,"historical-preview-files"),Check);
  HistoricalArchiveTests.Run(Path.Combine(Path.GetDirectoryName(path)!,"historical-archive"),pw,Check);
  SyntheticHistoricalNativeTests.RunOffline(Path.Combine(Path.GetDirectoryName(path)!,"combined-historical"),Check);
 SyntheticCrmNativeTests.Run(Path.Combine(Path.GetDirectoryName(path)!,"synthetic-dao-populated.db"),SyntheticCrmNativeTests.Fixture(false),false,Check);
 SyntheticCrmNativeTests.Run(Path.Combine(Path.GetDirectoryName(path)!,"synthetic-dao-empty.db"),SyntheticCrmNativeTests.Fixture(true),true,Check);
 Console.WriteLine($"{passed} checks passed"); return 0;
}catch(Exception e){if(args.Length==0)Console.Error.WriteLine(e);else Console.Error.WriteLine("Synthetic native qualification rejected or failed."); return 1;}finally{SqliteConnection.ClearAllPools();Directory.Delete(Path.GetDirectoryName(path)!,true);}
