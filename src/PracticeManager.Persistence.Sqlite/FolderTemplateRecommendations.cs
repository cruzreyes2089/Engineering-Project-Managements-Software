using Microsoft.Data.Sqlite;
namespace PracticeManager.Persistence;

public sealed record FolderTemplateRecommendation(long Revision,string Category,long TemplateRevision,bool Enabled,bool CurrentTemplate,string TemplateName)
{
 public string Display=>Category+" — "+(Enabled?TemplateName+(CurrentTemplate?"":" (review required)"):"disabled");
 public bool CanRecommend=>Enabled&&CurrentTemplate;
 public override string ToString()=>"[Folder template recommendation]";
}
public sealed partial class LocalPractice
{
 private static void UpgradeToV52(SqliteConnection c,SqliteTransaction t)
 {
  Exec(c,t,@"CREATE TABLE folder_template_rules(
   id INTEGER PRIMARY KEY,firm_id INTEGER NOT NULL REFERENCES firms(id) CHECK(firm_id=1),
   category TEXT NOT NULL COLLATE NOCASE CHECK(length(category) BETWEEN 1 AND 60),
   template_id INTEGER REFERENCES named_folder_templates(id),
   enabled INTEGER NOT NULL CHECK(enabled IN(0,1)),
   recorded_by INTEGER NOT NULL REFERENCES users(id),recorded_utc TEXT NOT NULL);
   CREATE INDEX folder_template_rule_category ON folder_template_rules(category,id);
   CREATE TRIGGER folder_template_rule_no_update BEFORE UPDATE ON folder_template_rules BEGIN SELECT RAISE(ABORT,'Recommendation history retained'); END;
   CREATE TRIGGER folder_template_rule_no_delete BEFORE DELETE ON folder_template_rules BEGIN SELECT RAISE(ABORT,'Recommendation history retained'); END;
   PRAGMA user_version=52;");
 }
 static string CategoryLabel(string value)
 {
  if(string.IsNullOrWhiteSpace(value)||value.Length>60||value!=value.Trim()||
     value.Any(x=>!char.IsAsciiLetterOrDigit(x)&&x is not (' ' or '-' or '_' or '/')))
   throw new ArgumentException("Use a category of 1–60 letters, numbers, spaces, hyphens, underscores or slashes.");
  return value;
 }
 static FolderTemplateRecommendation ReadRule(SqliteConnection c,SqliteTransaction t,long id)
 {
  string category;long template;bool enabled;
  using(var q=Command(c,t,"SELECT category,coalesce(template_id,0),enabled FROM folder_template_rules WHERE id=$i AND firm_id=1",("$i",id)))
  using(var reader=q.ExecuteReader())
  {
   if(!reader.Read())throw new ArgumentException("Recommendation revision unavailable.");
   category=CategoryLabel(reader.GetString(0));template=reader.GetInt64(1);enabled=reader.GetInt64(2)==1;
  }
  var choice=ReadChoice(c,t,template);
  var current=template==0||Convert.ToInt64(Scalar(c,t,"SELECT max(id) FROM named_folder_templates WHERE firm_id=1 AND name=$n COLLATE NOCASE",("$n",choice.Name)))==template;
  return new(id,category,template,enabled,current,choice.Name);
 }
 static IReadOnlyList<FolderTemplateRecommendation> Rules(SqliteConnection c,SqliteTransaction t)
 {
  var ids=new List<long>();
  using(var q=Command(c,t,"SELECT max(id) FROM folder_template_rules WHERE firm_id=1 GROUP BY category ORDER BY category"))
  using(var reader=q.ExecuteReader())while(reader.Read())ids.Add(reader.GetInt64(0));
  return ids.Select(id=>ReadRule(c,t,id)).ToList().AsReadOnly();
 }
 public IReadOnlyList<FolderTemplateRecommendation> ReadFolderTemplateRules()
 {
  using var c=Open();using var t=c.BeginTransaction();RequireStorageSettings(c,t);return Rules(c,t);
 }
 public IReadOnlyList<FolderTemplateRecommendation> ReadProjectTemplateRecommendations(long project)
 {
  using var c=Open();using var t=c.BeginTransaction();RequireStorageSettings(c,t);RequireDocuments(c,t,false);
  _=ProjectRow(c,t,project);return Rules(c,t).Where(x=>x.CanRecommend).ToList().AsReadOnly();
 }
 public FolderTemplateRecommendation SaveFolderTemplateRule(string category,long templateRevision,bool enabled,long expectedRevision,bool confirmed)
 {
  using var c=Open();using var t=c.BeginTransaction(deferred:false);var actor=RequireStorageSettings(c,t);
  if(!confirmed)throw new ArgumentException("Confirm the recommendation change.");
  category=CategoryLabel(category);
  var previous=Convert.ToInt64(Scalar(c,t,"SELECT coalesce(max(id),0) FROM folder_template_rules WHERE firm_id=1 AND category=$n COLLATE NOCASE",("$n",category)));
  if(previous!=expectedRevision)throw new ArgumentException("Recommendation changed or category exists. Reload.");
  var choice=ReadChoice(c,t,templateRevision);
  if(enabled&&choice.Revision>0&&Convert.ToInt64(Scalar(c,t,"SELECT max(id) FROM named_folder_templates WHERE firm_id=1 AND name=$n COLLATE NOCASE",("$n",choice.Name)))!=choice.Revision)
   throw new ArgumentException("Choose the current template revision.");
  if(previous>0)
  {
   var prior=ReadRule(c,t,previous);
   if(prior.Category==category&&prior.TemplateRevision==templateRevision&&prior.Enabled==enabled)return prior;
  }
  Exec(c,t,"INSERT INTO folder_template_rules(firm_id,category,template_id,enabled,recorded_by,recorded_utc) VALUES(1,$n,$i,$e,$a,$u)",
   ("$n",category),("$i",templateRevision==0?(object?)null:templateRevision),("$e",enabled?1:0),("$a",actor),("$u",UtcNow()));
  var id=Last(c,t);var saved=ReadRule(c,t,id);Audit(c,t,actor,"RecordFolderTemplateRecommendation","Firm",1);t.Commit();return saved;
 }
}
