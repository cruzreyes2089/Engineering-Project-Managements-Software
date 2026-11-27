namespace PracticeManager.Core;
public sealed record DevelopmentWorkspace(string DatabasePath,string? AcceptanceId)
{
 public bool IsAcceptance=>AcceptanceId!=null;
 public static DevelopmentWorkspace Resolve(string localAppData,IReadOnlyList<string> arguments)=>throw new NotImplementedException("Isolated acceptance workspace not implemented.");
}
