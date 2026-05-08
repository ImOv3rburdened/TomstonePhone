namespace TomestonePhone.Server.Models;

public sealed class PersistedConversationMember
{
    public Guid AccountId { get; set; }

    public string Role { get; set; } = "Member";

    public DateTimeOffset JoinedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? RemovedAtUtc { get; set; }

    public DateTimeOffset? HiddenAtUtc { get; set; }
}
