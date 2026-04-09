namespace TomestonePhone.Shared.Models;

public sealed record ActiveCallSessionRecord(
    Guid Id,
    Guid ConversationId,
    Guid CallId,
    string DisplayName,
    bool IsGroup,
    DateTimeOffset StartedUtc,
    Guid StartedByAccountId,
    string StartedByDisplayName,
    IReadOnlyList<string> Participants,
    bool IncludesCurrentAccount,
    VoiceSessionInfo? VoiceSession);
