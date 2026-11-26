namespace PracticeManager.Core;
public sealed record ProposalInput(long ClientId,string Title,string Site,string Scope,decimal Fee);
public sealed record ProposalSummary(long Id,string Number,long ClientId,string ClientName,string Title,string Site,string Scope,decimal Fee,int Revision,string State,string? SnapshotHash);
public sealed record ProposalDecision(string Outcome,long? ProjectId=null,string? ProjectNumber=null);
public sealed record PendingProject(long Id,string Number,long ProposalId,string Title,string ClientName,string Site,string State,string SetupState);
