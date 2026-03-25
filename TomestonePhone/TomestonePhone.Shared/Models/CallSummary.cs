namespace TomestonePhone.Shared.Models;

public sealed record CallSummary(
    Guid Id,
    string DisplayName,
    CallKind Kind,
    DateTimeOffset StartedUtc,
    TimeSpan Duration,
    bool Missed);
