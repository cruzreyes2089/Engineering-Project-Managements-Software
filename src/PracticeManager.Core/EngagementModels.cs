namespace PracticeManager.Core;
public sealed record ProposalInput(long ClientId,string Title,string Site,string Scope,decimal Fee);
public sealed record ProposalSummary(long Id,string Number,long ClientId,string ClientName,string Title,string Site,string Scope,decimal Fee,int Revision,string State,string? SnapshotHash);
public sealed record ProposalDecision(string Outcome,long? ProjectId=null,string? ProjectNumber=null);
public sealed record PendingProject(long Id,string Number,long ProposalId,string Title,string ClientName,string Site,string State,string SetupState);
public sealed record AgreementEvidence(long Id,long ProjectId,string IssuedHash,string FileName,string PdfHash,string RecordedUtc);
public sealed record ProjectHandoff(long ProjectId,string ProjectNumber,string State,string IssuedHash,AgreementEvidence? CurrentAgreement,bool HasAuthorization);
public sealed record ProjectSetupItem(string Item,bool Complete,string Note);
public sealed record ProjectSetupReview(long ProjectId,string State,IReadOnlyList<ProjectSetupItem> Items);
