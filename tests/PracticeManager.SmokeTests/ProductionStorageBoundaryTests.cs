using PracticeManager.Core;
using PracticeManager.Persistence;

internal static class ProductionStorageBoundaryTests
{
 internal static void Run(string parent,Action<bool,string> check)
 {
  var root=Directory.CreateDirectory(Path.Combine(parent,"production-layout-fixture")).FullName;
  var profile=Directory.CreateDirectory(Path.Combine(root,"profile")).FullName;
  var projects=Directory.CreateDirectory(Path.Combine(root,"projects")).FullName;
  var backup=Directory.CreateDirectory(Path.Combine(root,"backup")).FullName;
  void Refuse(Action a,string label){try{a();}catch(ArgumentException){check(true,label);return;}throw new Exception("FAIL: "+label);}
  var layout=ProductionStorageBoundary.Review(profile,projects,backup);
  check(layout.ProgramRoot==Path.Combine(profile,"Programs","CRC Practice Manager"),"production code root fixed independently of data");
  check(layout.DatabasePath==Path.Combine(profile,"CRC Practice Manager DATA","practice.db"),"production database root distinct from DEV");
  check(layout.ProjectRoot==projects&&layout.BackupRoot==backup,"project and recovery choices retained");
  check(!Directory.Exists(layout.ProgramRoot)&&!Directory.Exists(layout.DataRoot),"production path review does not create program or data directory");
  Refuse(()=>ProductionStorageBoundary.Review(profile,projects,projects),"same project and recovery root rejected");
  var nested=Directory.CreateDirectory(Path.Combine(projects,"nested")).FullName;
  Refuse(()=>ProductionStorageBoundary.Review(profile,projects,nested),"nested backup root rejected");
  Refuse(()=>ProductionStorageBoundary.Review(profile,profile,backup),"project root cannot contain application or test stores");
  Refuse(()=>ProductionStorageBoundary.Review(profile,projects,profile),"recovery root cannot contain application or test stores");
  var dev=Directory.CreateDirectory(Path.Combine(profile,"CRC Practice Manager DEV")).FullName;
  Refuse(()=>ProductionStorageBoundary.Review(profile,dev,backup),"development root cannot be selected as production projects");
  var acceptance=Directory.CreateDirectory(Path.Combine(profile,"CRC Practice Manager DOCUMENT ACCEPTANCE")).FullName;
  Refuse(()=>ProductionStorageBoundary.Review(profile,projects,acceptance),"acceptance root cannot be selected as production backup");
  var programParent=Path.GetDirectoryName(layout.ProgramRoot)!;
  File.WriteAllText(programParent,"synthetic collision");
  Refuse(()=>ProductionStorageBoundary.Review(profile,projects,backup),"unexpected file at program parent rejected");
  check(File.ReadAllText(programParent)=="synthetic collision","program parent collision remains unchanged");
  File.Delete(programParent);
  if(OperatingSystem.IsWindows())
  {
   Refuse(()=>layout.VerifyExistingPrivateDataLayout(),"missing production store cannot be silently initialized");
   WorkspaceConfidentiality.EnsureWorkspaceDirectory(layout.DataRoot);
   Refuse(()=>layout.VerifyExistingPrivateDataLayout(),"missing identity marker and database rejected");
   File.WriteAllText(layout.IdentityMarkerPath,"synthetic layout only");
   Refuse(()=>layout.VerifyExistingPrivateDataLayout(),"marker alone cannot qualify missing database");
   File.WriteAllText(layout.DatabasePath,"synthetic bytes not a database");
   layout.VerifyExistingPrivateDataLayout();
   check(File.ReadAllText(layout.DatabasePath)=="synthetic bytes not a database","layout verification does not open or modify database content");
   Directory.CreateDirectory(layout.DatabasePath+"-wal");
   Refuse(()=>layout.VerifyExistingPrivateDataLayout(),"unexpected sidecar directory rejected");
   var profile2=Directory.CreateDirectory(Path.Combine(root,"identity-profile")).FullName;
   var candidate=ProductionStorageBoundary.Review(profile2,projects,backup);
   var id=ProductionWorkspaceIdentityCandidate.InitializeNewFixture(candidate);
   check(Guid.TryParseExact(id,"N",out var parsed)&&parsed!=Guid.Empty,"fresh production candidate assigns unique identity");
   check(ProductionWorkspaceIdentityCandidate.VerifyExisting(candidate)==id,"marker and database identity match read-only");
   var original=File.ReadAllBytes(candidate.DatabasePath);
   var marker=File.ReadAllText(candidate.IdentityMarkerPath);
   Refuse(()=>ProductionWorkspaceIdentityCandidate.InitializeNewFixture(candidate),"existing production location cannot be initialized again");
   check(original.SequenceEqual(File.ReadAllBytes(candidate.DatabasePath)),"repeated initialization preserves original database bytes");
   try{_=new LocalPractice(candidate.DatabasePath);throw new Exception("FAIL: production candidate opened by DEV");}
   catch(InvalidOperationException){check(original.SequenceEqual(File.ReadAllBytes(candidate.DatabasePath)),"development opener refuses production candidate without database mutation");}
   File.WriteAllText(candidate.IdentityMarkerPath,"CRC-PRACTICE-PRODUCTION-V1:"+Guid.NewGuid().ToString("N"));
   Refuse(()=>ProductionWorkspaceIdentityCandidate.VerifyExisting(candidate),"different marker identity refused");
   File.WriteAllText(candidate.IdentityMarkerPath,marker);
   File.Delete(candidate.IdentityMarkerPath);
   Refuse(()=>ProductionWorkspaceIdentityCandidate.VerifyExisting(candidate),"interrupted candidate with missing marker refused");
   Refuse(()=>ProductionWorkspaceIdentityCandidate.InitializeNewFixture(candidate),"interrupted candidate cannot be initialized over");
   File.WriteAllText(candidate.IdentityMarkerPath,marker);
   check(ProductionWorkspaceIdentityCandidate.VerifyExisting(candidate)==id,"restored synthetic marker matches unchanged database");
   File.Delete(candidate.DatabasePath);
   Refuse(()=>ProductionWorkspaceIdentityCandidate.VerifyExisting(candidate),"missing candidate database not reopened");
   check(!File.Exists(candidate.DatabasePath),"missing candidate database remains absent");
   Refuse(()=>ProductionWorkspaceIdentityCandidate.InitializeNewFixture(candidate),"existing marker cannot trigger database replacement");
   var protectedProjects=WorkspaceConfidentiality.EnsureWorkspaceDirectory(Path.Combine(root,"protected-projects"));
   var folders=ProductionStorageBoundary.Review(profile2,protectedProjects,backup);
   var target=ProductionProjectFolderPolicy.ResolveProjectDirectory(folders,"PRJ-000001");
   check(target==Path.Combine(protectedProjects,"PRJ-000001"),"production project path uses assigned stable project number");
   var children=ProductionProjectFolderPolicy.ResolveTemplateDirectories(folders,"PRJ-000001",ProjectFolderTemplate.Defaults);
   check(children.Count==9&&children.Contains(Path.Combine(target,"Structural DWGs"))&&children.Contains(Path.Combine(target,"Plancheck Comment Responses"))&&!children.Contains(Path.Combine(target,"Correspondence")),"production folder plan preserves nine approved defaults and optional correspondence");
   check(!Directory.Exists(target),"folder plan never creates project directory");
   foreach(var invalid in new[]{"PRJ-1","PRJ-000000","PRJ-000001/other","prj-000001","PRJ-0000001","../PRJ-000001"})
    Refuse(()=>ProductionProjectFolderPolicy.ResolveProjectDirectory(folders,invalid),"noncanonical project number refused: "+invalid);
   File.WriteAllText(target,"existing file");
   Refuse(()=>ProductionProjectFolderPolicy.ResolveTemplateDirectories(folders,"PRJ-000001",ProjectFolderTemplate.Defaults),"existing project file collision rejected");
   check(File.ReadAllText(target)=="existing file","project collision left unchanged");
   File.Delete(target);
   WorkspaceConfidentiality.EnsureWorkspaceDirectory(target);
   File.WriteAllText(Path.Combine(target,"Calculations"),"existing child file");
   Refuse(()=>ProductionProjectFolderPolicy.ResolveTemplateDirectories(folders,"PRJ-000001",ProjectFolderTemplate.Defaults),"existing template child file collision rejected before creation");
   check(!Directory.Exists(Path.Combine(target,"Architectural")),"collision review does not create other folders");
   var writerProfile=Directory.CreateDirectory(Path.Combine(root,"folder-writer-profile")).FullName;
   var writerProjects=WorkspaceConfidentiality.EnsureWorkspaceDirectory(Path.Combine(root,"folder-writer-projects"));
   var writer=ProductionStorageBoundary.Review(writerProfile,writerProjects,backup);
   var writerId=ProductionWorkspaceIdentityCandidate.InitializeNewFixture(writer);
   var project=Path.Combine(writerProjects,"PRJ-000001");
   Refuse(()=>ProductionProjectFolderCreationCandidate.CreateFixtureFolders(writer,Guid.NewGuid().ToString("N"),"PRJ-000001",ProjectFolderTemplate.Defaults),"wrong workspace identity refuses folder creation");
   check(!Directory.Exists(project),"wrong workspace leaves project directory absent");
   WorkspaceConfidentiality.EnsureWorkspaceDirectory(project);
   var sentinel=Path.Combine(project,"existing-drawing.txt");
   File.WriteAllText(sentinel,"preserve this");
   Refuse(()=>ProductionProjectFolderCreationCandidate.CreateFixtureFolders(writer,writerId,"PRJ-000001",ProjectFolderTemplate.Defaults),"unmarked existing project directory not adopted");
   check(File.ReadAllText(sentinel)=="preserve this"&&!Directory.Exists(Path.Combine(project,"Calculations")),"unmarked project contents preserved");
   var made=ProductionProjectFolderCreationCandidate.CreateFixtureFolders(writer,writerId,"PRJ-000002",ProjectFolderTemplate.Defaults);
   check(made.Created==9&&made.Existing==0&&Directory.Exists(Path.Combine(made.ProjectDirectory,"Structural DWGs")),"marked project creates nine default directories");
   var drawing=Path.Combine(made.ProjectDirectory,"Structural DWGs","existing-drawing.txt");
   File.WriteAllText(drawing,"retain drawing");
   var again=ProductionProjectFolderCreationCandidate.CreateFixtureFolders(writer,writerId,"PRJ-000002",ProjectFolderTemplate.Defaults);
   check(again.Created==0&&again.Existing==9&&File.ReadAllText(drawing)=="retain drawing","repeat setup idempotent and preserves project files");
   var optional=ProjectFolderTemplate.Defaults.Select(x=>x with{IncludedByDefault=true}).ToArray();
   var added=ProductionProjectFolderCreationCandidate.CreateFixtureFolders(writer,writerId,"PRJ-000002",optional);
   check(added.Created==1&&added.Existing==9&&Directory.Exists(Path.Combine(made.ProjectDirectory,"Correspondence")),"optional correspondence can be added without replacing other folders");
   Refuse(()=>ProductionProjectFolderCreationCandidate.CreateFixtureFolders(writer,writerId,"PRJ-000003",new[]{new ProjectFolderTemplateItem("Other",true)}),"unapproved template label rejected");
   File.WriteAllText(Path.Combine(made.ProjectDirectory,".crc-project-identity"),"wrong identity");
   Refuse(()=>ProductionProjectFolderCreationCandidate.CreateFixtureFolders(writer,writerId,"PRJ-000002",optional),"altered project identity stops setup");
   check(File.ReadAllText(drawing)=="retain drawing","identity failure leaves existing files unchanged");
   var workflowProfile=Directory.CreateDirectory(Path.Combine(root,"workflow-profile")).FullName;
   var workflowProjects=WorkspaceConfidentiality.EnsureWorkspaceDirectory(Path.Combine(root,"workflow-projects"));
   var workflow=ProductionStorageBoundary.Review(workflowProfile,workflowProjects,backup);
   var workflowId=ProductionWorkspaceIdentityCandidate.InitializeNewFixture(workflow);
   Refuse(()=>LocalPractice.OpenProductionFixture(workflow,Guid.NewGuid().ToString("N")),"fixture service rejects wrong workspace identity");
   var service=LocalPractice.OpenProductionFixture(workflow,workflowId);
   const string pw="Synthetic-production-fixture-passphrase-001";
   check(!service.IsInitialized(),"new verified fixture service opens empty base schema");
   service.Bootstrap("Synthetic production workflow","admin",pw,"CL");
   check(service.Login("admin",pw),"fixture service authenticates owner");
   var client=service.SaveClient(new Client(0,"","Business / Company","Synthetic Client","","","","","","","","","","","Active"));
   var proposal=service.CreateProposal(new(client,"Synthetic","Site","Scope",100m));
   var issued=ConflictReviewTests.Issue(service,proposal,1);
   var projectId=service.RecordProposalDecision(proposal,issued,true,"Synthetic").ProjectId!.Value;
   var setup=service.SetupProductionFixtureFolders(projectId,ProjectFolderTemplate.Defaults);
   check(setup.Created==9&&Path.GetFileName(setup.ProjectDirectory)=="PRJ-000001","authenticated project setup derives number from accepted project");
   check(service.SetupProductionFixtureFolders(projectId,ProjectFolderTemplate.Defaults).Existing==9,"authorized retry preserves existing folders");
   Refuse(()=>service.SetupProductionFixtureFolders(long.MaxValue,ProjectFolderTemplate.Defaults),"unknown project cannot create folders");
   using(var sql=new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder{DataSource=workflow.DatabasePath,Pooling=false}.ToString()))
   {
    sql.Open();using var command=sql.CreateCommand();
    command.CommandText="SELECT count(*) FROM audit_events WHERE action='ProductionFixtureFolderSetupCompleted'";
    check(Convert.ToInt64(command.ExecuteScalar())==2,"successful project setup records completion audit");
    command.CommandText="UPDATE role_permissions SET allowed=0 WHERE permission_code='Projects.Setup'";
    command.ExecuteNonQuery();
   }
   try{service.SetupProductionFixtureFolders(projectId,ProjectFolderTemplate.Defaults);throw new Exception("FAIL: revoked setup grant accepted");}
   catch(UnauthorizedAccessException){check(true,"revoked setup grant refuses folder operation");}
   service.Logout();
   try{service.SetupProductionFixtureFolders(projectId,ProjectFolderTemplate.Defaults);throw new Exception("FAIL: unauthenticated setup");}
   catch(UnauthorizedAccessException){check(true,"logged out user cannot create folders");}
   var savedMarker=File.ReadAllText(workflow.IdentityMarkerPath);
   File.WriteAllText(workflow.IdentityMarkerPath,"CRC-PRACTICE-PRODUCTION-V1:"+Guid.NewGuid().ToString("N"));
   Refuse(()=>service.IsInitialized(),"subsequent connection refuses changed workspace marker");
   File.WriteAllText(workflow.IdentityMarkerPath,savedMarker);
   check(LocalPractice.OpenProductionFixture(workflow,workflowId).IsInitialized(),"existing initialized fixture reopens without migration");
   var savedLayout=ProductionWorkspaceIdentityCandidate.ReadStoredLayout(workflowProfile,workflowId);
   check(savedLayout.ProjectRoot==workflowProjects&&savedLayout.BackupRoot==backup,"persisted settings recover reviewed project and backup paths");
   check(LocalPractice.ReopenProductionFixture(workflowProfile,workflowId).IsInitialized(),"normal fixture reopen uses stored paths without directory selection");
   var otherBackup=Directory.CreateDirectory(Path.Combine(root,"other-backup")).FullName;
   var overrideLayout=ProductionStorageBoundary.Review(workflowProfile,workflowProjects,otherBackup);
   Refuse(()=>LocalPractice.OpenProductionFixture(overrideLayout,workflowId),"caller cannot override persisted backup location");
   using(var sql=new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder{DataSource=workflow.DatabasePath,Pooling=false}.ToString()))
   {
    sql.Open();using var command=sql.CreateCommand();
    command.CommandText="UPDATE production_storage_settings SET backup_root=$root WHERE singleton=1";
    command.Parameters.AddWithValue("$root",workflowProjects);command.ExecuteNonQuery();
    Refuse(()=>service.IsInitialized(),"active connection refuses changed settings");
    Refuse(()=>LocalPractice.ReopenProductionFixture(workflowProfile,workflowId),"reopen refuses overlapping stored roots");
    command.Parameters[0].Value=backup;command.ExecuteNonQuery();
   }
   check(LocalPractice.ReopenProductionFixture(workflowProfile,workflowId).IsInitialized(),"restored fixture settings permit reopen");
   // Work only on a closed synthetic copy of the initialized fixture.
   var identityReviewRoot=WorkspaceConfidentiality.EnsureWorkspaceDirectory(Path.Combine(root,"identity-review"));
   var identityReviewPath=Path.Combine(identityReviewRoot,"copy.db");
   var identitySourceBytes=File.ReadAllBytes(workflow.DatabasePath);
   File.Copy(workflow.DatabasePath,identityReviewPath,false);
   using(var identityDb=new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder{DataSource=identityReviewPath,Pooling=false,ForeignKeys=true}.ToString()))
   {
    identityDb.Open();using var command=identityDb.CreateCommand();
    command.CommandText="PRAGMA trusted_schema=OFF";command.ExecuteNonQuery();
    using var identityTx=identityDb.BeginTransaction(deferred:false);
    command.Transaction=identityTx;
    ProductionWorkspaceIdentityCandidate.VerifyMigrationIdentity(identityDb,identityTx,workflowId,workflow,47,false);
    check(true,"schema 47 identity and retained roots qualify for isolated migration review");
    _=EffortBudgetMigrationCandidate.Apply(identityDb,identityTx);
    ProductionWorkspaceIdentityCandidate.VerifyMigrationIdentity(identityDb,identityTx,workflowId,workflow,48,false);
    check(true,"schema 48 migration identity and budget evidence preserved");
    _=DocumentRegisterMigrationCandidate.Apply(identityDb,identityTx);
    ProductionWorkspaceIdentityCandidate.VerifyMigrationIdentity(identityDb,identityTx,workflowId,workflow,49,false);
    check(true,"schema 49 migration identity and document evidence preserved");
    Refuse(()=>ProductionWorkspaceIdentityCandidate.VerifyMigrationIdentity(identityDb,identityTx,workflowId,workflow,48,false),"migration identity refuses wrong expected schema");
    Refuse(()=>ProductionWorkspaceIdentityCandidate.VerifyMigrationIdentity(identityDb,identityTx,Guid.NewGuid().ToString("N"),workflow,49,false),"migration identity refuses foreign workspace");
    Refuse(()=>ProductionWorkspaceIdentityCandidate.VerifyMigrationIdentity(identityDb,identityTx,workflowId,overrideLayout,49,false),"migration identity refuses changed retained roots");
    Refuse(()=>ProductionWorkspaceIdentityCandidate.VerifyConnectionIdentity(identityDb,identityTx,workflowId),"ordinary startup gate still refuses schema 49");
    Refuse(()=>ProductionWorkspaceIdentityCandidate.VerifyMigrationIdentity(identityDb,identityTx,workflowId,workflow,49,true),"live identity cannot qualify as recovery identity");
    command.CommandText="PRAGMA application_id="+ProductionWorkspaceIdentityCandidate.RecoveryApplicationId;command.ExecuteNonQuery();
    ProductionWorkspaceIdentityCandidate.VerifyMigrationIdentity(identityDb,identityTx,workflowId,workflow,49,true);
    check(true,"explicit schema 49 recovery identity review preserves workspace binding");
    Refuse(()=>ProductionWorkspaceIdentityCandidate.VerifyMigrationIdentity(identityDb,identityTx,workflowId,workflow,49,false),"recovery identity cannot qualify as live workspace");
    identityTx.Rollback();
   }
   check(identitySourceBytes.SequenceEqual(File.ReadAllBytes(workflow.DatabasePath)),"identity migration review leaves original fixture bytes unchanged");

   File.Delete(workflow.DatabasePath);
   Refuse(()=>service.IsInitialized(),"missing database not recreated by fixture service");
   check(!File.Exists(workflow.DatabasePath),"fixture service preserves missing database state");
   var offlineProfile=Directory.CreateDirectory(Path.Combine(root,"offline-profile")).FullName;
   var offlineProjects=WorkspaceConfidentiality.EnsureWorkspaceDirectory(Path.Combine(root,"offline-projects"));
   var offlineBackup=WorkspaceConfidentiality.EnsureWorkspaceDirectory(Path.Combine(root,"offline-backup"));
   var offline=ProductionStorageBoundary.Review(offlineProfile,offlineProjects,offlineBackup);
   var offlineId=ProductionWorkspaceIdentityCandidate.InitializeNewFixture(offline);
   check(offline.VerifyBackupDestination()==offlineBackup,"available protected backup destination qualifies for path review");
   Directory.Delete(offlineBackup);
   Refuse(()=>offline.VerifyBackupDestination(),"missing backup destination refused at backup time");
   check(!LocalPractice.ReopenProductionFixture(offlineProfile,offlineId).IsInitialized(),"offline backup does not prevent normal database reopen");
   var offlineMade=ProductionProjectFolderCreationCandidate.CreateFixtureFolders(offline,offlineId,"PRJ-000001",ProjectFolderTemplate.Defaults);
   check(offlineMade.Created==9&&!Directory.Exists(offlineBackup),"offline backup does not prevent project folder creation or cause replacement");
   Refuse(()=>ProductionStorageBoundary.ReviewForDailyUse(offlineProfile,offlineProjects,offlineProjects),"offline policy retains overlap rejection");
   Refuse(()=>ProductionStorageBoundary.ReviewForDailyUse(offlineProfile,offlineProjects,Path.Combine(root,"..","invalid")),"offline policy rejects ambiguous path");
   Refuse(()=>ProductionStorageBoundary.ReviewForDailyUse(offlineProfile,offlineProjects,@"\\server\share\backup"),"offline policy rejects UNC path");
   Refuse(()=>ProductionStorageBoundary.ReviewForDailyUse(offlineProfile,offlineProjects,Path.GetPathRoot(offlineBackup)!),"offline policy rejects drive root");
   File.WriteAllText(offlineBackup,"not a backup directory");
   Refuse(()=>offline.VerifyBackupDestination(),"backup file collision refused");
   check(File.ReadAllText(offlineBackup)=="not a backup directory","backup collision remains unchanged");
   File.Delete(offlineBackup);
   WorkspaceConfidentiality.EnsureWorkspaceDirectory(offlineBackup);
   check(offline.VerifyBackupDestination()==offlineBackup,"restored protected backup path qualifies without settings change");
   // Keep nested acceptance + recovery staging fixture paths within SQLite's
   // ordinary Windows path budget. Long-path support is not assumed.
   var desktopProfile=Directory.CreateDirectory(Path.Combine(parent,"pa")).FullName;
   var session=ProductionAcceptanceStartup.Open(desktopProfile,new[]{"--production-acceptance-new"});
   check(session.Service.IsProductionAcceptance&&!session.Service.IsInitialized(),"explicit acceptance startup creates isolated empty fixture");
   var sessionRoot=Path.Combine(desktopProfile,"CRC Practice Manager PRODUCTION ACCEPTANCE",session.SessionId);
   check(File.Exists(Path.Combine(sessionRoot,"CRC Practice Manager DATA","practice.db"))&&!Directory.Exists(Path.Combine(desktopProfile,"CRC Practice Manager DATA")),"acceptance does not create normal production database");
   session.Service.Bootstrap("Synthetic desktop acceptance","admin",pw,"CL");
   var reopened=ProductionAcceptanceStartup.Open(desktopProfile,new[]{"--production-acceptance-session",session.SessionId});
   check(reopened.Service.IsInitialized()&&reopened.Service.Login("admin",pw),"explicit acceptance reopens original company");
   Refuse(()=>ProductionAcceptanceStartup.Open(desktopProfile,new[]{"--production-acceptance-session",Guid.NewGuid().ToString("N")}),"unknown acceptance session refused without creation");
   Refuse(()=>ProductionAcceptanceStartup.Open(desktopProfile,Array.Empty<string>()),"implicit acceptance startup refused");
   Refuse(()=>ProductionAcceptanceStartup.Open(desktopProfile,new[]{"--production-acceptance-session","../other"}),"invalid acceptance session refused");
   var sessionBackup=Path.Combine(sessionRoot,"synthetic-backups");
   var backupReceipt=reopened.Service.CreateConfiguredAcceptanceBackup();
   check(Path.GetDirectoryName(backupReceipt.Path)==sessionBackup&&File.Exists(backupReceipt.Path),"acceptance backup uses configured directory");
   Refuse(()=>reopened.Service.CreateRecoveryCopy(Path.Combine(root,"outside-backup.db")),"acceptance backup rejects outside destination");
   check(!File.Exists(Path.Combine(root,"outside-backup.db")),"refused backup does not create outside file");
   var restore=reopened.Service.VerifyConfiguredAcceptanceBackup(backupReceipt.Path,backupReceipt.Sha256);
   check(Path.GetDirectoryName(restore.Path)==sessionBackup&&restore.Path!=backupReceipt.Path&&restore.Sha256==backupReceipt.Sha256,"configured isolated restore preserves source and hash");
   try{_=new LocalPractice(restore.Path);throw new Exception("FAIL: recovery copy opened as development");}
   catch(InvalidOperationException){check(true,"recovery-only copy refused by normal opener");}
   Refuse(()=>reopened.Service.VerifyConfiguredAcceptanceBackup(backupReceipt.Path,new string('0',64)),"wrong recovery hash refused");
   var unrelated=ProductionAcceptanceStartup.Open(desktopProfile,new[]{"--production-acceptance-new"});
   unrelated.Service.Bootstrap("Other synthetic firm","admin",pw,"CL");
   check(unrelated.Service.Login("admin",pw),"separate acceptance fixture authenticates");
   var unrelatedBackup=Path.Combine(desktopProfile,"CRC Practice Manager PRODUCTION ACCEPTANCE",unrelated.SessionId,"synthetic-backups");
   var foreignPath=Path.Combine(unrelatedBackup,"foreign.db");
   File.Copy(backupReceipt.Path,foreignPath);
   WorkspaceConfidentiality.ProtectOwnedStagingFile(foreignPath);
   Refuse(()=>unrelated.Service.VerifyConfiguredAcceptanceBackup(foreignPath,backupReceipt.Sha256),"foreign workspace recovery rejected despite matching hash");
   check(reopened.Service.IsInitialized(),"active workspace remains open after recovery validation");
   var sessionMarker=Path.Combine(sessionRoot,"production-acceptance-session.txt");
   File.WriteAllText(sessionMarker,session.SessionId+":"+Guid.NewGuid().ToString("N"));
   Refuse(()=>ProductionAcceptanceStartup.Open(desktopProfile,new[]{"--production-acceptance-session",session.SessionId}),"changed session binding refuses reopen");
  }
 }
}
