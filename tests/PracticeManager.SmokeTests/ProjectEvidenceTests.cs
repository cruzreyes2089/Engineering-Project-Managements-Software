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
  (long Id,string Hash) Project(string title){var p=s.CreateProposal(new(client,title,"Site","Scope",100m));var h=s.RecordProposalIssued(p,1);return(s.RecordProposalDecision(p,h,true,"Synthetic").ProjectId!.Value,h);}
  var p=Project("First");var other=Project("Other");var bytes="Synthetic document"u8.ToArray();var key=Guid.NewGuid();
  Invalid(()=>s.RegisterProjectDocument(p.Id,Guid.NewGuid(),null,"Production","Reports","../report.txt",bytes),"document traversal rejected");
  Invalid(()=>s.RegisterProjectDocument(p.Id,Guid.NewGuid(),null,"Production","Reports","CON.txt",bytes),"reserved name rejected");
  Invalid(()=>s.RegisterProjectDocument(p.Id,Guid.NewGuid(),null,"Production","Reports","program.exe",bytes),"executable document rejected");
  var id=s.RegisterProjectDocument(p.Id,key,null,"Production","Reports","report.txt",bytes);
  check(s.RegisterProjectDocument(p.Id,key,null,"Production","Reports","report.txt",bytes)==id,"document idempotent by operation and hash");
  Invalid(()=>s.RegisterProjectDocument(p.Id,key,null,"Production","Reports","report.txt","Changed"u8.ToArray()),"document operation conflict");
  var old=s.ReadProjectDocuments(p.Id).Single();
  var successor=s.RegisterProjectDocument(p.Id,Guid.NewGuid(),id,"Production","Reports","report.txt","Revision"u8.ToArray());
  check(s.ReadProjectDocuments(p.Id).Single().Id==successor&&s.ReadProjectDocuments(p.Id,false).Count==2,"current document and history separate");
  Invalid(()=>s.RegisterProjectDocument(p.Id,Guid.NewGuid(),id,"Production","Reports","report.txt",bytes),"stale version cannot branch");
  Invalid(()=>s.RegisterProjectDocument(other.Id,Guid.NewGuid(),successor,"Production","Reports","report.txt",bytes),"cross-project version denied");
  var restricted=s.RegisterProjectDocument(p.Id,Guid.NewGuid(),null,"Restricted","Contracts","agreement.txt",bytes);
  var rd=s.ReadProjectDocuments(p.Id).Single(x=>x.Id==restricted);
  check(s.RetrieveProjectDocument(p.Id,id,old.Hash).Bytes.SequenceEqual(bytes),"old immutable document verified");
  Denied(()=>s.RetrieveProjectDocument(other.Id,id,old.Hash),"cross-project retrieval denied");
  Sql("UPDATE role_permissions SET allowed=0 WHERE permission_code='Documents.Restricted'");
  check(s.ReadProjectDocuments(p.Id,false).All(x=>x.Id!=restricted),"restricted document omitted from list");
  Denied(()=>s.RetrieveProjectDocument(p.Id,restricted,rd.Hash),"restricted document direct retrieval denied");
  Denied(()=>s.RegisterProjectDocument(p.Id,Guid.NewGuid(),null,"Restricted","Contracts","secret.txt",bytes),"restricted write denied");
  var q=s.CreateQualityItem(p.Id,"Synthetic QA","Correction","Review calculation");s.AddQualityNote(p.Id,q,"Synthetic review note");
  check(s.ReadQualityNotes(p.Id,q).Count==1&&s.ReadQualityItems(p.Id).Single().State=="Open","quality history retained");
  Invalid(()=>s.AddQualityNote(other.Id,q,"wrong project"),"quality scope enforced");
  check(s.CloseQualityItem(p.Id,q,"Resolved")=="Closed"&&s.CloseQualityItem(p.Id,q,"Changed")=="AlreadyClosed","quality closure immutable");
  Invalid(()=>s.AddQualityNote(p.Id,q,"After closure"),"closed quality history read-only");
  var change=s.ProposeScopeChange(p.Id,"Extra work","Synthetic additional scope");
  Invalid(()=>s.DecideScopeChange(p.Id,change,true,"Reviewed",true),"pending project cannot authorize additional work");
  var a=s.RecordSignedAgreement(p.Id,p.Hash,"signed.pdf","%PDF-1.7\nSynthetic\n%%EOF"u8.ToArray(),"Reviewed",true);s.AuthorizeProjectStart(p.Id,p.Hash,a.Id,a.PdfHash,true,"Synthetic");
  Invalid(()=>s.DecideScopeChange(p.Id,change,true,"Reviewed",false),"explicit client review required");
  check(s.DecideScopeChange(p.Id,change,true,"Written synthetic approval",true)=="Authorized"&&s.ReadScopeChanges(p.Id).Single().State=="Authorized","scope approval recorded separately");
  check(s.DecideScopeChange(p.Id,change,false,"Changed",false)=="AlreadyAuthorized","scope authorization not silently reversed");
 }
}
