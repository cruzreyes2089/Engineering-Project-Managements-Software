namespace PracticeManager.Core;
public sealed record ProjectParty(long Id,long ClientId,long? ContactId,string Role,string PartyName,string ContactName,string State,string Note,string RecordedUtc,string? RetirementNote,string? RetiredUtc);
