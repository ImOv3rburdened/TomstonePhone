namespace TomestonePhone.Server.Models;

public sealed class PersistedAuditLog
{
    public Guid Id { get; set; }

    public Guid? ActorAccountId { get; set; }

    public string ActorDisplayName { get; set; } = string.Empty;

    public string EventType { get; set; } = string.Empty;

    public string Summary { get; set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; set; }
}
