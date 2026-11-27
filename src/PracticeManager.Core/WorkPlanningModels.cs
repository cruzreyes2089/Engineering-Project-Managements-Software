namespace PracticeManager.Core;
public sealed record ProjectPhase(long Id,long ProjectId,string Name,DateOnly Start,DateOnly End,int Revision);
public sealed record ProjectTaskPlan(long PhaseId,long EmployeeId,string Title,DateOnly Start,DateOnly End,int EstimateMinutes,long? ScopeChangeId=null);
public sealed record ProjectTask(long Id,long ProjectId,string ProjectNumber,string ProjectState,long PhaseId,string Phase,long EmployeeId,string Employee,string Title,DateOnly Start,DateOnly End,int EstimateMinutes,string State,int Revision,long? ScopeChangeId=null,string ScopeState="Base scope",string ConflictStatus="NotReviewed",DateOnly? StoppedOn=null,long? ClosureId=null,DateOnly? ClosedOn=null)
{
 public decimal EstimatedHours=>EstimateMinutes/60m;
 public string WorkEntryLabel=>StoppedOn.HasValue?$"{Title} — discontinued {StoppedOn.Value:yyyy-MM-dd} (historical only)":Title;
}
public sealed record TaskScopeOption(long? Id,string Label,string State);
