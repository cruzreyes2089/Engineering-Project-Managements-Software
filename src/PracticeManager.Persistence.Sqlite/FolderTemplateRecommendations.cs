using Microsoft.Data.Sqlite;
namespace PracticeManager.Persistence;

// Historical migration only; category recommendations were retired in schema 53.
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
}
