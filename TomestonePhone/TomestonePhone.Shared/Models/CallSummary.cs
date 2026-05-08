namespace TomestonePhone.Shared.Models;

public sealed record CallSummary(
    Guid Id,
    Guid ConversationId,
    string DisplayName,
    CallKind Kind,
    CallDirection Direction,
    DateTimeOffset StartedUtc,
    DateTimeOffset? EndedUtc,
    TimeSpan Duration,
    bool Missed,
    bool Acknowledged,
    VoiceSessionInfo? VoiceSession);
