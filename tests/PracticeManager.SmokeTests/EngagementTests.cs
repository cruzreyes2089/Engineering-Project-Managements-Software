using Microsoft.Data.Sqlite;
using PracticeManager.Core;
using PracticeManager.Persistence;
internal static class EngagementTests
{
 public static async Task Run(string path,string pw,Action<bool,string> check)
 {
  var s=new LocalPractice(path);s.Bootstrap("Synthetic Engagement Firm","admin",pw,"CL");
  void Denied(Action action,string label){try{action();}catch(UnauthorizedAccessException){check(true,label);return;}throw new Exception("FAIL: "+label);}
  void Invalid(Action action,string label){try{action();}catch(ArgumentException){check(true,label);return;}throw new Exception("FAIL: "+label);}
  void Sql(string sql){using var c=new SqliteConnection($"Data Source={path}");c.Open();using var q=c.CreateCommand();q.CommandText=sql;q.ExecuteNonQuery();}
  Denied(()=>s.SearchProposals(),"engagement list requires authentication");
  check(s.Login("admin",pw)&&s.Can("Engagements.Edit"),"engagement admin grants");
  var client=s.SaveClient(new Client(0,"","Business / Company","Synthetic Client","","","","","","","","","","","Active"));
  var input=new ProposalInput(client,"Synthetic design","Synthetic site","Synthetic structural scope",1200m);
  Invalid(()=>s.CreateProposal(input with{Fee=1.001m}),"proposal rejects fractional cents");
  Invalid(()=>s.CreateProposal(input with{ClientId=999}),"proposal rejects missing client");
  var id=s.CreateProposal(input);var d=s.SearchProposals().Single();
  check(d.Id==id&&d.State=="Draft"&&d.Number=="P-000001"&&d.Revision==1,"proposal draft persisted");
  s.EditProposal(id,d.Revision,input with{Fee=1300m});d=s.SearchProposals().Single();
  Invalid(()=>s.EditProposal(id,1,input),"stale draft revision denied");
  check(d.Revision==2&&d.Fee==1300m,"draft edit increments revision");
  check(s.RecordProposalDecision(id,"not issued",true,"synthetic note").Outcome=="IssuedProposalRequired","acceptance requires issued snapshot");
  Invalid(()=>s.RecordProposalIssued(id,1),"issue rejects stale draft");
  var hash=ConflictReviewTests.Issue(s,id,2);
  check(hash.Length==64&&s.ReadIssuedProposalSnapshot(id).Contains("130000"),"issue stores frozen cents and hash");
  Invalid(()=>s.EditProposal(id,2,input),"issued proposal cannot edit");
  check(s.RecordProposalDecision(id,"wrong hash",true,"synthetic note").Outcome=="SnapshotMismatch","decision requires exact hash");
  var peer=new LocalPractice(path);check(peer.Login("admin",pw),"second engagement session");
  var results=await Task.WhenAll(Task.Run(()=>s.RecordProposalDecision(id,hash,true,"Synthetic acceptance")),Task.Run(()=>peer.RecordProposalDecision(id,hash,true,"Synthetic acceptance")));
  check(results.Count(x=>x.Outcome=="Accepted")==1&&results.Count(x=>x.Outcome=="AlreadyAccepted")==1&&results.Select(x=>x.ProjectId).Distinct().Count()==1,"concurrent acceptance creates one project");
  var project=s.SearchPendingProjects().Single();check(project.State=="Pending"&&project.SetupState=="NotStarted"&&project.ProposalId==id,"acceptance never authorizes start");
  check(s.RecordProposalDecision(id,hash,false,"Synthetic rejection").Outcome=="DecisionAlreadyRecorded","accepted proposal cannot reject");
  var rejected=s.CreateProposal(input);var rh=ConflictReviewTests.Issue(s,rejected,1);
  check(s.RecordProposalDecision(rejected,rh,false,"Synthetic rejection").Outcome=="Rejected"&&s.SearchPendingProjects().Count==1,"rejection creates no project");
  var failure=s.CreateProposal(input);var fh=ConflictReviewTests.Issue(s,failure,1);
  Sql("CREATE TRIGGER test_abort_project BEFORE INSERT ON projects BEGIN SELECT RAISE(ABORT,'synthetic failure'); END");
  try{s.RecordProposalDecision(failure,fh,true,"Synthetic acceptance");throw new Exception("FAIL: rollback injection");}catch(SqliteException){check(true,"injected project write failure");}
  check(s.SearchProposals().Single(x=>x.Id==failure).State=="Issued"&&s.SearchPendingProjects().Count==1,"acceptance rolls back completely");
  Sql("DROP TRIGGER test_abort_project");
  check(s.RecordProposalDecision(failure,fh,true,"Synthetic acceptance").ProjectNumber=="PRJ-000002","rollback preserves project sequence");
  Sql("UPDATE role_permissions SET allowed=0 WHERE permission_code='Engagements.Accept'");
  Denied(()=>s.RecordProposalDecision(id,hash,true,"Synthetic"),"revoked decision grant enforced");
  Sql("UPDATE role_permissions SET allowed=0 WHERE permission_code='Projects.View'");
  Denied(()=>s.SearchPendingProjects(),"project permission enforced");
  Sql("UPDATE role_permissions SET allowed=1 WHERE permission_code='Projects.View'");
  var reopened=new LocalPractice(path);check(reopened.Login("admin",pw)&&reopened.SearchPendingProjects().Count==2&&!reopened.Can("Engagements.Accept"),"history and revocation retained on restart");
 }
}
