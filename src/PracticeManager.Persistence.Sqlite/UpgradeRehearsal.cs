namespace PracticeManager.Persistence;
public sealed record UpgradeRehearsalResult(int SourceSchema,int TargetSchema,string SourceSha256,long Clients,long Contacts,long Users,bool SourceUnchanged);
public static class UpgradeRehearsal
{
 public static UpgradeRehearsalResult Run(string source,string expectedSha256,bool confirmed)=>throw new NotImplementedException("Isolated upgrade rehearsal not implemented.");
}
