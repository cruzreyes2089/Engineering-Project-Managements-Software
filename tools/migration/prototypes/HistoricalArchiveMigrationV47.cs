// Staged source only. Included solely in the disposable schema-47 qualification copy.
using Microsoft.Data.Sqlite;

namespace PracticeManager.Persistence;

public sealed partial class LocalPractice
{
    private static void UpgradeToV47(SqliteConnection c,SqliteTransaction t)
    {
        using var stream=typeof(LocalPractice).Assembly.GetManifestResourceStream("PracticeManager.historical_project_archive.sql")
            ??throw new InvalidOperationException("Historical archive migration resource missing.");
        using var reader=new StreamReader(stream);
        Exec(c,t,reader.ReadToEnd());
        foreach(var grant in new[]{"HistoricalProjects.View","Administration.HistoricalProjectImport"})
        {
            Exec(c,t,"INSERT OR IGNORE INTO permissions(code) VALUES($g)",("$g",grant));
            Exec(c,t,"INSERT OR IGNORE INTO role_permissions(role_id,permission_code,allowed) SELECT id,$g,1 FROM roles WHERE name='Administrator'",("$g",grant));
        }
        Exec(c,t,"PRAGMA user_version=47");
    }
}
