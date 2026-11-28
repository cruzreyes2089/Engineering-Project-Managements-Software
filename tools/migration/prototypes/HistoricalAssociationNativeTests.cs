// Not registered in the native smoke project. Requires reviewed association migration and service.
using Microsoft.Data.Sqlite;
using PracticeManager.Persistence;

internal static class HistoricalAssociationNativeTests
{
    // Invoke with the administrator and the disposable fixture after the existing
    // HistoricalAssociationTests.Run creates the same-client pending native project.
    public static void Run(LocalPractice s,string path,long native,Action<bool,string> check)
    {
        long Count(string sql)
        {
            using var c=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=path,Pooling=false}.ToString());
            c.Open();using var q=c.CreateCommand();q.CommandText=sql;return Convert.ToInt64(q.ExecuteScalar());
        }
        void Sql(string sql)
        {
            using var c=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=path,Pooling=false}.ToString());
            c.Open();using var q=c.CreateCommand();q.CommandText=sql;q.ExecuteNonQuery();
        }
        void Reject<T>(Action action,string label) where T:Exception
        {
            try{action();}catch(T){check(true,label);return;}check(false,label);
        }
        var before=s.ReadProjectHandoff(native);
        var number=s.PrepareHistoricalAssociation("P01",native).NativeProjectNumber;
        check(s.CanReviewHistoricalAssociations(),"Association administrator access");
        var found=s.FindHistoricalAssociationProjects("P01",number);
        check(found.Count==1&&found[0].ProjectId==native,"Association exact same-client search");
        check(s.FindHistoricalAssociationProjects("P02",number).Count==0,"Association search protects other-client project");
        check(s.ReadHistoricalAssociationHistory("P01").Count==0,"Association empty history");
        var preview=s.PrepareHistoricalAssociation("P01",native);
        Reject<ArgumentException>(()=>s.RecordHistoricalAssociation(preview,false,"Synthetic"),"Association explicit confirmation required");
        Reject<ArgumentException>(()=>s.RecordHistoricalAssociation(preview,true," "),"Association reason required");
        Reject<ArgumentException>(()=>s.RecordHistoricalAssociation(preview with{ReviewerId=long.MaxValue},true,"Synthetic"),"Association rejects altered reviewer");
        Sql("UPDATE role_permissions SET allowed=0 WHERE role_id=1 AND permission_code='Projects.View'");
        Reject<UnauthorizedAccessException>(()=>s.ReadHistoricalAssociationHistory("P01"),"Association history enforces project permission");
        Reject<UnauthorizedAccessException>(()=>s.RecordHistoricalAssociation(preview,true,"Synthetic"),"Association commit rechecks revoked project permission");
        Sql("UPDATE role_permissions SET allowed=1 WHERE role_id=1 AND permission_code='Projects.View'");
        Sql("CREATE TRIGGER test_association_audit_fail BEFORE INSERT ON audit_events WHEN NEW.action='RecordHistoricalAssociation' BEGIN SELECT RAISE(ABORT,'Synthetic audit failure'); END");
        try
        {
            Reject<SqliteException>(()=>s.RecordHistoricalAssociation(preview,true,"Synthetic"),"Association audit failure rejects write");
            check(Count("SELECT count(*) FROM historical_associations")==0,"Association audit failure rolls back link");
        }
        finally{Sql("DROP TRIGGER test_association_audit_fail");}
        var id=s.RecordHistoricalAssociation(preview,true,"Synthetic independent review");
        var history=s.ReadHistoricalAssociationHistory("P01");
        check(history.Count==1&&history[0].Id==id&&history[0].IsActive&&!history[0].WorkAuthorized,"Association active history and no authorization");
        check(Count("SELECT count(*) FROM audit_events WHERE action='RecordHistoricalAssociation' AND entity='HistoricalAssociation'")==1,"Association single native audit");
        Reject<ArgumentException>(()=>s.RecordHistoricalAssociation(preview,true,"Synthetic"),"Association duplicate rejected");
        var reversal=s.PrepareHistoricalAssociationReversal(id);
        Reject<ArgumentException>(()=>s.ReverseHistoricalAssociation(reversal,false,"Synthetic"),"Reversal explicit confirmation required");
        Sql("CREATE TRIGGER test_reversal_audit_fail BEFORE INSERT ON audit_events WHEN NEW.action='ReverseHistoricalAssociation' BEGIN SELECT RAISE(ABORT,'Synthetic audit failure'); END");
        try
        {
            Reject<SqliteException>(()=>s.ReverseHistoricalAssociation(reversal,true,"Synthetic"),"Reversal audit failure rejects write");
            check(Count("SELECT count(*) FROM historical_association_reversals")==0,"Reversal audit failure rolls back reversal");
        }
        finally{Sql("DROP TRIGGER test_reversal_audit_fail");}
        s.ReverseHistoricalAssociation(reversal,true,"Synthetic correction");
        check(!s.ReadHistoricalAssociationHistory("P01").Single().IsActive,"Reversal retains original association");
        check(Count("SELECT count(*) FROM audit_events WHERE action='ReverseHistoricalAssociation' AND entity='HistoricalAssociation'")==1,"Reversal single native audit");
        Reject<ArgumentException>(()=>s.ReverseHistoricalAssociation(reversal,true,"Synthetic"),"Reversal replay rejected");
        Reject<ArgumentException>(()=>s.RecordHistoricalAssociation(preview,true,"Synthetic"),"Link/reverse cycle invalidates original preview");
        var next=s.RecordHistoricalAssociation(s.PrepareHistoricalAssociation("P01",native),true,"Synthetic reassociation");
        history=s.ReadHistoricalAssociationHistory("P01");
        check(next!=id&&history.Count==2&&history.Count(x=>x.IsActive)==1,"Reassociation preserves immutable history");
        check(s.ReadProjectHandoff(native)==before&&Count("SELECT count(*) FROM project_start_authorizations")==0,
            "Association and reversal do not alter operational project or authorize start");
    }
}
