namespace PracticeManager.Core;
public sealed record EmployeeRate(long EmployeeId,DateOnly EffectiveOn,decimal BillingRate,decimal CostRate);
public sealed record BillingPolicy(long ProjectId,string Mode,decimal FixedLimit,decimal? TotalLimit,bool Expenses,string Evidence);
public sealed record InvoiceCandidate(long SourceId,string Employee,string Kind,DateOnly Date,string Description,int? Minutes,decimal? BillingRate,decimal? Amount,string Status);
public sealed record FixedFeeInput(string Description,decimal Amount);
public sealed record InvoiceDraftInput(Guid OperationKey,long ProjectId,DateOnly InvoiceDate,DateOnly DueDate,IReadOnlyList<FixedFeeInput> FixedFees,IReadOnlyList<long> Sources);
public sealed record InvoiceDraftLine(string Kind,long? SourceId,string Description,int? Minutes,decimal? BillingRate,decimal Amount);
public sealed record InvoiceDraftSummary(long Id,long ProjectId,string Number,DateOnly InvoiceDate,DateOnly DueDate,decimal Total,string State,string PayloadHash);
public sealed record InvoiceDraftDetail(InvoiceDraftSummary Summary,IReadOnlyList<InvoiceDraftLine> Lines);
