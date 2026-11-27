namespace PracticeManager.Core;
public sealed record AttendanceSession(long Id,DateTimeOffset ClockIn,DateTimeOffset? ClockOut);
public sealed record WorkSubmissionInput(Guid OperationKey,long TaskId,DateOnly Date,string Kind,int? Minutes,decimal? Amount,string? Category,string Description);
public sealed record OwnWorkSubmission(long Id,string ProjectNumber,string Task,string Kind,DateOnly Date,int? Minutes,decimal? Amount,string? Category,string Description,string ReviewState,long TaskId=0,long? ReplacesId=null,long? ReplacementId=null);
public sealed record WorkReviewRow(long Id,string Employee,string ProjectNumber,string Task,string Kind,DateOnly Date,int? Minutes,decimal? Amount,string? Category,string Description,string ReviewState,long TaskId=0,long? ReplacesId=null,long? ReplacementId=null);
public sealed record WorkBillingRow(long Id,string Employee,string ProjectNumber,string Task,string Kind,DateOnly Date,int? Minutes,decimal? Amount,string? Category,string Description,string Classification);
