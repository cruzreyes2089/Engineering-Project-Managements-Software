namespace PracticeManager.Core;
public sealed record ProjectDocument(long Id,long ProjectId,long? SupersedesId,string Classification,string Category,string FileName,string Hash,long Bytes,string RecordedUtc,bool IsCurrent);
public sealed record ProjectDocumentContent(ProjectDocument Document,byte[] Bytes)
{
 public override string ToString()=>"[Project document content]";
}
public sealed record QualityItem(long Id,long ProjectId,string Title,string Severity,string Description,string State,string RecordedUtc,string? Resolution);
public sealed record QualityNote(string Note,string RecordedUtc);
public sealed record ScopeChange(long Id,long ProjectId,string Title,string Scope,string State,string RecordedUtc,string? DecisionEvidence);
