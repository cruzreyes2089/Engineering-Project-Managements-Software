using Microsoft.Data.Sqlite;
using PracticeManager.Core;
using PracticeManager.Persistence;
internal static class FolderTemplateRecommendationTests
{
 public static void Run(string root,string pw,Action<bool,string> check)
 {
  var path=Path.Combine(root,"template-recommendations.db");var s=new LocalPractice(path);s.Bootstrap("Synthetic recommendation firm","admin",pw,"AA");
  void Reject<T>(Action action,string label) where T:Exception
  {try{action();}catch(T){check(true,label);return;}throw new Exception("FAIL: "+label);}
  Reject<UnauthorizedAccessException>(()=>s.ReadFolderTemplateRules(),"anonymous recommendations denied");
  check(s.Login("admin",pw),"recommendation administrator authenticated");
  check(s.ReadFolderTemplateRules().Count==0,"no hardcoded project categories");
  var first=s.SaveNamedFolderChoice("Residential design",new[]{"Design","Design/Drawings"},0,true);
  var other=s.SaveNamedFolderChoice("Commercial",new[]{"Reports"},0,true);
  Reject<ArgumentException>(()=>s.SaveFolderTemplateRule("Residential",first.Revision,true,0,false),"recommendation confirmation required");
  var rule=s.SaveFolderTemplateRule("Residential",first.Revision,true,0,true);
  check(rule.CanRecommend&&rule.TemplateRevision==first.Revision,"business-defined recommendation recorded");
  Reject<ArgumentException>(()=>s.SaveFolderTemplateRule("residential",other.Revision,true,0,true),"duplicate category rejected");
  var crc=s.SaveFolderTemplateRule("General",0,true,0,true);
  check(crc.CanRecommend&&s.ReadFolderTemplateRules().Count==2,"optional CRC preset can be recommended");
  var client=s.SaveClient(new Client(0,"","Business / Company","Synthetic Client","","","","","","","","","","","Active"));
  var p=s.CreateProposal(new(client,"Residential","Site","Scope",100m));var h=ConflictReviewTests.Issue(s,p,1);var project=s.RecordProposalDecision(p,h,true,"Synthetic").ProjectId!.Value;
  check(s.ReadProjectTemplateRecommendations(project).Count==2&&!s.ReadProjectFolderPreview(project).Selection.Assigned,"recommendations do not automatically assign project");
  var selected=s.SelectProjectFolderTemplate(project,first.Revision,false,0,true);
  var fingerprint=s.ReadProjectFolderPreview(project).Plan.Fingerprint;
  var revised=s.SaveNamedFolderChoice(first.Name,new[]{"Revised Design"},first.Revision,true);
  check(!s.ReadFolderTemplateRules().Single(x=>x.Revision==rule.Revision).CanRecommend&&s.ReadProjectTemplateRecommendations(project).Count==1,
    "updated template requires recommendation re-review");
  Reject<ArgumentException>(()=>s.SaveFolderTemplateRule("Residential",first.Revision,true,rule.Revision,true),"obsolete template recommendation rejected");
  var repaired=s.SaveFolderTemplateRule("Residential",revised.Revision,true,rule.Revision,true);
  check(repaired.CanRecommend&&s.ReadProjectFolderPreview(project).Selection==selected&&s.ReadProjectFolderPreview(project).Plan.Fingerprint==fingerprint,"recommendation revision preserves existing project");
  Reject<ArgumentException>(()=>s.SaveFolderTemplateRule("Residential",other.Revision,true,rule.Revision,true),"stale recommendation change denied");
  var disabled=s.SaveFolderTemplateRule("Residential",repaired.TemplateRevision,false,repaired.Revision,true);
  check(!disabled.CanRecommend&&s.ReadProjectTemplateRecommendations(project).Single().Revision==crc.Revision,"disabled recommendation not suggested");
  check(s.SaveFolderTemplateRule("Residential",repaired.TemplateRevision,false,disabled.Revision,true)==disabled,"unchanged recommendation no-op");
  using(var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=path,Pooling=false,ForeignKeys=true}.ToString()))
  {
   db.Open();using var q=db.CreateCommand();
   q.CommandText="SELECT count(*) FROM folder_template_rules";check(Convert.ToInt64(q.ExecuteScalar())==4,"recommendation history retained");
   q.CommandText="DELETE FROM folder_template_rules";Reject<SqliteException>(()=>q.ExecuteNonQuery(),"recommendation history immutable");
   q.CommandText="UPDATE role_permissions SET allowed=0 WHERE permission_code='Documents.Restricted'";q.ExecuteNonQuery();
   Reject<UnauthorizedAccessException>(()=>s.ReadProjectTemplateRecommendations(project),"recommendation access rechecked");
   Reject<UnauthorizedAccessException>(()=>s.SaveFolderTemplateRule("Residential",0,true,disabled.Revision,true),"recommendation update permission enforced");
   q.CommandText="UPDATE role_permissions SET allowed=1 WHERE permission_code='Documents.Restricted'";q.ExecuteNonQuery();
  }
  s.Logout();var reopened=new LocalPractice(path);check(reopened.Login("admin",pw)&&reopened.ReadFolderTemplateRules().Count==2,"recommendation rules survive restart");
  check(reopened.ReadProjectFolderPreview(project).Plan.Fingerprint==fingerprint,"project remains pinned after reopen");
 }
}
