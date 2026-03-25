namespace TomestonePhone.Shared.Models;

public sealed record AuditLogRecord(
    Guid Id,
    Guid? ActorAccountId,
    string ActorDisplayName,
    string EventType,
    string Summary,
    DateTimeOffset CreatedAtUtc);
