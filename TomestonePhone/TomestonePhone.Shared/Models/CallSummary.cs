namespace TomestonePhone.Shared.Models;

public sealed record CallSummary(
    Guid Id,
    string DisplayName,
    CallKind Kind,
    CallDirection Direction,
    DateTimeOffset StartedUtc,
    DateTimeOffset? EndedUtc,
    TimeSpan Duration,
    bool Missed,
    bool Acknowledged,
    VoiceSessionInfo? VoiceSession);
