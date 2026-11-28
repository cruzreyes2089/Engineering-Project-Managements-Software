using Microsoft.Data.Sqlite;
using PracticeManager.Core;
using PracticeManager.Persistence;
internal static class ProjectEvidenceTests
{
 public static void Run(string path,string pw,Action<bool,string> check)
 {
  var s=new LocalPractice(path);s.Bootstrap("Synthetic Evidence Firm","admin",pw,"CL");check(s.Login("admin",pw),"project evidence admin login");
  void Invalid(Action a,string label){try{a();}catch(ArgumentException){check(true,label);return;}throw new Exception("FAIL: "+label);}
  void Denied(Action a,string label){try{a();}catch(UnauthorizedAccessException){check(true,label);return;}throw new Exception("FAIL: "+label);}
  void Sql(string sql){using var db=new SqliteConnection($"Data Source={path}");db.Open();using var q=db.CreateCommand();q.CommandText=sql;q.ExecuteNonQuery();}
  var client=s.SaveClient(new Client(0,"","Business / Company","Synthetic Client","","","","","","","","","","","Active"));
  (long Id,string Hash) Project(string title){var p=s.CreateProposal(new(client,title,"Site","Scope",100m));var h=ConflictReviewTests.Issue(s,p,1);return(s.RecordProposalDecision(p,h,true,"Synthetic").ProjectId!.Value,h);}
  var p=Project("First");var other=Project("Other");var bytes="Synthetic document"u8.ToArray();var key=Guid.NewGuid();
  Invalid(()=>s.RegisterProjectDocument(p.Id,Guid.NewGuid(),null,"Production","Reports","../report.txt",bytes),"document traversal rejected");
  Invalid(()=>s.RegisterProjectDocument(p.Id,Guid.NewGuid(),null,"Production","Reports","CON.txt",bytes),"reserved name rejected");
  foreach(var name in new[]{"CONIN$.txt","conout$.pdf","NUL .dwg","COM¹ .txt","LPT2 .pdf"})
   Invalid(()=>s.RegisterProjectDocument(p.Id,Guid.NewGuid(),null,"Production","Reports",name,bytes),"document register rejects reserved device alias "+name);
  check(s.ReadProjectDocuments(p.Id,false).Count==0,"rejected filenames leave document register unchanged");
  Invalid(()=>s.RegisterProjectDocument(p.Id,Guid.NewGuid(),null,"Production","Reports","program.exe",bytes),"executable document rejected");
  var id=s.RegisterProjectDocument(p.Id,key,null,"Production","Reports","report.txt",bytes);
  check(s.RegisterProjectDocument(p.Id,key,null,"Production","Reports","report.txt",bytes)==id,"document idempotent by operation and hash");
  Invalid(()=>s.RegisterProjectDocument(p.Id,key,null,"Production","Reports","report.txt","Changed"u8.ToArray()),"document operation conflict");
  var old=s.ReadProjectDocuments(p.Id).Single();
  var successor=s.RegisterProjectDocument(p.Id,Guid.NewGuid(),id,"Production","Reports","report.txt","Revision"u8.ToArray());
  check(s.ReadProjectDocuments(p.Id).Single().Id==successor&&s.ReadProjectDocuments(p.Id,false).Count==2,"current document and history separate");
  check(s.ReadProjectDocuments(p.Id,true,"REPORT","Reports").Single().Id==successor&&s.ReadProjectDocuments(p.Id,false,"report","Reports").Count==2,"document search intersects category and current version filter");
  check(s.ReadProjectDocuments(p.Id,false,"report","Drawings").Count==0&&s.ReadProjectDocuments(other.Id,false,"report").Count==0,"document search remains category and project scoped");
  Invalid(()=>s.ReadProjectDocuments(p.Id,false,"","Unknown"),"document search rejects invalid category");
  Invalid(()=>s.RegisterProjectDocument(p.Id,Guid.NewGuid(),id,"Production","Reports","report.txt",bytes),"stale version cannot branch");
  Invalid(()=>s.RegisterProjectDocument(other.Id,Guid.NewGuid(),successor,"Production","Reports","report.txt",bytes),"cross-project version denied");
  var restricted=s.RegisterProjectDocument(p.Id,Guid.NewGuid(),null,"Restricted","Contracts","agreement.txt",bytes);
  var rd=s.ReadProjectDocuments(p.Id).Single(x=>x.Id==restricted);
  check(s.RetrieveProjectDocument(p.Id,id,old.Hash).Bytes.SequenceEqual(bytes),"old immutable document verified");
  Denied(()=>s.RetrieveProjectDocument(other.Id,id,old.Hash),"cross-project retrieval denied");
  Sql("UPDATE role_permissions SET allowed=0 WHERE permission_code='Documents.Restricted'");
  check(s.ReadProjectDocuments(p.Id,false).All(x=>x.Id!=restricted),"restricted document omitted from list");
  check(s.ReadProjectDocuments(p.Id,false,"agreement","Contracts").Count==0,"document search cannot reveal restricted match");
  check(s.CanRegisterProjectDocuments(p.Id),"document registration preflight allows production writer");
  Sql("UPDATE role_permissions SET allowed=0 WHERE permission_code=\u0027Documents.Record\u0027");
  check(!s.CanRegisterProjectDocuments(p.Id)&&s.ReadProjectDocuments(p.Id).Count==1,"read-only user can list but cannot register documents");
  Denied(()=>s.RegisterProjectDocument(p.Id,Guid.NewGuid(),null,"Production","Reports","denied.txt",bytes),"registration service denies revoked write grant");
  Sql("UPDATE role_permissions SET allowed=1 WHERE permission_code=\u0027Documents.Record\u0027");
  Denied(()=>s.RetrieveProjectDocument(p.Id,restricted,rd.Hash),"restricted document direct retrieval denied");
  Denied(()=>s.RegisterProjectDocument(p.Id,Guid.NewGuid(),null,"Restricted","Contracts","secret.txt",bytes),"restricted write denied");
  Invalid(()=>s.RecordProjectLifecycle(p.Id,null,true,"Synthetic pending closure",true),"pending project cannot close");
  var before=s.ReadProjectCloseoutReview(p.Id);check(before.ProjectState=="Pending"&&before.Phases==0&&before.OpenTasks==0&&before.OpenQualityItems==0&&before.PendingScopeDecisions==0,"closeout review describes empty pending project without authorizing closure");
  var qualityId=s.CreateQualityItem(p.Id,"Synthetic QA","Correction","Review calculation");s.AddQualityNote(p.Id,qualityId,"Synthetic review note");
  check(s.ReadQualityNotes(p.Id,qualityId).Count==1&&s.ReadQualityItems(p.Id).Single().State=="Open","quality history retained");
  check(s.ReadProjectCloseoutReview(p.Id).OpenQualityItems==1&&s.ReadProjectCloseoutReview(other.Id).OpenQualityItems==0,"closeout QA count is project scoped");
  Invalid(()=>s.AddQualityNote(other.Id,qualityId,"wrong project"),"quality scope enforced");
  check(s.CloseQualityItem(p.Id,qualityId,"Resolved")=="Closed"&&s.CloseQualityItem(p.Id,qualityId,"Changed")=="AlreadyClosed","quality closure immutable");
  Invalid(()=>s.AddQualityNote(p.Id,qualityId,"After closure"),"closed quality history read-only");
  var change=s.ProposeScopeChange(p.Id,"Extra work","Synthetic additional scope");
  check(s.ReadProjectCloseoutReview(p.Id).OpenQualityItems==0&&s.ReadProjectCloseoutReview(p.Id).PendingScopeDecisions==1,"closeout counts unresolved scope but excludes closed QA");
  Sql("UPDATE role_permissions SET allowed=0 WHERE permission_code='Reports.Operations'");
  Denied(()=>s.ReadProjectCloseoutReview(p.Id),"closeout report requires operations grant");
  Sql("UPDATE role_permissions SET allowed=1 WHERE permission_code='Reports.Operations'");
  Invalid(()=>s.DecideScopeChange(p.Id,change,true,"Reviewed",true),"pending project cannot authorize additional work");
  var a=s.RecordSignedAgreement(p.Id,p.Hash,"signed.pdf","%PDF-1.7\nSynthetic\n%%EOF"u8.ToArray(),"Reviewed",true);ConflictReviewTests.Clear(s,p.Id);s.AuthorizeProjectStart(p.Id,p.Hash,a.Id,a.PdfHash,true,"Synthetic");
  Invalid(()=>s.DecideScopeChange(p.Id,change,true,"Reviewed",false),"explicit client review required");
  check(s.DecideScopeChange(p.Id,change,true,"Written synthetic approval",true)=="Authorized"&&s.ReadScopeChanges(p.Id).Single().State=="Authorized","scope approval recorded separately");
  check(s.DecideScopeChange(p.Id,change,false,"Changed",false)=="AlreadyAuthorized","scope authorization not silently reversed");
  var after=s.ReadProjectCloseoutReview(p.Id);check(after.ProjectState=="Active"&&after.PendingScopeDecisions==0&&after.ConflictStatus=="Clear","closeout reflects resolved scope and current project state");
  Invalid(()=>s.ReadProjectCloseoutReview(long.MaxValue),"closeout rejects nonexistent project");
  var pdf="%PDF-1.7\nSynthetic review only\n%%EOF"u8.ToArray();
  var pdfId=s.RegisterProjectDocument(p.Id,Guid.NewGuid(),null,"Production","Drawings","drawing.pdf",pdf);
  var pdfRow=s.ReadProjectDocuments(p.Id).Single(x=>x.Id==pdfId);
  var pick=new[]{new ReleaseReviewSelection(pdfId,pdfRow.Hash)};
  Invalid(()=>s.PrepareProjectReleaseReview(p.Id,pick,"Review","Synthetic recipient","Review",false),"release review confirmation required");
  Invalid(()=>s.PrepareProjectReleaseReview(p.Id,new[]{new ReleaseReviewSelection(successor,s.ReadProjectDocuments(p.Id).Single(x=>x.Id==successor).Hash)},"Review","Synthetic recipient","Review",true),"release review rejects non-PDF");
  Denied(()=>s.PrepareProjectReleaseReview(p.Id,new[]{new ReleaseReviewSelection(restricted,rd.Hash)},"Review","Synthetic recipient","Review",true),"release review refuses restricted content");
  Invalid(()=>s.PrepareProjectReleaseReview(other.Id,pick,"Review","Synthetic recipient","Review",true),"pending other project cannot prepare release review");

  var qa=s.CreateQualityItem(p.Id,"Synthetic release QA","Correction","Review pending");
  Invalid(()=>s.PrepareProjectReleaseReview(p.Id,pick,"Review","Synthetic recipient","Review",true),"open QA blocks release review");
  s.CloseQualityItem(p.Id,qa,"Resolved");
  var package=s.PrepareProjectReleaseReview(p.Id,pick,"Review","Synthetic recipient","Reviewed",true);
  using(var memory=new MemoryStream(package.Bytes))
  using(var zip=new System.IO.Compression.ZipArchive(memory,System.IO.Compression.ZipArchiveMode.Read))
  {
   check(zip.Entries.Count==2&&zip.GetEntry("REVIEW-ONLY.json")!=null,"review package contains only selected PDF and internal manifest");
   using var entry=zip.GetEntry("document-"+pdfId+".pdf")!.Open();using var data=new MemoryStream();entry.CopyTo(data);
   check(data.ToArray().SequenceEqual(pdf),"review package contains exact registered PDF bytes");
  }
  var dest=Path.Combine(Path.GetDirectoryName(path)!,"synthetic-release-review.zip");
  VerifiedDocumentFile.Save(dest,".zip",package.Bytes,package.Sha256);
  check(File.ReadAllBytes(dest).SequenceEqual(package.Bytes),"verified local save publishes exact package");
  Invalid(()=>VerifiedDocumentFile.Save(dest,".zip",package.Bytes,package.Sha256),"verified save refuses overwrite");
  Invalid(()=>VerifiedDocumentFile.Save(Path.Combine(Path.GetDirectoryName(path)!,"wrong-hash.zip"),".zip",package.Bytes,new string('0',64)),"verified save rejects mismatched bytes");
  if(OperatingSystem.IsWindows())check(WorkspaceConfidentiality.HasRestrictedFileAcl(dest),"verified document copy retains restricted ACL");
  var newPdf=s.RegisterProjectDocument(p.Id,Guid.NewGuid(),pdfId,"Production","Drawings","drawing.pdf","%PDF-1.7\nSynthetic revised\n%%EOF"u8.ToArray());
  Invalid(()=>s.PrepareProjectReleaseReview(p.Id,pick,"Review","Synthetic recipient","Review",true),"superseded document cannot enter release review");
  check(!Directory.EnumerateDirectories(Path.GetDirectoryName(path)!,".crc-recovery-stage-*").Any(),"verified document staging cleaned");
  Denied(()=>s.ReadCandidateFolderConfiguration(),"ordinary database cannot access inactive document register");
  // Actual internal additive migrations, on this disposable fixture only.
  using(var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=path,ForeignKeys=true,Pooling=false}.ToString()))
  {
   db.Open();
   using(var t=db.BeginTransaction(deferred:false)){_=EffortBudgetMigrationCandidate.Apply(db,t);t.Commit();}
   try
   {
    using var bad=db.BeginTransaction(deferred:false);
    var prior=DocumentRegisterMigrationCandidate.Apply(db,bad);
    using(var q=db.CreateCommand()){q.Transaction=bad;q.CommandText="UPDATE users SET failed_attempts=failed_attempts+1";q.ExecuteNonQuery();}
    DocumentRegisterMigrationCandidate.VerifyPreservation(db,bad,prior);
    throw new Exception("FAIL: changed migration evidence");
   }
   catch(InvalidOperationException){check(true,"document migration witness rejects altered pre-existing user row");}
   using(var q=db.CreateCommand()){q.CommandText="SELECT (SELECT user_version FROM pragma_user_version)=48 AND NOT EXISTS(SELECT 1 FROM sqlite_schema WHERE name='project_folder_configurations')";check(Convert.ToInt64(q.ExecuteScalar())==1,"failed document migration rolls back schema and version");}
   using(var t=db.BeginTransaction(deferred:false)){check(DocumentRegisterMigrationCandidate.Apply(db,t).Length==64,"document additive migration preserves full prior schema and rows");t.Commit();}
  }
  var syntheticRoot=Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(path)!)!,"crc-document-folder-fixture-"+Guid.NewGuid().ToString("N"));
  var syntheticProject=Path.Combine(syntheticRoot,"Synthetic Project");
  Directory.CreateDirectory(syntheticProject);
  try
  {
   var config=s.RecordCandidateFolderConfiguration(Guid.NewGuid(),null,syntheticRoot,ProjectFolderTemplate.Defaults,"Synthetic root",true).Entry;
   var folder=s.RecordCandidateProjectFolder(p.Id,Guid.NewGuid(),null,config.Hash,"Registered","Synthetic Project","Synthetic review",true).Entry;
   check(s.ReadCandidateProjectFolders(p.Id).Single()==folder&&folder.DisplayPath==syntheticProject,"actual project folder service binds reviewed root and exact path");
  }
  finally{Directory.Delete(syntheticProject,false);Directory.Delete(syntheticRoot,false);}
  var currentPdf=s.ReadProjectDocuments(p.Id).Single(x=>x.Id==newPdf);
  var intent=DocumentTransmittalReview.Prepare(p.Id,"Synthetic recipient","Synthetic issue",new[]{new ReviewedIssueDocument(currentPdf.Id,currentPdf.Hash,currentPdf.FileName,currentPdf.Category,currentPdf.Bytes)});
  var issueQa=s.CreateQualityItem(p.Id,"Synthetic issue hold","Correction","Pending");
  Invalid(()=>s.PrepareCandidateTransmittal(Guid.NewGuid(),intent,"Synthetic review","Reviewed",true),"actual project QA blocks candidate preparation");
  s.CloseQualityItem(p.Id,issueQa,"Resolved");
  var prep=s.PrepareCandidateTransmittal(Guid.NewGuid(),intent,"Synthetic review","Reviewed",true).Record;
  check(prep.History.Single().Event.State=="Prepared","actual project service records preparation separately");
  var secondQa=s.CreateQualityItem(p.Id,"Synthetic new hold","Correction","Pending");
  Invalid(()=>s.RecordCandidateTransmittalEvent(p.Id,prep.Id,Guid.NewGuid(),prep.History[0].Event.Hash,"IssueRecorded","Synthetic external issue","Reviewed",true),"actual issue gate rechecks new QA hold");
  s.CloseQualityItem(p.Id,secondQa,"Resolved");
  var issued=s.RecordCandidateTransmittalEvent(p.Id,prep.Id,Guid.NewGuid(),prep.History[0].Event.Hash,"IssueRecorded","Synthetic external issue","Reviewed",true).Entry;
  var delivered=s.RecordCandidateTransmittalEvent(p.Id,prep.Id,Guid.NewGuid(),issued.Event.Hash,"DeliveryRecorded","Synthetic delivery evidence","Reviewed",true).Entry;
  check(delivered.Event.State=="DeliveryRecorded"&&s.ReadCandidateTransmittal(p.Id,prep.Id).History.Count==3,"actual project service retains issue and delivery attestations");
  Sql("UPDATE role_permissions SET allowed=0 WHERE permission_code='Documents.View'");
  Denied(()=>s.ReadCandidateTransmittal(p.Id,prep.Id),"document permission revoked denies candidate read");
  Sql("UPDATE role_permissions SET allowed=1 WHERE permission_code='Documents.View'");
  // Witness is tested using actual history on this disposable database, not production restore.
  using(var source=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=path,ForeignKeys=true,Pooling=false}.ToString()))
  {
   source.Open();
   DocumentRegisterWitness Verify(SqliteConnection db){using var t=db.BeginTransaction(deferred:true);return DocumentRegisterVerification.Verify(db,t);}
   var original=Verify(source);
   check(original.Configurations==1&&original.FolderRevisions==1&&original.Transmittals==1&&original.Documents==1&&original.Events==3,"document witness reconciles actual project transmittal");
   using(var copy=new SqliteConnection("Data Source=:memory:;Foreign Keys=True;Pooling=False"))
   {copy.Open();source.BackupDatabase(copy);check(Verify(copy)==original,"SQLite backup preserves document register witness");}
   foreach(var fault in new[]{"INSERT INTO audit_events(actor_id,action,entity,entity_id) SELECT actor_id,'RecordTransmittalEvent','Project',entity_id FROM audit_events WHERE action='RecordTransmittalEvent' LIMIT 1",
    "DROP TRIGGER project_transmittal_events_no_delete;DELETE FROM project_transmittal_events WHERE sequence=3",
    "INSERT INTO project_transmittal_intents(project_id,operation_key,intent_hash,recipient,purpose,expected_documents) SELECT project_id,'0123456789abcdef0123456789abcdef','"+new string('a',64)+"','Synthetic','Unsealed',1 FROM project_transmittal_intents LIMIT 1"})
   {
    using var copy=new SqliteConnection("Data Source=:memory:;Foreign Keys=True;Pooling=False");copy.Open();source.BackupDatabase(copy);
    using(var cmd=copy.CreateCommand()){cmd.CommandText=fault;cmd.ExecuteNonQuery();}
    try{_=Verify(copy);throw new Exception("FAIL: document witness corruption");}
    catch(InvalidOperationException){check(true,"document witness rejects inconsistent history or orphan audit");}
   }
   check(Verify(source)==original,"document recovery witness tests leave source unchanged");
  }
  var recovery=Path.Combine(Path.GetDirectoryName(path)!,"document-register-recovery.db");
  var restored=Path.Combine(Path.GetDirectoryName(path)!,"document-register-restored.db");
  var saved=s.CreateRecoveryCopy(recovery);
  var result=s.RestoreRecoveryCopyToNewFile(recovery,saved.Sha256,restored);
  check(result.Sha256==saved.Sha256,"actual isolated recovery restores exact schema-49 bytes");
  DocumentRegisterWitness FileWitness(string file)
  {
   using var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=file,Mode=SqliteOpenMode.ReadOnly,ForeignKeys=true,Pooling=false}.ToString());db.Open();
   using var t=db.BeginTransaction(deferred:true);DocumentRegisterMigrationCandidate.VerifyLayout(db,t);return DocumentRegisterVerification.Verify(db,t);
  }
  check(FileWitness(path)==FileWitness(recovery)&&FileWitness(recovery)==FileWitness(restored),"actual recovery and restore preserve configuration folder and transmittal witness");
  var corrupted=Path.Combine(Path.GetDirectoryName(path)!,"document-register-corrupted.db");File.Copy(recovery,corrupted);
  using(var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=corrupted,Pooling=false}.ToString()))
  {db.Open();using var q=db.CreateCommand();q.CommandText="DROP TRIGGER project_transmittal_events_no_update;UPDATE project_transmittal_events SET note='Tampered'";q.ExecuteNonQuery();}
  var corruptedHash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(corrupted))).ToLowerInvariant();
  var refused=Path.Combine(Path.GetDirectoryName(path)!,"document-register-refused.db");
  try{s.RestoreRecoveryCopyToNewFile(corrupted,corruptedHash,refused);throw new Exception("FAIL: tampered document restore");}
  catch(InvalidOperationException){check(!File.Exists(refused),"actual restore rejects tampered document register even with recomputed file hash");}
  s.Logout();Denied(()=>s.ReadCandidateTransmittal(p.Id,prep.Id),"signed-out candidate history denied");


 }
}
