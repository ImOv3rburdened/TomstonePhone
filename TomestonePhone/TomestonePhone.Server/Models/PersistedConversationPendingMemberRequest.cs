namespace TomestonePhone.Server.Models;

public sealed class PersistedConversationPendingMemberRequest
{
    public Guid TargetAccountId { get; set; }

    public Guid RequestedByAccountId { get; set; }

    public DateTimeOffset RequestedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
