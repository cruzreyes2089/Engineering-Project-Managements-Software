namespace PracticeManager.Core;
public sealed record OverheadTimeInput(Guid OperationKey,DateOnly Date,int Minutes,string Category,string Description);
public sealed record OwnOverheadTime(long Id,DateOnly Date,int Minutes,string Category,string Description,string ReviewState);
public sealed record OverheadReviewRow(long Id,string Employee,DateOnly Date,int Minutes,string Category,string Description,string ReviewState);
